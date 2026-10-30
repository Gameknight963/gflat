using System.Text;

namespace gflat.Tests.Std.Numerics;

public sealed class CopySignTests
{
    private static ulong[] Boundaries(bool wide)
    {
        ulong exponent = wide ? 0x7ff0000000000000ul : 0x7f800000ul;
        ulong fraction = wide ? 0x000ffffffffffffful : 0x007ffffful;
        ulong one = wide ? 0x3ff0000000000000ul : 0x3f800000ul;
        var boundaries = new HashSet<ulong> { 0, 1, fraction, fraction + 1, one, exponent - 1, exponent, exponent | fraction };
        // Each payload bit, including the quiet/signaling bit, in both signs.
        for (int bit = 0; bit < (wide ? 52 : 23); bit++) boundaries.Add(exponent | (1ul << bit));
        return boundaries.ToArray();
    }

    private static ulong[] Signs(bool wide)
    {
        ulong sign = wide ? 1ul << 63 : 1ul << 31;
        ulong exponent = wide ? 0x7ff0000000000000ul : 0x7f800000ul;
        ulong fraction = wide ? 0x000ffffffffffffful : 0x007ffffful;
        ulong one = wide ? 0x3ff0000000000000ul : 0x3f800000ul;
        return [0, sign, one, one | sign, exponent, exponent | sign,
            exponent | 1, exponent | sign | 1, exponent | fraction, exponent | fraction | sign];
    }

    private static ulong Next(ref ulong state)
    {
        state ^= state << 13;
        state ^= state >> 7;
        state ^= state << 17;
        return state;
    }

    private static IEnumerable<(ulong Magnitude, ulong Sign)> Inputs(bool wide)
    {
        ulong sign = wide ? 1ul << 63 : 1ul << 31;
        ulong[] magnitudes = Boundaries(wide).SelectMany(bits => new[] { bits, bits | sign }).ToArray();
        foreach (ulong magnitude in magnitudes)
            foreach (ulong signBits in Signs(wide))
                yield return (magnitude, signBits);
        ulong state = 621815ul;
        for (int i = 0; i < 512; i++)
        {
            ulong a = Next(ref state), b = Next(ref state);
            yield return wide ? (a, b) : ((uint)a, (uint)b);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CopySignPreservesMagnitudeBitsAndUsesExactSignBit(int optimization)
    {
        // Keep data compact: expanded pair/expected arrays create thousands of
        // local initializer stores and make LLVM's optimization unnecessarily slow.
        var code = new StringBuilder("""
            using std; using std::Numerics;
            ulong Next(ref ulong state) nothrow {
                state = state ^ (state << 13);
                state = state ^ (state >> 7);
                state = state ^ (state << 17);
                return state;
            }
            int main() {
            """);
        foreach (bool wide in new[] { false, true })
        {
            var input = Inputs(wide).ToArray();
            string type = wide ? "double" : "float", integer = wide ? "ulong" : "uint", suffix = wide ? "ul" : "u";
            ulong[] expected = input.Select(pair => wide
                ? BitConverter.DoubleToUInt64Bits(Math.CopySign(BitConverter.UInt64BitsToDouble(pair.Magnitude), BitConverter.UInt64BitsToDouble(pair.Sign)))
                : BitConverter.SingleToUInt32Bits(MathF.CopySign(BitConverter.UInt32BitsToSingle((uint)pair.Magnitude), BitConverter.UInt32BitsToSingle((uint)pair.Sign)))).ToArray();
            // Also check the independent bit-level contract, particularly signaling NaNs.
            ulong mask = wide ? 1ul << 63 : 1ul << 31;
            for (int i = 0; i < input.Length; i++)
                Assert.Equal((input[i].Magnitude & ~mask) | (input[i].Sign & mask), expected[i]);
            ulong[] boundaries = Boundaries(wide), signs = Signs(wide);
            ulong finalState = 621815ul;
            for (int i = 0; i < 1024; i++) Next(ref finalState);
            code.AppendLine($$"""
                {
                    {{integer}}[{{boundaries.Length}}] magnitude = [{{string.Join(",",boundaries.Select(value=>value+suffix))}}];
                    {{integer}}[{{signs.Length}}] signs = [{{string.Join(",",signs.Select(value=>value+suffix))}}];
                    {{integer}} mask={{mask}}{{suffix}};
                    for(nuint i=0;i<{{boundaries.Length}};i++){
                        for(int negative=0;negative<2;negative++){
                            {{integer}} a=magnitude[i];if(negative!=0)a=a|mask;
                            for(nuint j=0;j<{{signs.Length}};j++){
                                {{integer}} b=signs[j];
                                {{integer}} expected=(a&{{mask-1}}{{suffix}})|(b&mask);
                                {{type}} value=Math::CopySign((a).reinterpret<{{type}}>(),(b).reinterpret<{{type}}>());
                                if((value).reinterpret<{{integer}}>()!=expected)return 1;
                            }
                        }
                    }
                    ulong state=621815ul;
                    for(int i=0;i<512;i++){
                        {{integer}} a=({{integer}})Next(ref state);
                        {{integer}} b=({{integer}})Next(ref state);
                        {{integer}} expected=(a&{{mask-1}}{{suffix}})|(b&mask);
                        {{type}} value=Math::CopySign((a).reinterpret<{{type}}>(),(b).reinterpret<{{type}}>());
                        if((value).reinterpret<{{integer}}>()!=expected)return 2;
                    }
                    if(state!={{finalState}}ul)return 3;
                }
                """);
        }
        code.AppendLine("return 0; }");
        var result = CompilerTestHelper.Run([
            new SourceFile("Math.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core/Numerics/Math.gf"))),
            new SourceFile("main.gf", code.ToString())
        ], optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }
}
