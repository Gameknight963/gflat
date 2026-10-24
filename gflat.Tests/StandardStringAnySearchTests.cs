using System.Text.RegularExpressions;

namespace gflat.Tests;

public sealed class StandardStringAnySearchTests
{
    private const string Counters="""
        using std;extern void*? malloc(nuint bytes);extern void free(void* storage);
        int attempts=0;int allocated=0;int released=0;bool deny=false;
        namespace Memory {
            public replace void*? Allocate(nuint bytes){attempts++;if(deny)return null;void*? p=malloc(bytes);if(p!=null)allocated++;return p;}
            public replace void Free(void* storage)nothrow{released++;free(storage);}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Latin1HostOracleChecksEveryStartAndBothCountedAndCStringSetsWithoutAllocation(int optimization)
    {
        byte[] every=Enumerable.Range(0,256).Select(value=>(byte)value).ToArray();
        var rows=new List<(byte[] Text,byte[] Set)> {([],[]),([],every),(every,[]),(every,every),
            (every,[255,0,128,255]),([65,0,66,65,255],[65,65,0,66]),([1,2,3],[255]),([128,255,128],[255,128])};
        var random=new Random(861293);
        for(int sample=0;sample<32;sample++) {
            byte[] text=new byte[random.Next(257)],set=new byte[random.Next(65)];random.NextBytes(text);random.NextBytes(set);
            if(sample%4==0&&set.Length!=0)set[^1]=0;rows.Add((text,set));
        }
        using var manifest=new MemoryStream();
        using(var writer=new BinaryWriter(manifest,System.Text.Encoding.UTF8,leaveOpen:true))foreach(var row in rows) {
            string text=new(row.Text.Select(value=>(char)value).ToArray());char[] set=row.Set.Select(value=>(char)value).ToArray();
            writer.Write((ushort)row.Text.Length);writer.Write((ushort)row.Set.Length);writer.Write(row.Text);writer.Write(row.Set);
            WriteIndex(writer,text.LastIndexOfAny(set));
            for(int start=0;start<=text.Length;start++)WriteIndex(writer,text.IndexOfAny(set,start));
            int end=Array.IndexOf(row.Set,(byte)0);if(end<0)end=row.Set.Length;
            char[] cstring=set.Take(end).ToArray();WriteIndex(writer,text.IndexOfAny(cstring));WriteIndex(writer,text.LastIndexOfAny(cstring));
        }
        string hex=Convert.ToHexString(manifest.ToArray());
        Run(Counters+$$"""
            int Digit(char value)nothrow{if(value<='9')return value-'0';return value-'A'+10;}
            int ReadByte(readonly(char)* text,ref nuint position)nothrow{int value=(Digit(text[position])<<4)|Digit(text[position+1]);position+=2;return value;}
            int ReadWord(readonly(char)* text,ref nuint position)nothrow{int low=ReadByte(text,ref position);return low|(ReadByte(text,ref position)<<8);}
            nuint ReadIndex(readonly(char)* text,ref nuint position)nothrow{int value=ReadWord(text,ref position);if(value==65535)return String::NotFound;return (nuint)value;}
            int main(){
                readonly(char)* manifest="{{hex}}";nuint position=0;char[256] raw;char[257] values;
                for(int row=0;row<{{rows.Count}};row++){
                    nuint length=(nuint)ReadWord(manifest,ref position);nuint count=(nuint)ReadWord(manifest,ref position);
                    nuint rawStart=position;
                    for(nuint index=0;index<length;index++)raw[index]=(char)ReadByte(manifest,ref position);
                    nuint valuesStart=position;
                    for(nuint index=0;index<count;index++)values[index]=(char)ReadByte(manifest,ref position);
                    values[count]='\0';
                    {
                        String text=String::From(raw,length);immutable(String)* view=&text;
                        int before=attempts;deny=true;
                        if(view.LastIndexOfAny(values,count)!=ReadIndex(manifest,ref position))return 1;
                        for(nuint start=0;start<=length;start++)if(view.IndexOfAny(values,count,start)!=ReadIndex(manifest,ref position))return 2;
                        if(view.IndexOfAny(values)!=ReadIndex(manifest,ref position)||view.LastIndexOfAny(values)!=ReadIndex(manifest,ref position))return 3;
                        if(attempts!=before)return 4;deny=false;
                        for(nuint index=0;index<length;index++){
                            char expected=(char)((Digit(manifest[rawStart+index*2])<<4)|Digit(manifest[rawStart+index*2+1]));
                            if(text[index]!=expected||raw[index]!=expected)return 5;
                        }
                        for(nuint index=0;index<count;index++)if(values[index]!=(char)((Digit(manifest[valuesStart+index*2])<<4)|Digit(manifest[valuesStart+index*2+1])))return 6;
                        if(values[count]!='\0')return 7;
                    }
                }
                if(position!={{hex.Length}}||allocated!=released)return 8;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EmptySetsSelfBorrowingAllBytesStartGuardsAndLastSearchNothrowRetainStringContracts(int optimization)
    {
        string program=Counters+"""
            bool Observe(immutable(String)* text,readonly(char)* values,nuint count)nothrow{
                return text.LastIndexOfAny(values,count)!=String::NotFound&&text.LastIndexOfAny(values)!=String::NotFound;
            }
            int main(){
                {
                    String empty=new();immutable(String)* none=&empty;
                    char[256] bytes;for(int index=0;index<256;index++)bytes[index]=(char)index;
                    String text=String::From(bytes,256);immutable(String)* view=&text;
                    int before=attempts;deny=true;
                    if(none.IndexOfAny((readonly(char)*)1,0)!=String::NotFound||none.LastIndexOfAny((readonly(char)*)1,0)!=String::NotFound)return 1;
                    if(none.IndexOfAny((readonly(char)*)1,123)!=String::NotFound||none.LastIndexOfAny((readonly(char)*)1,123)!=String::NotFound)return 2;
                    if(view.IndexOfAny((readonly(char)*)1,0)!=String::NotFound||view.LastIndexOfAny((readonly(char)*)1,0)!=String::NotFound)return 3;
                    if(view.IndexOfAny((readonly(char)*)1,123,256)!=String::NotFound)return 4;
                    for(nuint index=0;index<256;index++)if(view.IndexOfAny(view.Data+index,1)!=index||view.LastIndexOfAny(view.Data+index,1)!=index)return 5;
                    nuint[7] offsets=[0,1,127,128,254,255,256];
                    for(nuint row=0;row<7;row++){
                        nuint offset=offsets[row];nuint count=256-offset;nuint last=255;if(count==0)last=String::NotFound;
                        if(view.LastIndexOfAny(view.Data+offset,count)!=last)return 6;
                        for(nuint column=0;column<7;column++){
                            nuint start=offsets[column];nuint expected=start;if(offset>expected)expected=offset;
                            if(count==0||start==256)expected=String::NotFound;
                            if(view.IndexOfAny(view.Data+offset,count,start)!=expected)return 7;
                        }
                    }
                    if(view.IndexOfAny(view.Data)!=String::NotFound||view.LastIndexOfAny(view.Data)!=String::NotFound)return 8;
                    if(view.IndexOfAny(view.Data+1)!=1||view.LastIndexOfAny(view.Data+1)!=255||!Observe(view,view.Data+1,255))return 9;
                    if(attempts!=before)return 10;deny=false;
                    for(nuint index=0;index<256;index++)if(view.Data[index]!=bytes[index]||bytes[index]!=(char)index)return 11;
                    for(int mode=0;mode<2;mode++){
                        nuint start=257;if(mode==1)start=(nuint)0-1;bool caught=false;deny=true;
                        try{view.IndexOfAny((readonly(char)*)1,0,start);}catch(ref Exception error){if(!CString::Equals(error.GetMessage(),"String search start out of range"))return 12;caught=true;}
                        deny=false;if(!caught||text.Length!=256)return 13;
                        for(nuint index=0;index<256;index++)if(view.Data[index]!=(char)index)return 14;
                    }
                }
                if(allocated!=released)return 15;return 0;
            }
            """;
        string ir=Compiler.Emit(Sources(program));
        Match observe=Regex.Match(ir,@"(?ms)^define [^\r\n]*@gflat\$Observe\([^\r\n]*\{\r?\n(?<body>.*?)^\}");
        Assert.True(observe.Success);Assert.Contains("nounwind",observe.Value);Assert.DoesNotContain("invoke ",observe.Value);
        string[] last=ir.Split('\n').Where(line=>line.StartsWith("define ")&&line.Contains("$String$LastIndexOfAny",StringComparison.Ordinal)).ToArray();
        Assert.Equal(2,last.Length);Assert.All(last,line=>Assert.Contains("nounwind",line));
        Run(program,optimization);
    }

    private static void WriteIndex(BinaryWriter writer,int value)=>writer.Write(value<0?ushort.MaxValue:(ushort)value);
    private static IEnumerable<SourceFile> Sources(string program){
        string[] names=["String.gf","ByteSearch.gf","IStringConvertable.gf","IHashable.gf","IEquatable.gf","IComparable.gf","Hashing.gf","CString.gf"];
        return names.Select(name=>new SourceFile(name,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core",name))))
            .Append(new SourceFile("string-any-search.gf",program));
    }
    private static void Run(string program,int optimization){
        var result=CompilerTestHelper.Run(Sources(program),optimization);
        Assert.True(result.ExitCode==0,$"String any search exit {result.ExitCode}: {result.StandardError}");
    }
}
