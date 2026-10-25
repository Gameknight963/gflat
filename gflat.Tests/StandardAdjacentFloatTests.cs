using System.Text;
using System.Text.RegularExpressions;

namespace gflat.Tests;

public sealed class StandardAdjacentFloatTests
{
    private static ulong Next(ref ulong state)
    {
        state ^= state << 13; state ^= state >> 7; state ^= state << 17;
        return state;
    }

    private static ulong Expected(ulong bits, bool wide, bool increment)
    {
        if (wide)
        {
            double value = BitConverter.UInt64BitsToDouble(bits);
            if (double.IsNaN(value)) return bits;
            return BitConverter.DoubleToUInt64Bits(increment ? Math.BitIncrement(value) : Math.BitDecrement(value));
        }
        float single = BitConverter.UInt32BitsToSingle((uint)bits);
        if (float.IsNaN(single)) return (uint)bits;
        return BitConverter.SingleToUInt32Bits(increment ? MathF.BitIncrement(single) : MathF.BitDecrement(single));
    }

    private static ulong Mix(ulong hash, ulong bits) => unchecked((hash ^ bits) * 1099511628211ul);

    private static ulong[] Edges(bool wide)
    {
        ulong sign = wide ? 1ul << 63 : 1ul << 31;
        ulong fraction = wide ? 0x000ffffffffffffful : 0x007ffffful;
        ulong infinity = wide ? 0x7ff0000000000000ul : 0x7f800000ul;
        ulong one = wide ? 0x3ff0000000000000ul : 0x3f800000ul;
        ulong[] positive = [0, 1, 2, fraction - 1, fraction, fraction + 1, fraction + 2,
            one - 1, one, one + 1, infinity - 2, infinity - 1, infinity,
            infinity | 1, infinity | (1ul << (wide ? 51 : 22)), infinity | fraction];
        return positive.SelectMany(bits => new[] { bits, bits | sign }).ToArray();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EdgesExponentTransitionsAndSeededBitPatternsMatchHostSteppingWithExactNaNs(int optimization)
    {
        var code = new StringBuilder("""
            using std; using std::Numerics;
            int attempts = 0;
            namespace Memory {
                public replace void*? Allocate(nuint count) { attempts++; return null; }
                public replace void Free(void* storage) nothrow {}
            }
            ulong Next(ref ulong state) nothrow {
                state = state ^ (state << 13); state = state ^ (state >> 7); state = state ^ (state << 17);
                return state;
            }
            ulong Mix(ulong hash, ulong bits) nothrow => (hash ^ bits) * 1099511628211ul;
            int main() {
            """);
        foreach (bool wide in new[] { false, true })
        {
            string scalar = wide ? "double" : "float", integer = wide ? "ulong" : "uint", suffix = wide ? "ul" : "u";
            ulong sign = wide ? 1ul << 63 : 1ul << 31;
            ulong infinity = wide ? 0x7ff0000000000000ul : 0x7f800000ul;
            int fractionBits = wide ? 52 : 23, exponentLimit = wide ? 2047 : 255;
            ulong[] edges = Edges(wide);
            ulong[] increment = edges.Select(bits => Expected(bits, wide, true)).ToArray();
            ulong[] decrement = edges.Select(bits => Expected(bits, wide, false)).ToArray();
            ulong hash = 14695981039346656037ul;
            // Host Math/MathF is the oracle. The gflat loop shares only input generation and hashing.
            for (int exponent = 1; exponent < exponentLimit; exponent++)
            foreach (int offset in new[] { -1, 0, 1 })
            foreach (bool negative in new[] { false, true })
            {
                ulong bits = (ulong)((long)((ulong)exponent << fractionBits) + offset);
                if (negative) bits |= sign;
                hash = Mix(Mix(hash, Expected(bits, wide, true)), Expected(bits, wide, false));
            }
            ulong state = 839217ul;
            for (int index = 0; index < 512; index++)
            {
                ulong bits = Next(ref state); if (!wide) bits = (uint)bits;
                hash = Mix(Mix(hash, Expected(bits, wide, true)), Expected(bits, wide, false));
            }
            code.AppendLine($$"""
                {
                    {{integer}}[{{edges.Length}}] edges = [{{string.Join(',', edges.Select(bits => bits + suffix))}}];
                    {{integer}}[{{edges.Length}}] increment = [{{string.Join(',', increment.Select(bits => bits + suffix))}}];
                    {{integer}}[{{edges.Length}}] decrement = [{{string.Join(',', decrement.Select(bits => bits + suffix))}}];
                    for (nuint index = 0; index < {{edges.Length}}; index++) {
                        {{integer}} bits = edges[index]; {{scalar}} value = *(({{scalar}}*)&bits);
                        {{scalar}} up = Math::BitIncrement(value); {{scalar}} down = Math::BitDecrement(value);
                        if (*(({{integer}}*)&up) != increment[index] || *(({{integer}}*)&down) != decrement[index]) return 1;
                    }
                    // Test each NaN payload bit in both signs explicitly, independent of the host's NaN ABI.
                    for (int payload = 0; payload < {{fractionBits}}; payload++) {
                        for (int negative = 0; negative < 2; negative++) {
                            {{integer}} bits = {{infinity}}{{suffix}} | (({{integer}})1 << payload);
                            if (negative != 0) bits = bits | {{sign}}{{suffix}};
                            {{scalar}} value = *(({{scalar}}*)&bits);
                            {{scalar}} up = Math::BitIncrement(value); {{scalar}} down = Math::BitDecrement(value);
                            if (*(({{integer}}*)&up) != bits || *(({{integer}}*)&down) != bits) return 3;
                        }
                    }
                    ulong hash = 14695981039346656037ul;
                    for (int exponent = 1; exponent < {{exponentLimit}}; exponent++) {
                        for (int offset = -1; offset <= 1; offset++) {
                            for (int negative = 0; negative < 2; negative++) {
                                {{integer}} bits = ({{integer}})((long)((ulong)exponent << {{fractionBits}}) + (long)offset);
                                if (negative != 0) bits = bits | {{sign}}{{suffix}};
                                {{scalar}} value = *(({{scalar}}*)&bits);
                                {{scalar}} up = Math::BitIncrement(value); {{scalar}} down = Math::BitDecrement(value);
                                hash = Mix(Mix(hash, (ulong)*(({{integer}}*)&up)), (ulong)*(({{integer}}*)&down));
                            }
                        }
                    }
                    ulong state = 839217ul;
                    for (int index = 0; index < 512; index++) {
                        {{integer}} bits = ({{integer}})Next(ref state); {{scalar}} value = *(({{scalar}}*)&bits);
                        {{scalar}} up = Math::BitIncrement(value); {{scalar}} down = Math::BitDecrement(value);
                        hash = Mix(Mix(hash, (ulong)*(({{integer}}*)&up)), (ulong)*(({{integer}}*)&down));
                    }
                    if (hash != {{hash}}ul || state != {{state}}ul) return 2;
                }
                """);
        }
        code.AppendLine("if (attempts != 0) return 4; return 0; }");
        var result = CompilerTestHelper.Run(Sources(code.ToString()), optimization);
        Assert.True(result.ExitCode == 0, $"Adjacent float exit {result.ExitCode}: {result.StandardError}");
    }

    [Fact]
    public void AdjacentMethodsAndNothrowWrappersUseIntegerOperationsWithoutExceptionBoundaries()
    {
        string ir = Compiler.Emit(Sources("""
            float UpFloat(float value) nothrow => std::Numerics::Math::BitIncrement(value);
            float DownFloat(float value) nothrow => std::Numerics::Math::BitDecrement(value);
            double UpDouble(double value) nothrow => std::Numerics::Math::BitIncrement(value);
            double DownDouble(double value) nothrow => std::Numerics::Math::BitDecrement(value);
            int main() { return 0; }
            """));
        var methods = Regex.Matches(ir, @"(?ms)^define [^\r\n]*\$Math\$Bit(Increment|Decrement)\$[^\r\n]*\{\r?\n.*?^\}");
        Assert.Equal(4, methods.Count);
        foreach (Match method in methods)
        {
            Assert.Contains("nounwind", method.Value.Split('\n')[0]);
            Assert.DoesNotContain("invoke", method.Value);
            Assert.DoesNotContain("__gflat_eh_", method.Value);
            Assert.DoesNotContain("fcmp", method.Value);
            Assert.DoesNotMatch(@"\b(fadd|fsub|fmul|fdiv|fptosi|fptoui)\b", method.Value);
        }
        var wrappers = Regex.Matches(ir, @"(?ms)^define [^\r\n]*\$(UpFloat|DownFloat|UpDouble|DownDouble)\([^\r\n]*\{\r?\n.*?^\}");
        Assert.Equal(4, wrappers.Count);
        foreach (Match wrapper in wrappers)
        {
            Assert.Contains("nounwind", wrapper.Value.Split('\n')[0]);
            Assert.DoesNotContain("invoke", wrapper.Value);
            Assert.DoesNotContain("__gflat_eh_", wrapper.Value);
        }
    }

    private static IEnumerable<SourceFile> Sources(string program) => [
        new SourceFile("Math.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core/Numerics/Math.gf"))),
        new SourceFile("main.gf", program)
    ];
}
