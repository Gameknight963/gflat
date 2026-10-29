using System.Buffers.Binary;

namespace gflat.Tests.Std.Binary;

public class BitConverterTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FloatAndDoubleReinterpretationPreservesDotNetBitPatternsWithoutArithmetic(int optimization)
    {
        uint[] floatEdges = [0u, 0x80000000u, 1u, 0x80000001u, 0x007fffffu, 0x00800000u, 0x80800000u, 0x7f7fffffu, 0xff7fffffu,
            0x3f800000u, 0xbf800000u, 0x3eaaaaabu, 0x7f800000u, 0xff800000u, 0x7fc00000u, 0xffc12345u, 0x7f800001u, 0xff812345u];
        ulong[] doubleEdges = [0ul, 0x8000000000000000ul, 1ul, 0x8000000000000001ul, 0x000ffffffffffffful, 0x0010000000000000ul,
            0x8010000000000000ul, 0x7feffffffffffffful, 0xffeffffffffffffful, 0x3ff0000000000000ul, 0xbff0000000000000ul,
            0x3fd5555555555555ul, 0x7ff0000000000000ul, 0xfff0000000000000ul, 0x7ff8000000000000ul, 0xfff8123456789abcul,
            0x7ff0000000000001ul, 0xfff0123456789abcul];
        var random = new Random(0x13bd852);
        byte[] randomBytes = new byte[256 * 8]; random.NextBytes(randomBytes);
        uint[] floats = floatEdges.Concat(Enumerable.Range(0, 256).Select(i => BinaryPrimitives.ReadUInt32LittleEndian(randomBytes.AsSpan(i * 8, 4)))).ToArray();
        ulong[] doubles = doubleEdges.Concat(Enumerable.Range(0, 256).Select(i => BinaryPrimitives.ReadUInt64LittleEndian(randomBytes.AsSpan(i * 8, 8)))).ToArray();
        uint[] floatReference = floats.Select(bits => BitConverter.SingleToUInt32Bits(BitConverter.UInt32BitsToSingle(bits))).ToArray();
        ulong[] doubleReference = doubles.Select(bits => BitConverter.DoubleToUInt64Bits(BitConverter.UInt64BitsToDouble(bits))).ToArray();
        Assert.Equal(floats, floatReference);
        Assert.Equal(doubles, doubleReference);
        string code = $$"""
            using std; using std::Binary;
            int main() {
                uint[{{floats.Length}}] singles = [{{string.Join(",", floats.Select(bits => bits + "u"))}}];
                uint[{{floatReference.Length}}] expectedSingles = [{{string.Join(",", floatReference.Select(bits => bits + "u"))}}];
                ulong[{{doubles.Length}}] doubles = [{{string.Join(",", doubles.Select(bits => bits + "ul"))}}];
                ulong[{{doubleReference.Length}}] expectedDoubles = [{{string.Join(",", doubleReference.Select(bits => bits + "ul"))}}];
                for (int i = 0; i < {{floats.Length}}; i++) {
                    float decoded = BitConverter::UInt32BitsToFloat(singles[i]);
                    if (*((uint*)&decoded) != expectedSingles[i]) return 1;
                    float reference = *((float*)&singles[i]);
                    if (BitConverter::FloatToUInt32Bits(reference) != expectedSingles[i]) return 2;
                    if (BitConverter::FloatToUInt32Bits(decoded) != singles[i]) return 3;
                }
                for (int i = 0; i < {{doubles.Length}}; i++) {
                    double decoded = BitConverter::UInt64BitsToDouble(doubles[i]);
                    if (*((ulong*)&decoded) != expectedDoubles[i]) return 4;
                    double reference = *((double*)&doubles[i]);
                    if (BitConverter::DoubleToUInt64Bits(reference) != expectedDoubles[i]) return 5;
                    if (BitConverter::DoubleToUInt64Bits(decoded) != doubles[i]) return 6;
                }
                return 0;
            }
            """;
        SourceFile[] sources = [new("BitConverter.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core/Binary/BitConverter.gf"))), new("main.gf", code)];
        string ir = Compiler.Emit(sources, options: new() { NoStdLib = true, InternalizeFunctions = true });
        foreach (string forbidden in new[] { "@__gflat_eh_", "personality", "gflat$Memory$", "@malloc", "fptoui", "fptosi", "uitofp", "sitofp", " fadd ", " fmul ", " fcmp " })
            Assert.DoesNotContain(forbidden, ir);
        string directory = Path.Combine(Path.GetTempPath(), "gflat-bit-converter-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "bits.ll"), output = Path.Combine(directory, "bits.exe");
            File.WriteAllText(input, ir);
            var compiled = NativeToolchain.Compile(input, output, optimizationLevel: optimization);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            Assert.Equal(0, NativeToolchain.Run(output, []).ExitCode);
        }
        finally { Directory.Delete(directory, true); }
    }
}
