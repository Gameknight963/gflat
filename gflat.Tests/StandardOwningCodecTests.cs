using System.Text;

namespace gflat.Tests;

public sealed class StandardOwningCodecTests
{
    private const string Allocator = """
        using std;
        extern void*? malloc(nuint bytes);extern void free(void* storage);
        int allocations=0;int releases=0;int large=0;bool deny=false;
        namespace Memory{
            public replace void*? Allocate(nuint bytes){
                if(bytes>1024)large++;
                if(deny)return null;
                void*? result=malloc(bytes);if(result!=null)allocations++;return result;
            }
            public replace void Free(void* storage){releases++;free(storage);}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void UrlOwningResultsMatchConvertOracleAndRetainIndependentCountedBytes(int optimization)
    {
        string file=Path.Combine(Path.GetTempPath(),"gflat_owning_url_"+Guid.NewGuid().ToString("N")+".bin");
        int[] lengths=[..Enumerable.Range(0,21),255,256,257,1024];
        try{
            var random=new Random(756731);
            using(var writer=new BinaryWriter(File.Create(file)))foreach(int length in lengths){
                byte[] input=new byte[length];random.NextBytes(input);
                if(length>=256)for(int i=0;i<256;i++)input[i]=(byte)i;
                byte[] expected=Encoding.ASCII.GetBytes(Convert.ToBase64String(input).TrimEnd('=').Replace('+','-').Replace('/','_'));
                writer.Write((uint)length);writer.Write((uint)expected.Length);writer.Write(input);writer.Write(expected);
            }
            Run(Allocator+$$"""
                extern void*? fopen(readonly(char)* path,readonly(char)* mode);
                extern nuint fread(void* data,nuint size,nuint count,void* stream);
                extern int fclose(void* stream);extern int ferror(void* stream);
                int main(){
                    void*? file=fopen({{Literal(file)}},"rb");if(file==null)return 1;defer{fclose(file!);}
                    uint[2] header;char[1024] raw;char[1400] expected;int seen=0;
                    while(true){
                        nuint count=fread((void*)&header[0],1,8,file!);
                        if(count==0)break;if(count!=8)return 2;
                        nuint length=(nuint)header[0];nuint encodedLength=(nuint)header[1];
                        if(length>1024||encodedLength>1400)return 3;
                        if(fread((void*)&raw[0],1,length,file!)!=length)return 4;
                        if(fread((void*)&expected[0],1,encodedLength,file!)!=encodedLength)return 5;
                        {
                            int before=allocations;int added=0;if(length!=0)added=1;
                            String encoded=Base64UrlText::Encode(raw,length);
                            if(allocations!=before+added||encoded.Length!=encodedLength||encoded.Data[encodedLength]!='\0')return 6;
                            if(length!=0){char first=raw[0];raw[0]='!';
                                for(nuint i=0;i<encodedLength;i++)if(encoded[i]!=expected[i])return 7;raw[0]=first;}
                            before=allocations;String decoded=Base64UrlText::Decode(encoded.Data,encoded.Length);
                            if(allocations!=before+added||decoded.Length!=length||decoded.Data[length]!='\0')return 8;
                            encoded.Clear();
                            for(nuint i=0;i<length;i++)if(decoded[i]!=raw[i])return 9;
                        }
                        if(allocations!=releases)return 10;seen++;
                    }
                    if(ferror(file!)!=0||seen!={{lengths.Length}})return 11;return 0;
                }
                """,optimization);
        }finally{File.Delete(file);}
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void UrlStrictInvalidInputsOverflowAndEmptyInputPreserveOwners(int optimization)
    {
        string[] invalid=["A","Zg==","+w","/w","Zh","Zm9","Zg\n","Zm9vZg=","Zm9v!"];
        string cases=string.Join("\n",invalid.Select(value=>$$"""
            try{output=Base64UrlText::Decode({{Literal(value)}},{{value.Length}});return 1;}catch(ref Exception error){caught++;}
            """));
        Run(Allocator+$$"""
            int main(){
                {
                    String output=s"unchanged";readonly(char)* original=output.Data;nuint capacity=output.Capacity;int caught=0;
                    {{cases}}
                    try{output=Base64UrlText::Encode((readonly(char)*)1,(nuint)0-1);return 2;}catch(ref Exception error){caught++;}
                    if(caught!={{invalid.Length+1}}||output.Data!=original||output.Length!=9||output.Capacity!=capacity)return 3;
                    if(!output.StartsWith("unchanged"))return 4;
                    int before=allocations;deny=true;
                    String emptyEncoded=Base64UrlText::Encode((readonly(char)*)1,0);
                    String emptyDecoded=Base64UrlText::Decode((readonly(char)*)1,0);deny=false;
                    if(!emptyEncoded.IsEmpty||!emptyDecoded.IsEmpty||allocations!=before)return 5;
                    if(emptyEncoded.Data[0]!='\0'||emptyDecoded.Data[0]!='\0')return 6;
                }
                if(allocations!=releases)return 7;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryOwningCodecPreflightsBeforeAllocatingAndCleansUpAllocationFailures(int optimization)
        =>Run(Allocator+"""
            String Transform(int codec,bool decode,readonly(char)* data,nuint count){
                if(codec==0){if(decode)return Base64Text::Decode(data,count);return Base64Text::Encode(data,count);}
                if(codec==1){if(decode)return Base64UrlText::Decode(data,count);return Base64UrlText::Encode(data,count);}
                if(codec==2){if(decode)return HexText::Decode(data,count);return HexText::Encode(data,count);}
                if(decode)return PercentEncodingText::Decode(data,count);return PercentEncodingText::Encode(data,count);
            }
            int main(){
                char[3] source=['A','\0','B'];char[4100] invalid;
                {
                    String output=s"unchanged";readonly(char)* original=output.Data;nuint capacity=output.Capacity;
                    for(int codec=0;codec<4;codec++){
                        for(int mode=0;mode<2;mode++){
                            readonly(char)* input=source;nuint count=3;
                            if(mode==1){input="QQBC";count=4;if(codec==2){input="410042";count=6;}
                                if(codec==3){input="A%00B";count=5;}}
                            int balance=allocations-releases;deny=true;
                            try{output=Transform(codec,mode==1,input,count);return 1;}catch(ref BadAllocException error){}
                            deny=false;
                            if(allocations-releases!=balance||output.Data!=original||output.Length!=9||output.Capacity!=capacity)return 2;
                            if(output.Data[0]!='u'||output.Data[8]!='d'||output.Data[9]!='\0')return 3;
                            if(source[0]!='A'||source[1]!='\0'||source[2]!='B')return 4;
                            {
                                int before=allocations;String retry=Transform(codec,mode==1,input,count);
                                if(allocations!=before+1)return 5;
                                if(mode==1&&(retry.Length!=3||retry[0]!='A'||retry[1]!='\0'||retry[2]!='B'))return 6;
                            }
                            if(allocations-releases!=balance)return 7;
                        }
                        for(nuint i=0;i<4100;i++)invalid[i]='A';invalid[4099]='!';
                        if(codec==3)invalid[4099]='%';
                        int before=large;int balance=allocations-releases;
                        try{output=Transform(codec,true,invalid,4100);return 8;}catch(ref Exception error){}
                        if(large!=before||allocations-releases!=balance||output.Data!=original)return 9;
                        for(nuint i=0;i<4099;i++)if(invalid[i]!='A')return 10;
                        if(codec==3){if(invalid[4099]!='%')return 11;}else if(invalid[4099]!='!')return 12;
                    }
                }
                if(allocations!=releases)return 13;return 0;
            }
            """,optimization);

    private static string Literal(string value)=>"\""+value.Replace("\\","\\\\").Replace("\"","\\\"").Replace("\n","\\n")+"\"";
    private static void Run(string program,int optimization)
    {
        string[] names=["Base64.gf","Base64Text.gf","Base64Url.gf","Base64UrlText.gf","Hex.gf","HexText.gf",
            "PercentEncoding.gf","PercentEncodingText.gf","IStringConvertable.gf","IHashable.gf","IEquatable.gf",
            "IComparable.gf","Hashing.gf","String.gf","ByteSearch.gf","Formatting.gf"];
        var sources=names.Select(name=>new SourceFile(name,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core",name))))
            .Append(new SourceFile("owning-codecs.gf",program));
        var result=CompilerTestHelper.Run(sources,optimization);
        Assert.True(result.ExitCode==0,$"Exit {result.ExitCode}: {result.StandardError}");
    }
}

