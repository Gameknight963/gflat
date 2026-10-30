using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace gflat.Tests.Std.Codecs;

public sealed class Base32Tests
{
    private const string Alphabet="ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    // Independent MSB-first bit oracle: no product-style five-byte grouping.
    private static string Encode(byte[] bytes)
    {
        var text=new StringBuilder();
        for(int bit=0;bit<bytes.Length*8;bit+=5)
        {
            int digit=0;
            for(int offset=0;offset<5;offset++)
            {
                int index=bit+offset;digit<<=1;
                if(index<bytes.Length*8)digit|=(bytes[index/8]>>(7-index%8))&1;
            }
            text.Append(Alphabet[digit]);
        }
        while(text.Length%8!=0)text.Append('=');
        return text.ToString();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task IndependentBitOracleRfcVectorsAndEveryCapacityPreserveExactBoundaries(int optimization)
    {
        string[] plain=["","f","fo","foo","foob","fooba","foobar"];
        string[] rfc=["","MY======","MZXQ====","MZXW6===","MZXW6YQ=","MZXW6YTB","MZXW6YTBOI======"];
        var cases=plain.Select(Encoding.ASCII.GetBytes).ToList();
        for(int i=0;i<plain.Length;i++)Assert.Equal(rfc[i],Encode(cases[i]));
        cases.Add(Enumerable.Range(0,256).Select(value=>(byte)value).ToArray());
        var random=new Random(924701);
        foreach(int length in Enumerable.Range(0,26).Concat(new[]{31,32,33,63,64,65,127,128,129,255,256,257}))
        {
            byte[] bytes=new byte[length];random.NextBytes(bytes);cases.Add(bytes);
        }
        string data=string.Concat(cases.Select(Convert.ToHexString));
        string encoded=string.Concat(cases.Select(Encode));
        string lengths=string.Join(',',cases.Select(value=>value.Length.ToString(CultureInfo.InvariantCulture)));
        await Run($$"""
            using std; using std::Encoding;
            int Digit(char value) nothrow{if(value<='9')return (int)value-48;return (int)value-65+10;}
            int main(){
                int[{{cases.Count}}] lengths=[{{lengths}}];readonly(char)* data="{{data}}";
                readonly(char)* oracle="{{encoded}}";nuint sourceOffset=0;nuint encodedOffset=0;
                char[257] input;char[1026] output;
                for(nuint row=0;row<{{cases.Count}};row++){
                    nuint length=(nuint)lengths[row];nuint expected=((length+4)/5)*8;
                    for(nuint i=0;i<length;i++)input[i]=(char)((Digit(data[sourceOffset+i*2])<<4)|Digit(data[sourceOffset+i*2+1]));
                    nuint measured=99;if(!Base32::TryGetEncodedLength(length,ref measured)||measured!=expected)return 1;
                    if(!Base32::TryGetDecodedLength(oracle+encodedOffset,expected,ref measured)||measured!=length)return 2;
                    for(nuint capacity=0;capacity<=expected+1;capacity++){
                        for(nuint i=0;i<1026;i++)output[i]='?';nuint written=99;
                        bool success=Base32::TryEncode(input,length,&output[1],capacity,ref written);
                        if(success!=(capacity>=expected))return 3;
                        if(!success){if(written!=0)return 4;for(nuint i=0;i<1026;i++)if(output[i]!='?')return 5;}
                        else{if(written!=expected||output[0]!='?')return 6;
                            for(nuint i=0;i<expected;i++)if(output[i+1]!=oracle[encodedOffset+i])return 7;
                            for(nuint i=expected+1;i<1026;i++)if(output[i]!='?')return 8;}
                    }
                    for(nuint capacity=0;capacity<=length+1;capacity++){
                        for(nuint i=0;i<1026;i++)output[i]='?';nuint written=99;
                        bool success=Base32::TryDecode(oracle+encodedOffset,expected,&output[1],capacity,ref written);
                        if(success!=(capacity>=length))return 9;
                        if(!success){if(written!=0)return 10;for(nuint i=0;i<1026;i++)if(output[i]!='?')return 11;}
                        else{if(written!=length||output[0]!='?')return 12;
                            for(nuint i=0;i<length;i++)if(output[i+1]!=input[i])return 13;
                            for(nuint i=length+1;i<1026;i++)if(output[i]!='?')return 14;}
                    }
                    for(nuint i=0;i<1026;i++)output[i]='?';
                    for(nuint i=0;i<expected;i++)output[i+1]=oracle[encodedOffset+i];
                    nuint inPlaceWritten=99;
                    if(length!=0){
                        if(Base32::TryDecode(&output[1],expected,&output[1],length-1,ref inPlaceWritten)||inPlaceWritten!=0)return 22;
                        if(output[0]!='?')return 23;
                        for(nuint i=0;i<expected;i++)if(output[i+1]!=oracle[encodedOffset+i])return 24;
                        for(nuint i=expected+1;i<1026;i++)if(output[i]!='?')return 25;
                    }
                    if(!Base32::TryDecode(&output[1],expected,&output[1],length,ref inPlaceWritten)||inPlaceWritten!=length)return 17;
                    if(output[0]!='?')return 18;
                    for(nuint i=0;i<length;i++)if(output[i+1]!=input[i])return 19;
                    for(nuint i=length;i<expected;i++)if(output[i+1]!=oracle[encodedOffset+i])return 20;
                    for(nuint i=expected+1;i<1026;i++)if(output[i]!='?')return 21;
                    for(nuint i=0;i<length;i++)if((byte)input[i]!=(byte)((Digit(data[sourceOffset+i*2])<<4)|Digit(data[sourceOffset+i*2+1])))return 15;
                    sourceOffset+=length*2;encodedOffset+=expected;
                }
                if(sourceOffset!={{data.Length}}||encodedOffset!={{encoded.Length}})return 16;
                return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task EveryNonzeroTailBitAndMalformedSuffixFailsBeforeAnyOutput(int optimization)
    {
        var invalid=new HashSet<string>(StringComparer.Ordinal){"=","========","A=======","AAA=====","AAAAAA==",
            "MY","MY=====","MY=======","MY======\n","MY====== "," MY======","MY======MY======",
            "MZXW6YTBMY=====A","MZXW6YTBMY======A","MZXW6YTBMY======="};
        // Enumerate all 32 possible final digits for every legal padded tail length.
        // Invalidity is decided independently from its number of unused low bits.
        int[] unused=[2,4,1,3];
        for(int length=1;length<=4;length++)
        {
            string canonical=Encode(Enumerable.Range(0,length).Select(index=>(byte)(index*71+19)).ToArray());
            int last=canonical.IndexOf('=')-1;
            for(int digit=0;digit<32;digit++)if((digit&((1<<unused[length-1])-1))!=0)
            {
                char[] changed=canonical.ToCharArray();changed[last]=Alphabet[digit];
                invalid.Add(new string(changed));invalid.Add("MZXW6YTB"+new string(changed));
            }
        }
        const string block="MZXW6YTB";
        for(int index=0;index<8;index++)
        {
            foreach(char bad in new[]{'0','1','8','9',' ','\t','\r','\n','\0',(char)0x80,(char)0xff,'='})
            {char[] changed=block.ToCharArray();changed[index]=bad;invalid.Add(new string(changed));}
            char[] lower=block.ToCharArray();lower[index]=char.ToLowerInvariant(lower[index]);
            if(lower[index]!=block[index])invalid.Add(new string(lower));
        }
        string[] values=invalid.Order(StringComparer.Ordinal).ToArray();
        string blob=string.Concat(values.Select(value=>Convert.ToHexString(value.Select(character=>(byte)character).ToArray())));
        string lengths=string.Join(',',values.Select(value=>value.Length.ToString(CultureInfo.InvariantCulture)));
        await Run($$"""
            using std; using std::Encoding;
            int Digit(char value) nothrow{if(value<='9')return (int)value-48;return (int)value-65+10;}
            int main(){
                readonly(char)* blob="{{blob}}";int[{{values.Length}}] lengths=[{{lengths}}];nuint offset=0;
                char[32] text;char[34] output;
                for(nuint row=0;row<{{values.Length}};row++){
                    nuint length=(nuint)lengths[row];
                    for(nuint i=0;i<length;i++)text[i]=(char)((Digit(blob[offset+i*2])<<4)|Digit(blob[offset+i*2+1]));
                    for(nuint i=0;i<34;i++)output[i]='?';nuint written=99;
                    if(Base32::TryGetDecodedLength(text,length,ref written)||written!=0)return 1;
                    written=99;if(Base32::TryDecode(text,length,&output[1],32,ref written)||written!=0)return 2;
                    for(nuint i=0;i<34;i++)if(output[i]!='?')return 3;
                    for(nuint i=0;i<length;i++)output[i+1]=text[i];written=99;
                    if(Base32::TryDecode(&output[1],length,&output[1],32,ref written)||written!=0)return 5;
                    if(output[0]!='?')return 6;
                    for(nuint i=0;i<length;i++)if(output[i+1]!=text[i])return 7;
                    for(nuint i=length+1;i<34;i++)if(output[i]!='?')return 8;
                    offset+=length*2;
                }
                if(offset!={{blob.Length}})return 4;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task OverflowAndCountPreflightsDoNotReadInputAndPureCodecNeedsNoRuntime(int optimization)
        =>await Run("""
            using std; using std::Encoding;
            int main(){
                nuint maximum=(nuint)0-1;nuint boundary=(maximum/8)*5;nuint written=99;
                if(!Base32::TryGetEncodedLength(boundary,ref written)||written!=maximum-7)return 1;
                if(Base32::TryGetEncodedLength(boundary+1,ref written)||written!=0)return 2;
                if(Base32::TryGetEncodedLength(maximum,ref written)||written!=0)return 3;
                char[4] output;for(nuint i=0;i<4;i++)output[i]='?';
                if(Base32::TryEncode(((nuint)1).reinterpret<char*>(),maximum,&output[1],maximum,ref written)||written!=0)return 4;
                if(Base32::TryEncode(((nuint)1).reinterpret<char*>(),boundary,&output[1],0,ref written)||written!=0)return 5;
                for(nuint count=1;count<8;count++){
                    if(Base32::TryGetDecodedLength(((nuint)1).reinterpret<char*>(),count,ref written)||written!=0)return 6;
                    if(Base32::TryDecode(((nuint)1).reinterpret<char*>(),count,&output[1],2,ref written)||written!=0)return 7;
                }
                if(!Base32::TryEncode(((nuint)1).reinterpret<char*>(),0,&output[1],0,ref written)||written!=0)return 8;
                if(!Base32::TryDecode(((nuint)1).reinterpret<char*>(),0,&output[1],0,ref written)||written!=0)return 9;
                if(Base32::TryDecode("MZXW6YTB",8,&output[1],2,ref written)||written!=0)return 10;
                for(nuint i=0;i<4;i++)if(output[i]!='?')return 11;
                return 0;
            }
            """,optimization);

    private static async Task Run(string program,int optimization)
    {
        SourceFile[] files=[new("Base32.gf",File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core/Encoding/Base32.gf"))),new("main.gf",program)];
        string ir=Compiler.Emit(files,options:new(){NoStdLib=true,NoBuiltins=true,InternalizeFunctions=true});
        Assert.DoesNotContain("@__gflat_eh_",ir);Assert.DoesNotContain("@malloc",ir);Assert.DoesNotContain("@__gflat_gc_",ir);
        foreach(string name in new[]{"TryGetEncodedLength","TryEncode","TryGetDecodedLength","TryDecode"})
        {
            Match definition=Regex.Match(ir,@"(?m)^define [^\r\n]*@[^\r\n]*"+name+@"[^\r\n]*\{");
            Assert.True(definition.Success,$"Missing Base32 {name} definition");Assert.Contains("nounwind",definition.Value);
        }
        string directory=Path.Combine(Path.GetTempPath(),"gflat-base32-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            string input=Path.Combine(directory,"main.ll"),executable=Path.Combine(directory,"main.exe");File.WriteAllText(input,ir);
            var compiled=NativeToolchain.Compile(input,executable,optimizationLevel:optimization);Assert.True(compiled.ExitCode==0,compiled.StandardError);
            var result=NativeToolchain.Run(executable,[]);Assert.True(result.ExitCode==0,$"Base32 exit {result.ExitCode}: {result.StandardError}");
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory);}
    }
}
