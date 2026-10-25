using System.Text.RegularExpressions;

namespace gflat.Tests;

public sealed class StandardMathRoundingTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 2)]
    public void IntegralRoundingMatchesDotNetAndPreservesNonfiniteBits(bool wide, int optimization)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat_rounding_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "oracle.bin");
            using (var writer = new BinaryWriter(File.Create(path)))
                foreach (ulong bits in Inputs(wide))
                {
                    if (wide)
                    {
                        double value = BitConverter.UInt64BitsToDouble(bits);
                        writer.Write(bits);
                        foreach (double result in new[] { Math.Truncate(value), Math.Floor(value), Math.Ceiling(value), Math.Round(value) })
                            writer.Write(double.IsNaN(value) ? bits : BitConverter.DoubleToUInt64Bits(result));
                    }
                    else
                    {
                        float value = BitConverter.UInt32BitsToSingle((uint)bits);
                        writer.Write((uint)bits);
                        foreach (float result in new[] { MathF.Truncate(value), MathF.Floor(value), MathF.Ceiling(value), MathF.Round(value) })
                            writer.Write(float.IsNaN(value) ? (uint)bits : BitConverter.SingleToUInt32Bits(result));
                    }
                }
            string scalar = wide ? "double" : "float", integer = wide ? "ulong" : "uint";
            int recordSize = wide ? 40 : 20;
            string program = $$"""
                using std; using std::Numerics;
                extern void*? fopen(readonly(char)* path,readonly(char)* mode);
                extern nuint fread(void* data,nuint size,nuint count,void* stream);
                extern int fclose(void* stream);extern int ferror(void* stream);
                int main(){
                    void*? file=fopen({{Literal(path)}},"rb");if(file==null)return 1;
                    {{integer}}[5] row;{{scalar}}[4] actual;
                    while(true){
                        nuint read=fread((void*)&row[0],1,{{recordSize}},file!);if(read==0)break;if(read!={{recordSize}})return 2;
                        {{scalar}} value=*(({{scalar}}*)&row[0]);
                        actual[0]=Math::Truncate(value);actual[1]=Math::Floor(value);
                        actual[2]=Math::Ceiling(value);actual[3]=Math::Round(value);
                        for(nuint i=0;i<4;i++)if(*(({{integer}}*)&actual[i])!=row[i+1])return (int)i+3;
                    }
                    if(ferror(file!)!=0)return 7;return fclose(file!);
                }
                """;
            var sources = new[]
            {
                new SourceFile("Math.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core/Numerics/Math.gf"))),
                new SourceFile("main.gf", program)
            };
            string ir = Compiler.Emit(sources, options: new() { InternalizeFunctions = true });
            var functions = Regex.Matches(ir, @"(?ms)^define [^\r\n]*\$Math\$(Truncate|Floor|Ceiling|Round)\$[^\r\n]*\{\r?\n.*?^\}");
            Assert.Equal(8, functions.Count);
            foreach (string method in new[] { "Truncate", "Floor", "Ceiling", "Round" })
                Assert.Matches(@"call " + scalar + @" @[^\r\n ]*\$Math\$" + method + @"\$", ir);
            foreach (Match function in functions)
            {
                Assert.Contains("nounwind", function.Value.Split('\n')[0]);
                Assert.DoesNotContain("invoke", function.Value);
                Assert.DoesNotContain("__gflat_eh_", function.Value);
                Assert.DoesNotContain(" fast ", function.Value);
                Assert.DoesNotMatch(@"@(llvm\.(trunc|floor|ceil|round|nearbyint|rint)|trunc|truncf|floor|floorf|ceil|ceilf|round|roundf|nearbyint|rint)", function.Value);
                Assert.DoesNotContain("fptosi", function.Value);
                Assert.DoesNotContain("fptoui", function.Value);
            }
            Assert.False(NativeToolchain.RequiresWideIntegerRuntime(ir));
            string source = Path.Combine(directory, "rounding.ll"), executable = Path.Combine(directory, "rounding.exe");
            File.WriteAllText(source, ir);
            var compiled = NativeToolchain.Compile(source, executable, optimizationLevel: optimization);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            var nativeResult = NativeToolchain.Run(executable, []);
            Assert.True(nativeResult.ExitCode == 0, $"Native rounding exit {nativeResult.ExitCode}: {nativeResult.StandardOutput} {nativeResult.StandardError}");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static IEnumerable<ulong> Inputs(bool wide)
    {
        int fractionBits = wide ? 52 : 23;
        int exponentLimit = wide ? 2048 : 256;
        ulong sign = 1ul << (wide ? 63 : 31), fractionMask = (1ul << fractionBits) - 1;
        ulong[] fractions = [0, 1, (1ul << (fractionBits - 1)) - 1, 1ul << (fractionBits - 1), fractionMask - 1, fractionMask];
        var inputs = new HashSet<ulong>();
        for (int exponent = 0; exponent < exponentLimit; exponent++)
        foreach (ulong fraction in fractions)
        {
            ulong value = ((ulong)exponent << fractionBits) | fraction;
            inputs.Add(value);inputs.Add(value | sign);
        }
        // Adjacent representable values on either side of both odd/even half-integer ties.
        for (int integer = -32; integer <= 32; integer++)
        {
            double half = integer + 0.5;
            ulong bits = wide ? BitConverter.DoubleToUInt64Bits(half) : BitConverter.SingleToUInt32Bits((float)half);
            inputs.Add(bits - 1);inputs.Add(bits);inputs.Add(bits + 1);
        }
        // The last fractional binades and the first entirely integral binades.
        for (int exponent = fractionBits - 3; exponent <= fractionBits + 2; exponent++)
        foreach (int offset in new[] { -3, -2, -1, 0, 1, 2, 3 })
        {
            double value = Math.ScaleB(1.0, exponent);
            ulong bits = wide ? BitConverter.DoubleToUInt64Bits(value) : BitConverter.SingleToUInt32Bits((float)value);
            ulong adjacent = unchecked((ulong)((long)bits + offset));
            inputs.Add(adjacent);inputs.Add(adjacent | sign);
        }
        var random = new Random(2048752);
        for (int i = 0; i < 4096; i++)
        {
            byte[] bytes = new byte[8];random.NextBytes(bytes);
            ulong bits = BitConverter.ToUInt64(bytes);
            inputs.Add(wide ? bits : (uint)bits);
        }
        return inputs;
    }

    private static string Literal(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
