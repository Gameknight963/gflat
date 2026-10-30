using System.Buffers.Binary;

namespace gflat.Tests.Std.Binary;

public sealed class BinaryCursorSeekTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AbsolutePositionsMatchBoundedHostTableWithoutChangingAnyBytes(int optimization)
    {
        ulong maximum = UIntPtr.Size == 8 ? ulong.MaxValue : uint.MaxValue;
        var rows = new List<(ulong Length, ulong Target, bool Success, ulong Position)>();
        foreach (ulong length in new ulong[] { 0, 1, 2, 7, 8, 15, 16, 31, 32, 63, 64, 255 })
        {
            ulong position = 0;
            foreach (ulong target in new ulong[] { 0UL, length, length / 2, length + 1, maximum, 1, 0, maximum, length })
            {
                bool success = target <= length;
                if (success) position = target;
                rows.Add((length, target, success, position));
            }
        }
        string Number(ulong value) => value == maximum ? "((nuint)0 - (nuint)1)" : $"(nuint){value}";
        string table = string.Join(",", rows.SelectMany(row => new[]
        {
            Number(row.Length), Number(row.Target), row.Success ? "(nuint)1" : "(nuint)0", Number(row.Position)
        }));
        Run($$"""
            using std; using std::Binary;
            int main()
            {
                nuint[{{rows.Count * 4}}] table = [{{table}}];
                byte[257] bytes;
                for (nuint i = 0; i < 257; i++) bytes[i] = (byte)(i * 13 + 7);
                BinaryBufferReader reader = new(&bytes[1], 0);
                BinaryBufferWriter writer = new(&bytes[1], 0);
                nuint previousLength = 256;
                for (nuint row = 0; row < {{rows.Count}}; row++)
                {
                    nuint length = table[row * 4];
                    nuint target = table[row * 4 + 1];
                    bool expected = table[row * 4 + 2] != 0;
                    nuint position = table[row * 4 + 3];
                    if (length != previousLength)
                    {
                        reader = new(&bytes[1], length); writer = new(&bytes[1], length);
                        previousLength = length;
                    }
                    if (reader.TrySeek(target) != expected || writer.TrySeek(target) != expected) return 1;
                    if (reader.Position != position || writer.Position != position) return 2;
                    if (reader.Remaining != length - position || writer.Remaining != length - position) return 3;
                    if (reader.Length != length || writer.Capacity != length || reader.Data != &bytes[1] || writer.Data != &bytes[1]) return 4;
                    for (nuint i = 0; i < 257; i++) if (bytes[i] != (byte)(i * 13 + 7)) return 5;
                }
                BinaryBufferReader emptyRead = new(); BinaryBufferWriter emptyWrite = new();
                BinaryBufferReader fakeRead = new(((nuint)1).reinterpret<readonly(byte)*>(), 0);
                BinaryBufferWriter fakeWrite = new(((nuint)1).reinterpret<byte*>(), 0);
                nuint maximum = (nuint)0 - (nuint)1;
                if (!emptyRead.TrySeek(0) || !emptyWrite.TrySeek(0) || !fakeRead.TrySeek(0) || !fakeWrite.TrySeek(0)) return 6;
                if (emptyRead.TrySeek(1) || emptyWrite.TrySeek(maximum) || fakeRead.TrySeek(maximum) || fakeWrite.TrySeek(1)) return 7;
                if (emptyRead.Position != 0 || emptyWrite.Position != 0 || fakeRead.Position != 0 || fakeWrite.Position != 0) return 8;
                if (emptyRead.Data != null || emptyWrite.Data != null || fakeRead.Data != ((nuint)1).reinterpret<readonly(byte)*>() || fakeWrite.Data != ((nuint)1).reinterpret<byte*>()) return 9;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CopiesAndChildrenKeepIndependentPositionsAndCapturedBounds(int optimization)
        => Run("""
            using std; using std::Binary;
            int main()
            {
                byte[18] bytes;
                for (nuint i = 0; i < 18; i++) bytes[i] = (byte)(i + 20);
                BinaryBufferReader reader = new(&bytes[1], 16);
                BinaryBufferWriter writer = new(&bytes[1], 16);
                if (!reader.TrySeek(4) || !writer.TrySeek(4)) return 1;
                BinaryBufferReader copyRead = reader; BinaryBufferWriter copyWrite = writer;
                BinaryBufferReader childRead = new(); BinaryBufferWriter childWrite = new();
                if (!reader.TryReadSlice(5, ref childRead) || !writer.TryWriteSlice(5, ref childWrite)) return 2;
                if (reader.Position != 9 || writer.Position != 9 || copyRead.Position != 4 || copyWrite.Position != 4) return 3;
                if (childRead.Data != &bytes[5] || childWrite.Data != &bytes[5] || childRead.Length != 5 || childWrite.Capacity != 5) return 4;
                if (!childRead.TrySeek(5) || !childWrite.TrySeek(5) || childRead.TrySeek(6) || childWrite.TrySeek(6)) return 5;
                if (childRead.Position != 5 || childWrite.Position != 5 || reader.Position != 9 || writer.Position != 9) return 6;
                if (!copyRead.TrySeek(2) || !copyWrite.TrySeek(2) || !reader.TrySeek(16) || !writer.TrySeek(16)) return 7;
                if (childRead.Position != 5 || childWrite.Position != 5 || copyRead.Position != 2 || copyWrite.Position != 2) return 8;
                if (!childRead.TrySeek(0) || !childWrite.TrySeek(0)) return 9;
                if (reader.Position != 16 || writer.Position != 16 || copyRead.Position != 2 || copyWrite.Position != 2) return 10;
                reader.Reset(); writer.Reset();
                if (!reader.TrySkip(3) || !writer.TrySkip(3) || reader.Position != 3 || writer.Position != 3) return 11;
                BinaryBufferReader zeroRead = new(); BinaryBufferWriter zeroWrite = new();
                if (!reader.TryReadSlice(0, ref zeroRead) || !writer.TryWriteSlice(0, ref zeroWrite)) return 12;
                if (!zeroRead.TrySeek(0) || !zeroWrite.TrySeek(0) || zeroRead.TrySeek(1) || zeroWrite.TrySeek(1)) return 13;
                for (nuint i = 0; i < 18; i++) if (bytes[i] != (byte)(i + 20)) return 14;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PacketHeaderBackfillAndParserCheckpointsMatchHostBytes(int optimization)
    {
        int[] lengths = [0, 1, 2, 7, 8, 15, 31];
        var expected = new List<byte>();
        foreach (int length in lengths)
        {
            byte[] packet = new byte[length + 4];
            packet[0] = 7;
            BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(1), (ushort)length);
            for (int index = 0; index < length; index++) packet[index + 3] = (byte)(index * 17 + length);
            packet[^1] = 126;
            expected.AddRange(packet);
        }
        Run($$"""
            using std; using std::Binary;
            int main()
            {
                int[{{lengths.Length}}] lengths = [{{string.Join(',', lengths)}}];
                byte[{{expected.Count}}] expected = [{{string.Join(',', expected.Select(value => "(byte)" + value))}}];
                nuint offset = 0;
                for (nuint row = 0; row < {{lengths.Length}}; row++)
                {
                    nuint length = (nuint)lengths[row];
                    byte[66] bytes;
                    for (nuint i = 0; i < 66; i++) bytes[i] = (byte)204;
                    BinaryBufferWriter writer = new(&bytes[1], 64);
                    if (!writer.TryWriteByte((byte)7) || !writer.TrySkip(2)) return 1;
                    for (nuint i = 0; i < length; i++) if (!writer.TryWriteByte((byte)(i * 17 + length))) return 2;
                    nuint end = writer.Position;
                    if (!writer.TrySeek(1) || !writer.TryWriteUInt16BigEndian((ushort)length)) return 3;
                    if (!writer.TrySeek(end) || !writer.TryWriteByte((byte)126) || writer.Position != length + 4) return 4;
                    for (nuint i = 0; i < length + 4; i++) if (bytes[i + 1] != expected[offset + i]) return 5;
                    if (bytes[0] != (byte)204) return 6;
                    for (nuint i = length + 5; i < 66; i++) if (bytes[i] != (byte)204) return 7;
                    BinaryBufferReader reader = new(&bytes[1], writer.Position);
                    byte tag = (byte)0; ushort count = (ushort)0;
                    if (!reader.TryReadByte(ref tag) || tag != (byte)7 || !reader.TryReadUInt16BigEndian(ref count) || (nuint)count != length) return 8;
                    nuint checkpoint = reader.Position;
                    if (reader.TrySeek((nuint)0 - (nuint)1) || reader.Position != checkpoint) return 9;
                    if (!reader.TrySkip(length) || !reader.TryReadByte(ref tag) || tag != (byte)126) return 10;
                    if (!reader.TrySeek(checkpoint)) return 11;
                    for (nuint i = 0; i < length; i++) if (!reader.TryReadByte(ref tag) || tag != (byte)(i * 17 + length)) return 12;
                    if (reader.Remaining != 1 || !reader.TrySeek(reader.Length) || reader.TryReadByte(ref tag) || tag != (byte)0) return 13;
                    offset += length + 4;
                }
                if (offset != {{expected.Count}}) return 14;
                return 0;
            }
            """, optimization);
    }

    private static void Run(string program, int optimization)
    {
        string[] names = ["Binary/BinaryPrimitives.gf", "IO/IByteReader.gf", "Binary/BinaryBufferReader.gf", "Binary/BinaryBufferWriter.gf", "Memory/bytes.gf"];
        var sources = names.Select(name =>
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Std", "core", name);
            return new SourceFile(path, File.ReadAllText(path));
        }).Append(new SourceFile("binary-cursor-seek.gf", program));
        string ir = Compiler.Emit(sources, options: new() { NoStdLib = true, NoBuiltins = true, InternalizeFunctions = true });
        foreach (string forbidden in new[] { "invoke ", "personality", "@__gflat_eh_", "Memory$Allocate", "@malloc", "@free", "__gflat_gc" })
            Assert.DoesNotContain(forbidden, ir);
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }
}
