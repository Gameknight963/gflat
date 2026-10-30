using System.Text;

namespace gflat.Tests.Std.Binary;

public sealed class VarIntTests
{
    private static byte[] Encode(ulong value)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            writer.Write7BitEncodedInt64(unchecked((long)value));
        return stream.ToArray();
    }

    private static void Run(string program, int optimization)
    {
        var sources = new[] {
            new SourceFile("VarInt.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard", "core", "Binary", "VarInt.gf"))),
            new SourceFile("main.gf", program)
        };
        var result = CompilerTestHelper.Run(sources, optimizationLevel: optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }

    private static string Corpus(bool wide)
    {
        var values = new SortedSet<ulong> { 0, 1, 127, 128, 150, 300, 16383, 16384, wide ? ulong.MaxValue : uint.MaxValue };
        int width = wide ? 64 : 32;
        for (int bit = 1; bit < width; bit++)
        {
            ulong value = 1UL << bit;
            values.Add(value - 1); values.Add(value); values.Add(value + 1);
        }
        var random = new Random(67043);
        for (int i = 0; i < 128; i++)
        {
            byte[] bytes = new byte[8]; random.NextBytes(bytes);
            ulong value = BitConverter.ToUInt64(bytes);
            values.Add(wide ? value : (uint)value);
        }
        byte[][] encoded = values.Select(Encode).ToArray();
        string type = wide ? "ulong" : "uint", suffix = wide ? "ul" : "u", name = wide ? "UInt64" : "UInt32";
        return $$"""
            using Standard; using Standard::Binary;
            int main() {
                {{type}}[{{values.Count}}] values = [{{string.Join(",", values.Select(v => v + suffix))}}];
                byte[{{encoded.Sum(bytes => bytes.Length) + 1}}] expected = [{{string.Join(",", encoded.SelectMany(bytes => bytes).Append((byte)255).Select(value => "(byte)" + value))}}];
                nuint[{{values.Count}}] lengths = [{{string.Join(",", encoded.Select(bytes => "(nuint)" + bytes.Length))}}];
                byte[12] output;
                nuint offset = 0;
                for (nuint index = 0; index < {{values.Count}}; index++) {
                    nuint length = lengths[index];
                    if (VarInt::Get{{name}}Length(values[index]) != length) return 1;
                    for (nuint capacity = 0; capacity < length; capacity++) {
                        for (nuint i = 0; i < 12; i++) output[i] = (byte)90;
                        nuint written = 999;
                        if (VarInt::TryWrite{{name}}(&output[1], capacity, values[index], ref written) || written != 0) return 2;
                        for (nuint i = 0; i < 12; i++) if (output[i] != (byte)90) return 3;
                    }
                    nuint written = 999;
                    if (!VarInt::TryWrite{{name}}(&output[1], length, values[index], ref written) || written != length) return 4;
                    if (output[0] != (byte)90 || output[length + 1] != (byte)90) return 5;
                    for (nuint i = 0; i < length; i++) if (output[i + 1] != expected[offset + i]) return 6;
                    for (nuint count = 0; count < length; count++) {
                        {{type}} value = 42{{suffix}}; nuint consumed = 999;
                        if (VarInt::Read{{name}}(&expected[offset], count, ref value, ref consumed) != VarInt::Status::NeedMore) return 7;
                        if (value != 42{{suffix}} || consumed != 0) return 8;
                    }
                    {{type}} value = 42{{suffix}}; nuint consumed = 999;
                    if (VarInt::Read{{name}}(&expected[offset], length + 1, ref value, ref consumed) != VarInt::Status::Done) return 9;
                    if (value != values[index] || consumed != length) return 10;
                    offset += length;
                }
                return 0;
            }
            """;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void UInt32BoundariesRandomValuesAndEveryTruncationMatchDotNet(int optimization) => Run(Corpus(false), optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void UInt64BoundariesRandomValuesAndEveryTruncationMatchDotNet(int optimization) => Run(Corpus(true), optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OverflowNoncanonicalAndEmptyInputsPreserveValue(int optimization)
    {
        Run("""
            using Standard; using Standard::Binary;
            int main() {
                byte[11] input;
                for (nuint i = 0; i < 11; i++) input[i] = (byte)128;
                for (int last = 0; last < 256; last++) {
                    input[4] = (byte)last;
                    uint value = 42u; nuint consumed = 999;
                    VarInt::Status expected = VarInt::Status::Invalid;
                    if (last > 0 && last <= 15) expected = VarInt::Status::Done;
                    if (VarInt::ReadUInt32(input, 5, ref value, ref consumed) != expected) return 1;
                    if (expected == VarInt::Status::Invalid && (value != 42u || consumed != 0)) return 2;
                    if (expected == VarInt::Status::Done && (value != (uint)last << 28 || consumed != 5)) return 3;
                }
                input[4] = (byte)128;
                for (int last = 0; last < 256; last++) {
                    input[9] = (byte)last;
                    ulong value = 42ul; nuint consumed = 999;
                    VarInt::Status expected = VarInt::Status::Invalid;
                    if (last == 1) expected = VarInt::Status::Done;
                    if (VarInt::ReadUInt64(input, 10, ref value, ref consumed) != expected) return 4;
                    if (expected == VarInt::Status::Invalid && (value != 42ul || consumed != 0)) return 5;
                    if (expected == VarInt::Status::Done && (value != 9223372036854775808ul || consumed != 10)) return 6;
                }
                for (nuint length = 2; length <= 10; length++) {
                    for (nuint i = 0; i < length; i++) input[i] = (byte)128;
                    input[length - 1] = (byte)0;
                    input[0] = (byte)129;
                    ulong value = 42ul; nuint consumed = 999;
                    if (VarInt::ReadUInt64(input, length, ref value, ref consumed) != VarInt::Status::Invalid || value != 42ul || consumed != 0) return 7;
                    if (length <= 5) {
                        uint narrow = 42u;
                        if (VarInt::ReadUInt32(input, length, ref narrow, ref consumed) != VarInt::Status::Invalid || narrow != 42u || consumed != 0) return 8;
                    }
                }
                byte*? absent = null; ulong value = 42ul; nuint consumed = 999;
                if (VarInt::ReadUInt64(absent!, 0, ref value, ref consumed) != VarInt::Status::NeedMore || value != 42ul || consumed != 0) return 9;
                if (VarInt::TryWriteUInt64(absent!, 0, 0ul, ref consumed) || consumed != 0) return 10;
                return 0;
            }
            """, optimization);
    }

    [Fact]
    public void DotNetReferenceMatchesKnownUnsignedProtobufVectors()
    {
        Assert.Equal(new byte[] { 0 }, Encode(0));
        Assert.Equal(new byte[] { 1 }, Encode(1));
        Assert.Equal(new byte[] { 127 }, Encode(127));
        Assert.Equal(new byte[] { 128, 1 }, Encode(128));
        Assert.Equal(new byte[] { 150, 1 }, Encode(150));
        Assert.Equal(new byte[] { 172, 2 }, Encode(300));
        Assert.Equal(new byte[] { 255, 255, 255, 255, 15 }, Encode(uint.MaxValue));
        Assert.Equal(new byte[] { 255, 255, 255, 255, 255, 255, 255, 255, 255, 1 }, Encode(ulong.MaxValue));
    }
}
