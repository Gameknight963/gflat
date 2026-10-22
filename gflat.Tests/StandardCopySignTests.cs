using System.Text;

namespace gflat.Tests;

public sealed class StandardCopySignTests
{
    private static IEnumerable<(ulong Magnitude, ulong Sign)> Inputs(bool wide)
    {
        ulong sign = wide ? 1ul << 63 : 1ul << 31;
        ulong exponent = wide ? 0x7ff0000000000000ul : 0x7f800000ul;
        ulong fraction = wide ? 0x000ffffffffffffful : 0x007ffffful;
        ulong one = wide ? 0x3ff0000000000000ul : 0x3f800000ul;
        var boundaries = new HashSet<ulong> { 0, 1, fraction, fraction + 1, one, exponent - 1, exponent, exponent | fraction };
        // Each payload bit, including the quiet/signaling bit, in both signs.
        for (int bit = 0; bit < (wide ? 52 : 23); bit++) boundaries.Add(exponent | (1ul << bit));
        ulong[] magnitudes = boundaries.SelectMany(bits => new[] { bits, bits | sign }).ToArray();
        ulong[] signs = [0, sign, one, one | sign, exponent, exponent | sign,
            exponent | 1, exponent | sign | 1, exponent | fraction, exponent | fraction | sign];
        foreach (ulong magnitude in magnitudes)
            foreach (ulong signBits in signs)
                yield return (magnitude, signBits);
        var random = new Random(621815);
        byte[] bytes = new byte[16];
        for (int i = 0; i < 512; i++)
        {
            random.NextBytes(bytes);
            ulong a = BitConverter.ToUInt64(bytes), b = BitConverter.ToUInt64(bytes, 8);
            yield return wide ? (a, b) : ((uint)a, (uint)b);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CopySignPreservesMagnitudeBitsAndUsesExactSignBit(int optimization)
    {
        var code = new StringBuilder("using std; int main() {\n");
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
            code.AppendLine($$"""
                {
                    {{integer}}[{{input.Length}}] magnitude = [{{string.Join(",", input.Select(pair => pair.Magnitude + suffix))}}];
                    {{integer}}[{{input.Length}}] sign = [{{string.Join(",", input.Select(pair => pair.Sign + suffix))}}];
                    {{integer}}[{{input.Length}}] expected = [{{string.Join(",", expected.Select(value => value + suffix))}}];
                    for (nuint i = 0; i < {{input.Length}}; i++) {
                        {{integer}} a = magnitude[i];
                        {{integer}} b = sign[i];
                        {{type}} value = Math::CopySign(*(({{type}}*)&a), *(({{type}}*)&b));
                        if (*(({{integer}}*)&value) != expected[i]) return 1;
                    }
                }
                """);
        }
        code.AppendLine("return 0; }");
        var result = CompilerTestHelper.Run([
            new SourceFile("Math.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core/Math.gf"))),
            new SourceFile("main.gf", code.ToString())
        ], optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }
}
