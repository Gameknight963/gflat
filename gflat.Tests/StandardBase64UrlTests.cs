using System.Text;

namespace gflat.Tests;

public sealed class StandardBase64UrlTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ConvertOracleShortCapacitiesAndInPlaceDecode(int optimization)
    {
        WithDirectory(directory =>
        {
            string path = Path.Combine(directory, "oracle.bin");
            var random = new Random(463817);
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                foreach (int length in Enumerable.Range(0, 131).Concat([255, 256, 257, 511, 512, 513, 1024]))
                {
                    byte[] input = new byte[length];random.NextBytes(input);
                    if (length >= 256) for (int i = 0; i < 256; i++) input[i] = (byte)i;
                    byte[] encoded = Encoding.ASCII.GetBytes(Convert.ToBase64String(input).TrimEnd('=').Replace('+', '-').Replace('/', '_'));
                    writer.Write((uint)input.Length);writer.Write((uint)encoded.Length);writer.Write(input);writer.Write(encoded);
                }
            }
            Run(directory, $$"""
                using std;
                extern void*? fopen(readonly(char)* path,readonly(char)* mode);
                extern nuint fread(void* data,nuint size,nuint count,void* stream);
                extern int fclose(void* stream);extern int ferror(void* stream);
                int main(){
                    void*? file=fopen({{Literal(path)}},"rb");if(file==null)return 1;
                    uint[2] sizes;char[1024] input;char[1400] expected;char[1402] output;char[1402] inplace;
                    while(true){
                        nuint header=fread((void*)&sizes[0],1,8,file!);if(header==0)break;if(header!=8)return 2;
                        nuint count=(nuint)sizes[0];nuint length=(nuint)sizes[1];
                        if(fread((void*)&input[0],1,count,file!)!=count||fread((void*)&expected[0],1,length,file!)!=length)return 3;
                        nuint written=99;
                        if(!Base64Url::TryGetEncodedLength(count,ref written)||written!=length)return 4;
                        if(!Base64Url::TryGetDecodedLength(expected,length,ref written)||written!=count)return 5;
                        for(nuint cap=0;cap<length;cap++){
                            for(int i=0;i<1402;i++)output[i]='?';written=99;
                            if(Base64Url::TryEncode(input,count,&output[1],cap,ref written)||written!=0)return 6;
                            for(int i=0;i<1402;i++)if(output[i]!='?')return 7;
                        }
                        for(int i=0;i<1402;i++)output[i]='?';
                        if(!Base64Url::TryEncode(input,count,&output[1],length,ref written)||written!=length)return 8;
                        for(nuint i=0;i<length;i++)if(output[i+1]!=expected[i])return 9;
                        if(output[0]!='?'||output[length+1]!='?')return 10;
                        for(nuint cap=0;cap<count;cap++){
                            for(int i=0;i<1402;i++){output[i]='?';inplace[i]='?';}
                            for(nuint i=0;i<length;i++)inplace[i+1]=expected[i];written=99;
                            if(Base64Url::TryDecode(expected,length,&output[1],cap,ref written)||written!=0)return 11;
                            for(int i=0;i<1402;i++)if(output[i]!='?')return 12;
                            if(Base64Url::TryDecode(&inplace[1],length,&inplace[1],cap,ref written)||written!=0)return 13;
                            for(nuint i=0;i<length;i++)if(inplace[i+1]!=expected[i])return 14;
                            if(inplace[0]!='?'||inplace[length+1]!='?')return 15;
                        }
                        for(int i=0;i<1402;i++){output[i]='?';inplace[i]='?';}
                        for(nuint i=0;i<length;i++)inplace[i+1]=expected[i];
                        if(!Base64Url::TryDecode(expected,length,&output[1],count,ref written)||written!=count)return 16;
                        if(!Base64Url::TryDecode(&inplace[1],length,&inplace[1],count,ref written)||written!=count)return 17;
                        for(nuint i=0;i<count;i++)if(output[i+1]!=input[i]||inplace[i+1]!=input[i])return 18;
                        for(nuint i=count;i<length;i++)if(inplace[i+1]!=expected[i])return 19;
                        if(output[0]!='?'||output[count+1]!='?'||inplace[0]!='?'||inplace[length+1]!='?')return 20;
                    }
                    if(ferror(file!)!=0)return 21;return fclose(file!);
                }
                """, optimization);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryBytePositionAndTailBitsAreStrictAndFailureIsAtomic(int optimization)
        => WithDirectory(directory => Run(directory, """
            using std;
            int Digit(int value) nothrow{
                if(value>=65&&value<=90)return value-65;
                if(value>=97&&value<=122)return value-97+26;
                if(value>=48&&value<=57)return value-48+52;
                if(value==45)return 62;if(value==95)return 63;return -1;
            }
            int main(){
                char[13] input;char[13] inplace;char[16] output;
                for(nuint length=2;length<=11;length++){
                    for(nuint position=0;position<length;position++){
                        for(int value=0;value<256;value++){
                            for(int i=0;i<13;i++){input[i]='A';inplace[i]='A';}
                            input[position]=(char)value;inplace[position]=(char)value;
                            int digit=Digit(value);bool valid=length%4!=1&&digit>=0;
                            if(position==length-1){
                                if(length%4==2&&(digit&15)!=0)valid=false;
                                if(length%4==3&&(digit&3)!=0)valid=false;
                            }
                            nuint expected=length/4*3;if(length%4!=0)expected+=length%4-1;
                            nuint written=99;bool actual=Base64Url::TryGetDecodedLength(input,length,ref written);
                            if(actual!=valid)return 1;
                            if(valid){if(written!=expected)return 2;}
                            else{
                                if(written!=0)return 3;
                                for(int i=0;i<16;i++)output[i]='?';
                                if(Base64Url::TryDecode(input,length,&output[1],14,ref written)||written!=0)return 4;
                                for(int i=0;i<16;i++)if(output[i]!='?')return 5;
                                if(Base64Url::TryDecode(inplace,length,inplace,13,ref written)||written!=0)return 6;
                                for(int i=0;i<13;i++)if(inplace[i]!=input[i])return 7;
                            }
                        }
                    }
                }
                return 0;
            }
            """, optimization));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ZeroInputsOverflowPreflightAndOrdinaryCallsNeedNoAllocation(int optimization)
        => WithDirectory(directory => Run(directory, """
            using std;
            int allocations=0;
            namespace Memory{
                public replace void*? Allocate(nuint size){allocations++;return null;}
                public replace void Free(void* data){}
            }
            int main(){
                readonly(char)* unreadable=(readonly(char)*)1;char* unwritable=(char*)1;
                nuint length=99;
                if(!Base64Url::TryEncode(unreadable,0,unwritable,0,ref length)||length!=0)return 1;
                if(!Base64Url::TryDecode(unreadable,0,unwritable,0,ref length)||length!=0)return 2;
                if(!Base64Url::TryGetDecodedLength(unreadable,0,ref length)||length!=0)return 3;
                if(Base64Url::TryGetDecodedLength(unreadable,1,ref length)||length!=0)return 4;
                if(Base64Url::TryDecode(unreadable,5,unwritable,0,ref length)||length!=0)return 5;
                nuint maximum=(nuint)0-1;
                if(Base64Url::TryGetEncodedLength(maximum,ref length)||length!=0)return 6;
                if(Base64Url::TryEncode(unreadable,maximum,unwritable,maximum,ref length)||length!=0)return 7;
                if(Base64Url::TryEncode(unreadable,3,unwritable,3,ref length)||length!=0)return 8;
                nuint largest=maximum/4*3+2;
                if(!Base64Url::TryGetEncodedLength(largest,ref length)||length!=maximum)return 9;
                if(Base64Url::TryGetEncodedLength(largest+1,ref length)||length!=0)return 10;
                char[4] output;char[3] decoded;char[3] input;
                input[0]=(char)251;input[1]=(char)255;input[2]=(char)255;
                if(!Base64Url::TryEncode(input,3,output,4,ref length)||length!=4)return 11;
                if(output[0]!='-'||output[1]!='_'||output[2]!='_'||output[3]!='_')return 12;
                if(!Base64Url::TryDecode(output,4,decoded,3,ref length)||length!=3)return 13;
                if((byte)decoded[0]!=(byte)251||(byte)decoded[1]!=(byte)255||(byte)decoded[2]!=(byte)255)return 14;
                return allocations;
            }
            """, optimization));

    private static string Literal(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    private static void WithDirectory(Action<string> action)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat_base64url_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { action(directory); } finally { Directory.Delete(directory, true); }
    }
    private static void Run(string directory, string program, int optimization)
    {
        var sources = new[] { new SourceFile("Base64Url.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core/Base64Url.gf"))), new SourceFile("main.gf", program) };
        string ir = Path.Combine(directory, "codec.ll"), executable = Path.Combine(directory, "codec.exe");
        File.WriteAllText(ir, Compiler.Emit(sources, options: new() { InternalizeFunctions = true }));
        var compilation = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
        Assert.True(compilation.ExitCode == 0, compilation.StandardError);
        var result = NativeToolchain.Run(executable, []);
        Assert.True(result.ExitCode == 0, $"Native Base64Url exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
