using System.Text;
using System.Text.Json;

namespace gflat.Tests;

public sealed class StandardJsonStringTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EmittedLiteralsRoundTripThroughIndependentJsonParser(int optimization)
    {
        List<byte[]> cases=[[],Encoding.UTF8.GetBytes("plain / < > & ' text"),Enumerable.Range(0,128).Select(value=>(byte)value).ToArray(),
            Encoding.UTF8.GetBytes("\0\b\t\n\f\r\"\\"),Encoding.UTF8.GetBytes("\u007f\u0080\u07ff\u0800\ud7ff\ue000\ufeff\ufffe\uffff\u2028\u2029😀\U0010ffff")];
        var random=new Random(22591);
        for(int test=0;test<24;test++)
        {
            var text=new StringBuilder();
            for(int scalar=0;scalar<12;scalar++)
            {
                int value=random.Next(0x110000);
                if(value is >=0xd800 and <=0xdfff)value=0;
                text.Append(char.ConvertFromUtf32(value));
            }
            cases.Add(Encoding.UTF8.GetBytes(text.ToString()));
        }
        string[] quoted=QuoteCases(cases,optimization);
        Assert.Contains(" / < > & ' ",quoted[1]);
        Assert.Equal("\"\\u0000\\b\\t\\n\\f\\r\\\"\\\\\"",quoted[3]);
        Assert.Contains("😀",quoted[4]);
        Assert.Contains("\u2028",quoted[4]); // Kept literally, not unnecessarily escaped.
        Assert.Contains("\uffff",quoted[4]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AllSingleBytesAndEmptyInputHaveStrictValidityAndAtomicFailures(int optimization)
    {
        Run("""
            using std; using std::Json;
            int main() {
                char[1] input;char[10] output;
                for(int value=0;value<256;value++) {
                    input[0]=(char)value;
                    for(nuint i=0;i<10;i++)output[i]='Z';
                    nuint needed=99; nuint written=99;
                    bool valid=JsonString::TryGetQuotedLength(input,1,ref needed);
                    if(valid!=(value<128))return 1;
                    bool quoted=JsonString::TryQuote(input,1,&output[1],8,ref written);
                    if(quoted!=valid)return 2;
                    if(!valid) {
                        if(needed!=0||written!=0)return 3;
                        for(nuint i=0;i<10;i++)if(output[i]!='Z')return 4;
                    }
                    else {
                        nuint expected=3;
                        if(value<32)expected=8;
                        if(value==8||value==9||value==10||value==12||value==13||value==34||value==92)expected=4;
                        if(needed!=expected||written!=needed)return 5;
                        if(output[0]!='Z'||output[written+1]!='Z')return 6;
                        if(output[1]!='"'||output[written]!='"')return 7;
                    }
                }
                nuint needed=0; nuint written=0;
                if(!JsonString::TryGetQuotedLength(((nuint)1).reinterpret<readonly(char)*>(),0,ref needed)||needed!=2)return 8;
                if(!JsonString::TryQuote(((nuint)1).reinterpret<readonly(char)*>(),0,output,2,ref written)||written!=2)return 9;
                if(output[0]!='"'||output[1]!='"')return 10;
                return 0;
            }
            """,optimization);
        // Parse actual native output for every valid singleton, not just its size.
        QuoteCases(Enumerable.Range(0,128).Select(value=>new[]{(byte)value}).ToList(),optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryShortCapacityAndMalformedSuffixLeavesAllOutputUntouched(int optimization)
    {
        byte[] valid=Encoding.UTF8.GetBytes("a\0\n\"\\\uffff😀");
        byte[][] malformed=[[0x80],[0xc0,0x80],[0xc2],[0xe0,0x9f,0xbf],[0xe1,0x80],[0xed,0xa0,0x80],
            [0xf0,0x8f,0xbf,0xbf],[0xf1,0x80,0x80],[0xf4,0x90,0x80,0x80],[0xf5,0x80,0x80,0x80],[0xff],[0xe2,0x28,0xa1]];
        var code=new StringBuilder("using std; using std::Json;int main(){char[64] input;char[128] output;\n");
        Assign(code,"input",valid);
        code.AppendLine($$"""
            nuint needed=0;nuint written=0;
            if(!JsonString::TryGetQuotedLength(input,{{valid.Length}},ref needed)||needed!=22)return 1;
            for(nuint capacity=0;capacity<needed;capacity++) {
                for(nuint i=0;i<128;i++)output[i]='Z';
                written=99;
                if(JsonString::TryQuote(input,{{valid.Length}},&output[1],capacity,ref written)||written!=0)return 2;
                for(nuint i=0;i<128;i++)if(output[i]!='Z')return 3;
            }
            if(!JsonString::TryQuote(input,{{valid.Length}},&output[1],needed,ref written)||written!=needed)return 4;
            if(output[0]!='Z'||output[needed+1]!='Z')return 5;
            """);
        foreach(byte[] suffix in malformed)
        {
            byte[] input=valid.Concat(suffix).ToArray();Assign(code,"input",input);
            code.AppendLine($$"""
                for(nuint i=0;i<128;i++)output[i]='Z';
                needed=99;written=99;
                if(JsonString::TryGetQuotedLength(input,{{input.Length}},ref needed)||needed!=0)return 6;
                if(JsonString::TryQuote(input,{{input.Length}},&output[1],126,ref written)||written!=0)return 7;
                for(nuint i=0;i<128;i++)if(output[i]!='Z')return 8;
                """);
        }
        code.AppendLine("return 0;}");Run(code.ToString(),optimization);
    }

    private static string[] QuoteCases(List<byte[]> cases,int optimization)
    {
        int maximum=Math.Max(1,cases.Max(value=>value.Length));
        var code=new StringBuilder($"using std; using std::Json;extern int putchar(int value) nothrow;int main(){{char[{maximum}] input;char[{maximum*6+4}] output;nuint needed=0;nuint written=0;\n");
        foreach(byte[] input in cases)
        {
            Assign(code,"input",input);
            code.AppendLine($$"""
                if(!JsonString::TryGetQuotedLength(input,{{input.Length}},ref needed))return 1;
                for(nuint i=0;i<{{maximum*6+4}};i++)output[i]='Z';
                if(!JsonString::TryQuote(input,{{input.Length}},&output[1],needed,ref written)||written!=needed)return 2;
                if(output[0]!='Z'||output[written+1]!='Z')return 3;
                for(nuint i=0;i<written;i++) {
                    byte value=(byte)output[i+1];
                    putchar((int)"0123456789ABCDEF"[(nuint)(value>>4)]);
                    putchar((int)"0123456789ABCDEF"[(nuint)(value&(byte)15)]);
                }
                putchar(10);
                """);
        }
        code.AppendLine("return 0;}");
        string[] lines=Run(code.ToString(),optimization).Replace("\r\n","\n",StringComparison.Ordinal).Split('\n');
        Assert.Equal(cases.Count+1,lines.Length);Assert.Equal("",lines[^1]);
        string[] quoted=new string[cases.Count];
        for(int i=0;i<cases.Count;i++)
        {
            // Hex transport avoids the native process helper's host console-codepage
            // decoding. Parse the exact output bytes, including unchanged UTF-8.
            byte[] bytes=Convert.FromHexString(lines[i]);
            using var document=JsonDocument.Parse(bytes);
            Assert.Equal(Encoding.UTF8.GetString(cases[i]),document.RootElement.GetString());
            quoted[i]=Encoding.UTF8.GetString(bytes);
        }
        return quoted;
    }

    private static void Assign(StringBuilder code,string name,byte[] values)
    {
        for(int i=0;i<values.Length;i++)code.AppendLine($"{name}[{i}]=(char){values[i]};");
    }

    private static string Run(string code,int optimization)
    {
        SourceFile[] sources=new[]{"Text/utf8.gf","Json/JsonString.gf"}
            .Select(name=>new SourceFile(name,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core",name))))
            .Append(new("main.gf",code)).ToArray();
        string ir=Compiler.Emit(sources,options:new(){NoStdLib=true});
        foreach(string forbidden in new[]{"invoke ","personality","@__gflat_eh_","gflat$Memory$","@malloc","@free"})Assert.DoesNotContain(forbidden,ir);
        string directory=Path.Combine(Path.GetTempPath(),"gflat-json-string-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            string input=Path.Combine(directory,"input.ll"),output=Path.Combine(directory,"test.exe");File.WriteAllText(input,ir);
            var compiled=NativeToolchain.Compile(input,output,optimizationLevel:optimization);Assert.True(compiled.ExitCode==0,compiled.StandardError);
            var result=NativeToolchain.Run(output,[]);Assert.Equal(0,result.ExitCode);return result.StandardOutput;
        }
        finally {Directory.Delete(directory,true);}
    }
}
