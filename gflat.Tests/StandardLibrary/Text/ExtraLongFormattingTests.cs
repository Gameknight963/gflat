using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace gflat.Tests.Std.Text;

public sealed class ExtraLongFormattingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DecimalMatchesInt128AcrossPowersRandomAndAtomicCapacityFailures(int optimization)
    {
        List<Int128> values = [Int128.MinValue, Int128.MaxValue, 0, 1, -1];
        for (Int128 power = 10; ; power *= 10)
        {
            foreach (int delta in new[] { -1, 0, 1 }) { values.Add(power + delta); values.Add(-power + delta); }
            if (power > Int128.MaxValue / 10) break;
        }
        var random = new Random(177821);
        for (int i = 0; i < 64; i++) { byte[] bytes = new byte[16]; random.NextBytes(bytes); values.Add((Int128)new BigInteger(bytes)); }
        values = values.Distinct().ToList();
        string Bits(Int128 value)
        {
            UInt128 bits = unchecked((UInt128)value);
            return $"(((extralong){(ulong)(bits >> 64)}ul << 64) | (extralong){(ulong)bits}ul)";
        }
        string code = $$"""
            int main() nothrow {
                extralong[{{values.Count}}] values = [{{string.Join(",", values.Select(Bits))}}];
                readonly(char)*[{{values.Count}}] expected = [{{string.Join(",", values.Select(v => "\"" + v.ToString(CultureInfo.InvariantCulture) + "\""))}}];
                char[43] output; nuint written = 99;
                for (nuint test = 0; test < {{values.Count}}; test++) {
                    nuint length = 0; while(expected[test][length] != '\0') length++;
                    for(nuint capacity = 0; capacity <= 41; capacity++) {
                        for(nuint i=0;i<43;i++) output[i]='Z'; written=99;
                        bool success=std::Text::IntegerFormatting::TryFormat(values[test],&output[1],capacity,ref written);
                        if(success != (capacity>=length))return 1;
                        if(!success){if(written!=0)return 2;for(nuint i=0;i<43;i++)if(output[i]!='Z')return 3;}
                        else{if(written!=length||output[0]!='Z')return 4;
                         for(nuint i=0;i<written;i++)if(output[i+1]!=expected[test][i])return 5;
                         for(nuint i=written+1;i<43;i++)if(output[i]!='Z')return 6;}
                    }
                }
                if(std::Text::IntegerFormatting::TryFormat((extralong)0,((nuint)1).reinterpret<char*>(),0,ref written)||written!=0)return 7;
                return 0;
            }
            """;
        SourceFile[] sources = [new("IntegerFormatting.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core/Text/IntegerFormatting.gf"))), new("main.gf",code)];
        string ir = Compiler.Emit(sources, options:new(){NoStdLib=true,NoBuiltins=true,InternalizeFunctions=true});
        Assert.DoesNotMatch(@"\b(?:sdiv|udiv|srem|urem)\s+i128\b",ir);
        foreach(string forbidden in new[]{"__divti3","__udivti3","llvm.memcpy","@malloc","@free","__gflat_eh","gflat$Memory$"}) Assert.DoesNotContain(forbidden,ir);
        Match method=Regex.Match(ir,@"(?ms)^define [^\r\n]*@[^\r\n]*TryFormat[^\r\n]*i128[^\r\n]*\{\r?\n(?<body>.*?)^\}");
        Assert.True(method.Success); Assert.Contains("nounwind",method.Value);
        Assert.Equal(0,CompilerTestHelper.Run(sources,optimization).ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void WriterReceivesOneCountedWriteAndPreservesItsException(int optimization)
    {
        string code = """
            using std; using std::Text;
            struct Writer : ITextWriter {
                public int Calls; public bool Fail;
                public void Write(readonly(char)* text,nuint count) {
                    Calls++;
                    if(count!=40)throw new Exception("length");
                    readonly(char)* expected="-170141183460469231731687303715884105728";
                    for(nuint i=0;i<count;i++)if(text[i]!=expected[i])throw new Exception("digits");
                    if(Fail)throw new Exception("destination",73);
                }
            }
            int main(){
                Writer writer = new Writer(); writer.Calls=0; writer.Fail=false;
                extralong minimum=(extralong)1<<127;
                minimum.Format(&writer);
                if(writer.Calls!=1)return 1;
                writer.Fail=true;
                try{minimum.Format(&writer);return 2;}
                catch(Exception* error){if(error.GetCode()!=73)return 3;}
                if(writer.Calls!=2)return 4;
                return 0;
            }
            """;
        var sources = new[] { "IntegerFormatting.gf", "ExtraLongFormatting.gf" }.Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core",name == "IntegerFormatting.gf" ? "Text/IntegerFormatting.gf" : name == "ExtraLongFormatting.gf" ? "extra_long_format.gf" : name)))).Append(new SourceFile("main.gf",code));
        Assert.Equal(0,CompilerTestHelper.Run(sources,optimization).ExitCode);
    }
}
