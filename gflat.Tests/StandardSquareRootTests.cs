using System.Text;
using System.Text.RegularExpressions;

namespace gflat.Tests;

public sealed class StandardSquareRootTests
{
    private static ulong[] Inputs(bool wide)
    {
        var values = new HashSet<ulong>(wide
            ? [0, 0x8000000000000000, 1, 0x000fffffffffffff, 0x0010000000000000,
                0x3ff0000000000000, 0x4000000000000000, 0x4010000000000000, 0xbff0000000000000,
                0x7fefffffffffffff, 0xffefffffffffffff, 0x7ff0000000000000, 0xfff0000000000000,
                0x7ff8000000000000, 0xfff8000000000000, 0x7ff0000000000001, 0xfff0000000000001, ulong.MaxValue]
            : [0, 0x80000000, 1, 0x007fffff, 0x00800000, 0x3f800000, 0x40000000, 0x40800000,
                0xbf800000, 0x7f7fffff, 0xff7fffff, 0x7f800000, 0xff800000, 0x7fc00000, 0xffc00000,
                0x7f800001, 0xff800001, uint.MaxValue]);
        var random = new Random(812597);
        for (int i = 0; i < 256; i++)
        {
            byte[] bytes = new byte[8]; random.NextBytes(bytes);
            ulong bits = BitConverter.ToUInt64(bytes);
            values.Add(wide ? bits : (uint)bits);
        }
        return values.ToArray();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FloatingSquareRootMatchesDotNetAtBoundariesAndRandomBitPatterns(int optimization)
    {
        var code = new StringBuilder("using std; int main() {\n");
        foreach (bool wide in new[] { false, true })
        {
            ulong[] input = Inputs(wide);
            string type = wide ? "double" : "float", integer = wide ? "ulong" : "uint", suffix = wide ? "ul" : "u";
            ulong[] expected = input.Select(bits => wide
                ? BitConverter.DoubleToUInt64Bits(Math.Sqrt(BitConverter.UInt64BitsToDouble(bits)))
                : BitConverter.SingleToUInt32Bits(MathF.Sqrt(BitConverter.UInt32BitsToSingle((uint)bits)))).ToArray();
            string nanMask = wide ? "0x7ff0000000000000ul" : "0x7f800000u", fractionMask = wide ? "0x000ffffffffffffful" : "0x007fffffu";
            code.AppendLine($$"""
                {
                    {{integer}}[{{input.Length}}] input = [{{string.Join(",", input.Select(value => value + suffix))}}];
                    {{integer}}[{{expected.Length}}] expected = [{{string.Join(",", expected.Select(value => value + suffix))}}];
                    for (nuint i = 0; i < {{input.Length}}; i++) {
                        {{integer}} bits = input[i];
                        {{type}} original = *(({{type}}*)&bits);
                        {{type}} value = Math::Sqrt(original);
                        {{integer}} actual = *(({{integer}}*)&value);
                        bool nan = (expected[i] & {{nanMask}}) == {{nanMask}} && (expected[i] & {{fractionMask}}) != ({{integer}})0;
                        if (nan) {
                            if ((actual & {{nanMask}}) != {{nanMask}} || (actual & {{fractionMask}}) == ({{integer}})0) return 1;
                        }
                        else if (actual != expected[i]) return 2;
                    }
                }
                """);
        }
        code.AppendLine("return 0; }");
        var sources = new[] {
            new SourceFile("Math.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core/Math.gf"))),
            new SourceFile("main.gf", code.ToString())
        };
        string ir = Compiler.Emit(sources);
        Assert.Contains("@llvm.sqrt.f32(float", ir);
        Assert.Contains("@llvm.sqrt.f64(double", ir);
        Assert.Contains("= call float @llvm.sqrt.f32(float", ir);
        Assert.Contains("= call double @llvm.sqrt.f64(double", ir);
        Assert.DoesNotContain("call fast ", ir);
        Assert.DoesNotContain("call afn ", ir);
        var roots = Regex.Matches(ir, @"(?ms)^define [^\n]*@gflat\$[^\n(]*\$Math\$Sqrt[^\n]*\{\r?\n.*?^\}");
        Assert.Equal(2, roots.Count);
        foreach (Match root in roots)
        {
            Assert.Contains("nounwind", root.Value);
        }
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }
}
