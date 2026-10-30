using System.Globalization;
using System.Text;
using System.Text.Json;

namespace gflat.Tests;

public sealed class StandardJsonPointerTests
{
    private const string Allocator = """
        using Standard; using Standard::Json; using Standard::Encoding; using Standard::Memory;extern void*? malloc(nuint size);extern void free(void* storage);
        int attempts=0;int allocated=0;int released=0;int failAt=0;
        namespace Memory{
            public replace void*? Allocate(nuint size){attempts++;if(attempts==failAt)return null;void*? result=malloc(size);if(result!=null)allocated++;return result;}
            public replace void Free(void* storage)nothrow{released++;free(storage);}
        }
        """;
    private const string Decode = """
        uint Hex(char c)nothrow{if(c>='0'&&c<='9')return(uint)(c-'0');return(uint)(c-'A')+10u;}
        byte ReadByte(readonly(char)* packed,ref nuint offset)nothrow{
            uint value=Hex(packed[offset])*16u+Hex(packed[offset+1]);offset+=2;return(byte)value;
        }
        nuint ReadSize(readonly(char)* packed,ref nuint offset)nothrow{
            nuint size=0;for(nuint i=0;i<4;i++)size=size|((nuint)ReadByte(packed,ref offset)<<(i*8));return size;
        }
        nuint ReadBytes(readonly(char)* packed,ref nuint offset,char* output)nothrow{
            nuint size=ReadSize(packed,ref offset);for(nuint i=0;i<size;i++)output[i]=(char)ReadByte(packed,ref offset);return size;
        }
        ulong Hash(readonly(char)* bytes,nuint size)nothrow{
            ulong hash=0ul;for(nuint i=0;i<size;i++)hash=hash*131ul+(ulong)(byte)bytes[i];return hash;
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PlainPointersMatchIndependentJsonDocumentOracleAndPreserveBorrowedStorage(int optimization)
    {
        const string rfc="""{"foo":["bar","baz"],"":0,"a/b":1,"c%d":2,"e^f":3,"g|h":4,"i\\j":5,"k\"l":6," ":7,"m~n":8}""";
        var rows=new List<(string Document,byte[] Pointer)>();
        void Add(string document,params string[] pointers){foreach(string pointer in pointers)rows.Add((document,Encoding.UTF8.GetBytes(pointer)));}
        Add(rfc,"","/foo","/foo/0","/foo/1","/","/a~1b","/c%d","/e^f","/g|h","/i\\j","/k\"l","/ ","/m~0n","/foo/2","/missing");
        Add("  {\"a\":{\"b\":[{\"\":\"x\"},null,true]}} \n","","/a/b/0/","/a/b/1","/a/b/2","/a/b/0//x");
        Add("{\"~1\":1,\"/\":2,\"\":{\"\":3},\"a\\u0000b\":4,\"😀\":5,\"é\":6}","/~01","/~1","//","/a\0b","/😀","/é","/e\u0301");
        Add("{\"a\":1,\"\\u0061\":2}","/a","");
        Add("{\"a\":{\"x\":1},\"a\":{\"x\":2},\"b\":3}","/a/x","/b");
        Add("[0,1,[2],{\"x\":3}]","/0","/2/0","/3/x","/00","/01","/-","/+1","/1.0","/18446744073709551616","/999999999999999999999999999999999999");
        Add("123","","/0");Add("\"quoted\"","","/x");Add("null","","/");
        Add(rfc,"foo","#/foo","/~","/~2","/missing/~2");
        rows.Add((rfc,[47,0xff]));rows.Add((rfc,[47,0xc0,0x80]));rows.Add((rfc,[47,0xed,0xa0,0x80]));
        var random=new Random(6901);
        for(int i=0;i<16;i++){
            string key="key/"+i+"~",document=JsonSerializer.Serialize(new Dictionary<string,int[]>{{key,Enumerable.Range(0,random.Next(1,8)).Select(_=>random.Next()).ToArray()}});
            Add(document,"/"+key.Replace("~","~0").Replace("/","~1")+"/0");
        }
        string packed=Pack(writer=>{
            writer.Write((uint)rows.Count);
            foreach(var row in rows){byte[]? expected=Resolve(row.Document,row.Pointer);Write(writer,Encoding.UTF8.GetBytes(row.Document));Write(writer,row.Pointer);Write(writer,expected??[]);writer.Write((byte)(expected is null?0:1));}
        });
        Run(Allocator+Decode+$$$"""
            int main(){readonly(char)* packed="{{{packed}}}";nuint offset=0;nuint rows=ReadSize(packed,ref offset);
                char[2048] document;char[512] path;char[2048] expected;
                for(nuint row=0;row<rows;row++){
                    nuint size=ReadBytes(packed,ref offset,document);nuint count=ReadBytes(packed,ref offset,path);
                    readonly(char)* documentData=document;
                    nuint expectedSize=ReadBytes(packed,ref offset,expected);bool found=ReadByte(packed,ref offset)!=0;
                    ulong before=Hash(document,size);ulong pathBefore=Hash(path,count);
                    Span<readonly(char)> result=new("sentinel",8);readonly(char)* old=result.Data!;
                    bool actual=JsonPointer::TryResolve(new(document,size),new(path,count),ref result);
                    if(actual!=found)return 1;if(Hash(document,size)!=before||Hash(path,count)!=pathBefore)return 2;
                    if(!found){if(result.Data!=old||result.Length!=8)return 3;}
                    else{
                        if(result.Length!=expectedSize||result.Data!<documentData||result.Data!+result.Length>documentData+size)return 4;
                        for(nuint i=0;i<expectedSize;i++)if(result.Data![i]!=expected[i])return 5;
                    }
                    if(allocated!=released)return 6;
                }
                Span<readonly(char)> reused=new(" {\"a\":1} ",9);
                if(!JsonPointer::TryResolve(reused,new("/a",2),ref reused)||reused.Length!=1||reused.Data![0]!='1')return 7;
                Span<readonly(char)> absent=new();Span<readonly(char)> root=new();
                if(!JsonPointer::TryResolve(new(" [] ",4),absent,ref root)||root.Length!=2)return 8;
                readonly(char)* exact=" {\"a\":[1,2],\"b\":1} ";
                if(!JsonPointer::TryResolve(new(exact,19),new("/a",2),ref root)||root.Data!=exact+6||root.Length!=5)return 9;
                if(!JsonPointer::TryResolve(new(exact,19),new("/b",2),ref root)||root.Data!=exact+16||root.Length!=1)return 10;
                if(!JsonPointer::TryResolve(new(exact,19),absent,ref root)||root.Data!=exact+1||root.Length!=17)return 11;
                return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EntireDocumentIsValidatedBeforeEvenMalformedPointersAndOffsetsRemainAbsolute(int optimization)
    {
        byte[][] invalid=[[]," "u8.ToArray()," {\"a\":1} trailing"u8.ToArray(),"{\"a\":1,\"b\":[0,]}"u8.ToArray(),"[1,{\"x\":\"\\uD800\"}]"u8.ToArray(),[91,34,255,34,93]];
        string packed=Pack(writer=>{writer.Write((uint)invalid.Length);foreach(byte[] bytes in invalid)Write(writer,bytes);});
        Run(Allocator+Decode+$$$"""
            int main(){readonly(char)* packed="{{{packed}}}";nuint offset=0;nuint rows=ReadSize(packed,ref offset);char[256] document;
                for(nuint row=0;row<rows;row++){
                    nuint size=ReadBytes(packed,ref offset,document);nuint expected=0;bool caught=false;
                    {JsonReader reader=new(document,size);try{while(reader.Read()){}}catch(ref JsonParseException error){expected=error.Offset;caught=true;}}
                    if(!caught)return 1;
                    for(int kind=0;kind<2;kind++){
                        Span<readonly(char)> result=new("sentinel",8);readonly(char)* old=result.Data!;caught=false;
                        Span<readonly(char)> pointer=new("/~2",3);if(kind==1)pointer=new("/a",2);
                        try{JsonPointer::TryResolve(new(document,size),pointer,ref result);}
                        catch(ref JsonParseException error){if(error.Offset!=expected)return 2;caught=true;}
                        if(!caught||result.Data!=old||result.Length!=8)return 3;
                    }
                    if(allocated!=released)return 4;
                }
                Span<readonly(char)> empty=new();Span<readonly(char)> result=new("sentinel",8);bool caught=false;
                try{JsonPointer::TryResolve(empty,empty,ref result);}catch(ref JsonParseException error){if(error.Offset!=0)return 5;caught=true;}
                if(!caught||result.Length!=8||allocated!=released)return 6;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryMeasuredAllocationFailurePreservesOutputAndReleasesTemporaryOwners(int optimization)
    {
        string key=new('x',96),document="{\"a\":{\""+key+"\":[[[[[[[[[1]]]]]]]]]}}",path="/a/"+key+"/0/0/0/0/0/0/0/0/0";
        Assert.NotNull(Resolve(document,Encoding.UTF8.GetBytes(path)));
        string packed=Pack(writer=>{Write(writer,Encoding.UTF8.GetBytes(document));Write(writer,Encoding.UTF8.GetBytes(path));});
        Run(Allocator+Decode+$$$"""
            int main(){readonly(char)* packed="{{{packed}}}";nuint offset=0;char[1024] document;char[512] path;
                nuint size=ReadBytes(packed,ref offset,document);nuint count=ReadBytes(packed,ref offset,path);
                ulong before=Hash(document,size);ulong pathBefore=Hash(path,count);
                Span<readonly(char)> result=new("sentinel",8);readonly(char)* old=result.Data!;
                attempts=0;if(!JsonPointer::TryResolve(new(document,size),new(path,count),ref result))return 1;
                int measured=attempts;if(measured<3||allocated!=released||result.Length!=1||result.Data![0]!='1')return 2;
                for(int point=1;point<=measured;point++){
                    result=new(old,8);attempts=0;failAt=point;bool caught=false;
                    try{JsonPointer::TryResolve(new(document,size),new(path,count),ref result);}
                    catch(ref BadAllocException error){caught=true;}
                    failAt=0;
                    if(!caught||result.Data!=old||result.Length!=8||allocated!=released)return 3;
                    if(Hash(document,size)!=before||Hash(path,count)!=pathBefore)return 4;
                    if(!JsonPointer::TryResolve(new(document,size),new(path,count),ref result)||result.Length!=1||result.Data![0]!='1')return 5;
                    if(allocated!=released)return 6;
                }
                return 0;
            }
            """,optimization);
    }

    private static byte[]? Resolve(string document,byte[] pointer)
    {
        using JsonDocument parsed=JsonDocument.Parse(document);
        string path;try{path=new UTF8Encoding(false,true).GetString(pointer);}catch(DecoderFallbackException){return null;}
        if(path.Length!=0&&path[0]!='/')return null;
        for(int i=0;i<path.Length;i++)if(path[i]=='~'&&(++i==path.Length||path[i] is not ('0' or '1')))return null;
        JsonElement value=parsed.RootElement;
        foreach(string encoded in path.Split('/').Skip(1)){
            string token=encoded.Replace("~1","/").Replace("~0","~");
            if(value.ValueKind==JsonValueKind.Object){
                var matches=value.EnumerateObject().Where(member=>member.Name==token).ToArray();
                if(matches.Length!=1)return null;value=matches[0].Value;
            }else if(value.ValueKind==JsonValueKind.Array){
                if(token.Length==0||(token.Length>1&&token[0]=='0')||token.Any(c=>c<'0'||c>'9')||!ulong.TryParse(token,NumberStyles.None,CultureInfo.InvariantCulture,out ulong index)||index>=(ulong)value.GetArrayLength())return null;
                value=value[(int)index];
            }else return null;
        }
        return Encoding.UTF8.GetBytes(value.GetRawText());
    }
    private static void Write(BinaryWriter writer,byte[] bytes){writer.Write((uint)bytes.Length);writer.Write(bytes);}
    private static string Pack(Action<BinaryWriter> write){using var stream=new MemoryStream();using(var writer=new BinaryWriter(stream,Encoding.UTF8,true))write(writer);return Convert.ToHexString(stream.ToArray());}
    private static IEnumerable<SourceFile> Sources(string code)
        =>Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Standard"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("main.gf",code));
    private static void Run(string code,int optimization)=>Assert.Equal(0,CompilerTestHelper.Run(Sources(code),optimization).ExitCode);
}
