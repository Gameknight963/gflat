using System.Numerics;
using System.Text;

namespace gflat.Tests;

public sealed class StandardZigZagTests
{
    private static string Corpus(bool wide)
    {
        int width = wide ? 64 : 32;
        BigInteger modulus = BigInteger.One << width;
        ulong mask = wide ? ulong.MaxValue : uint.MaxValue;
        var inputs = new SortedSet<ulong> { 0, 1, 2, 3, mask, mask - 1, mask - 2 };
        for (int bit = 0; bit < width; bit++)
        {
            ulong power = 1ul << bit;
            foreach (ulong value in new[] { power - 1, power, unchecked(power + 1) })
            {
                inputs.Add(value & mask);
                inputs.Add(unchecked(0ul - value) & mask);
            }
        }
        var random = new Random(205713);
        byte[] bytes = new byte[8];
        for (int i = 0; i < 256; i++)
        {
            random.NextBytes(bytes);
            inputs.Add(BitConverter.ToUInt64(bytes) & mask);
        }
        ulong[] encoded = inputs.Select(bits =>
        {
            BigInteger value = bits;
            if (value >= modulus / 2) value -= modulus;
            return (ulong)(value.Sign < 0 ? -value * 2 - 1 : value * 2);
        }).ToArray();
        ulong[] decoded = inputs.Select(bits =>
        {
            BigInteger value = bits;
            value = value.IsEven ? value / 2 : -(value + 1) / 2;
            if (value.Sign < 0) value += modulus;
            return (ulong)value;
        }).ToArray();
        string unsigned = wide ? "ulong" : "uint", signed = wide ? "long" : "int";
        string suffix = wide ? "ul" : "u", codec = wide ? "UInt64" : "UInt32";
        return $$"""
            {
                {{unsigned}}[{{inputs.Count}}] inputs = [{{string.Join(",", inputs.Select(value => value + suffix))}}];
                {{unsigned}}[{{inputs.Count}}] encoded = [{{string.Join(",", encoded.Select(value => value + suffix))}}];
                {{unsigned}}[{{inputs.Count}}] decoded = [{{string.Join(",", decoded.Select(value => value + suffix))}}];
                byte[12] output;
                for (nuint index = 0; index < {{inputs.Count}}; index++) {
                    {{signed}} original = ({{signed}})inputs[index];
                    {{unsigned}} zigzag = VarInt::ZigZagEncode(original);
                    if (zigzag != encoded[index]) return 1;
                    // Decode arbitrary unsigned inputs against an independent arithmetic
                    // oracle as well as checking the encode/decode round trip.
                    if (({{unsigned}})VarInt::ZigZagDecode(inputs[index]) != decoded[index]) return 2;
                    if (VarInt::ZigZagDecode(zigzag) != original) return 3;
                    for (nuint i = 0; i < 12; i++) output[i] = (byte)90;
                    nuint written = 0;
                    if (!VarInt::TryWrite{{codec}}(&output[1], 10, zigzag, ref written)) return 4;
                    if (written != VarInt::Get{{codec}}Length(zigzag)) return 5;
                    if (output[0] != (byte)90 || output[written + 1] != (byte)90) return 6;
                    {{unsigned}} restored = 0{{suffix}};
                    nuint consumed = 0;
                    if (VarInt::Read{{codec}}(&output[1], written, ref restored, ref consumed) != VarInt::Status::Done) return 7;
                    if (consumed != written || VarInt::ZigZagDecode(restored) != original) return 8;
                }
            }
            """;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SignedMappingMatchesIndependentArithmeticAndComposesWithUnsignedCodec(int optimization)
    {
        var code = new StringBuilder("using Standard; using Standard::Binary; int main() nothrow {\n");
        code.AppendLine(Corpus(false));
        code.AppendLine(Corpus(true));
        code.AppendLine("return 0; }");
        var sources = new[] {
            new SourceFile("VarInt.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard/core/Binary/VarInt.gf"))),
            new SourceFile("main.gf", code.ToString())
        };
        // The complete source and these calls type-check/emit without implicit std
        // definitions or exception support; native semantics are checked below.
        Compiler.Emit(sources, options: new() { NoStdLib = true, NoBuiltins = true });
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }
}
