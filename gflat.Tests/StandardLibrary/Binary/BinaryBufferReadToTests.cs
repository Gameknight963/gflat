using System.Text;

namespace gflat.Tests.Std.Binary;

public sealed class BinaryBufferReadToTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DelimitedBorrowedFieldsMatchIndependentBoundedSearch(int optimization)
    {
        var random = new Random(517927);
        byte[][] corpus = [[], [0], [255], [0, 65, 0, 0, 255, 0, 90], [65, 66, 67],
            Enumerable.Range(0, 256).Select(i => (byte)i).ToArray(),
            .. Enumerable.Range(0, 12).Select(_ => { byte[] bytes = new byte[random.Next(1, 65)]; random.NextBytes(bytes); return bytes; })];
        var storage = new List<byte>();
        var rows = new List<(int Offset, int Count, int Start, byte Delimiter, bool Found, int Length, int After)>();
        foreach (byte[] bytes in corpus)
        {
            storage.Add(90); int offset = storage.Count; storage.AddRange(bytes); storage.Add(170);
            foreach (int start in new[] { 0, bytes.Length / 2, bytes.Length }.Distinct())
            foreach (byte delimiter in new byte[] { 0, 255, 65, bytes.Length == 0 ? (byte)7 : bytes[bytes.Length / 2] }.Distinct())
            {
                int index = Array.IndexOf(bytes, delimiter, start, bytes.Length - start);
                rows.Add((offset, bytes.Length, start, delimiter, index >= 0, index < 0 ? 0 : index - start, index < 0 ? start : index + 1));
            }
        }
        string Values(Func<(int Offset, int Count, int Start, byte Delimiter, bool Found, int Length, int After), int> pick)
            => string.Join(",", rows.Select(pick));
        var program = new StringBuilder("using Standard::Binary;\n");
        program.Append(Guards);
        program.Append($$"""
            int main() nothrow {
                if (GuardCases() != 0) return 1;
                byte[{{storage.Count}}] storage = [{{string.Join(",", storage.Select(value => "(byte)" + value))}}];
                byte[{{storage.Count}}] original = [{{string.Join(",", storage.Select(value => "(byte)" + value))}}];
                nuint[{{rows.Count}}] offsets = [{{Values(row => row.Offset)}}];
                nuint[{{rows.Count}}] counts = [{{Values(row => row.Count)}}];
                nuint[{{rows.Count}}] starts = [{{Values(row => row.Start)}}];
                byte[{{rows.Count}}] delimiters = [{{Values(row => row.Delimiter)}}];
                bool[{{rows.Count}}] found = [{{string.Join(",", rows.Select(row => row.Found ? "true" : "false"))}}];
                nuint[{{rows.Count}}] lengths = [{{Values(row => row.Length)}}];
                nuint[{{rows.Count}}] after = [{{Values(row => row.After)}}];
                byte marker = 12;
                for (nuint row = 0; row < {{rows.Count}}; row++) {
                    BinaryBufferReader reader = new((byte*)storage + offsets[row], counts[row]);
                    if (!reader.TrySeek(starts[row])) return 2;
                    BinaryBufferReader copied = reader;
                    BinaryBufferReader child = new(&marker, 1);
                    child.TrySkip(1);
                    if (reader.TryReadTo(delimiters[row], ref child) != found[row]) return 3;
                    if (reader.Position != after[row] || reader.Remaining != counts[row] - after[row] || reader.Length != counts[row]) return 4;
                    if (copied.Position != starts[row] || child.Position != 0 || child.Length != lengths[row] || child.Remaining != lengths[row]) return 5;
                    if (lengths[row] == 0) { if (child.Data != null) return 6; }
                    else { if (child.Data != (byte*)storage + offsets[row] + starts[row]) return 7; }
                    BinaryBufferReader childCopy = child;
                    for (nuint i = 0; i < lengths[row]; i++) {
                        byte value = 0;
                        if (!child.TryReadByte(ref value) || value != original[offsets[row] + starts[row] + i]) return 8;
                    }
                    if (child.Position != lengths[row] || childCopy.Position != 0 || copied.Position != starts[row] || reader.Position != after[row]) return 9;
                    if (!found[row]) {
                        child = new(&marker, 1);
                        if (reader.TryReadTo(delimiters[row], ref child) || reader.Position != starts[row] || child.Position != 0 || child.Length != 0 || child.Data != null) return 10;
                    }
                }
                for (nuint i = 0; i < {{storage.Count}}; i++) if (storage[i] != original[i]) return 11;
                if (marker != (byte)12) return 12;
                byte[256] all;
                for (nuint i = 0; i < 256; i++) all[i] = (byte)i;
                for (nuint delimiter = 0; delimiter < 256; delimiter++) {
                    BinaryBufferReader reader = new(all, 256); BinaryBufferReader child = new();
                    if (!reader.TryReadTo((byte)delimiter, ref child) || child.Length != delimiter || reader.Position != delimiter + 1) return 13;
                }
                for (nuint i = 0; i < 256; i++) if (all[i] != (byte)i) return 14;
                byte[7] fields = [(byte)0,(byte)65,(byte)0,(byte)0,(byte)255,(byte)0,(byte)90];
                BinaryBufferReader sequence = new(fields, 7); BinaryBufferReader field = new();
                if (!sequence.TryReadTo((byte)0, ref field) || field.Length != 0 || field.Data != null || sequence.Position != 1) return 15;
                if (!sequence.TryReadTo((byte)0, ref field) || field.Length != 1 || sequence.Position != 3 || field.Data![0] != (byte)65) return 16;
                if (!sequence.TryReadTo((byte)0, ref field) || field.Length != 0 || field.Data != null || sequence.Position != 4) return 17;
                if (!sequence.TryReadTo((byte)0, ref field) || field.Length != 1 || sequence.Position != 6 || field.Data![0] != (byte)255) return 18;
                if (sequence.TryReadTo((byte)0, ref field) || sequence.Position != 6 || field.Length != 0 || field.Data != null) return 19;
                byte tail = 0;
                if (!sequence.TryReadByte(ref tail) || tail != (byte)90 || sequence.Position != 7) return 20;
                return 0;
            }
            """);
        string ir = Emit(program.ToString());
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }

    [Fact]
    public void DelimiterSearchNeedsNoAllocationOrExceptionRuntime()
    {
        string ir = Emit("using Standard::Binary;\n" + Guards + "int main() nothrow => GuardCases();");
        Assert.Equal((false, false), NativeToolchain.RequiredNativeRuntimes(ir));
        foreach (string forbidden in new[] { "@malloc", "@free", "gflat$Memory$Allocate", "personality", "@__gflat_eh_" })
            Assert.DoesNotContain(forbidden, ir);
    }
    private const string Guards = """
        int GuardCases() nothrow {
            byte marker = 12;
            BinaryBufferReader child = new(&marker, 1);
            child.TrySkip(1);
            BinaryBufferReader empty = new();
            if (empty.TryReadTo((byte)0, ref child) || empty.Position != 0 || empty.Remaining != 0 || child.Length != 0 || child.Position != 0 || child.Data != null) return 1;
            empty = new(((nuint)1).reinterpret<readonly(byte)*>(), 0);
            child = new(&marker, 1);
            if (empty.TryReadTo((byte)255, ref child) || empty.Position != 0 || empty.Remaining != 0 || child.Length != 0 || child.Position != 0 || child.Data != null) return 2;
            if (marker != (byte)12) return 3;
            return 0;
        }
        """;
    private static string Emit(string code)
    {
        string[] names = ["core/IO/IByteReader.gf", "core/Binary/BinaryPrimitives.gf", "core/Binary/BinaryBufferReader.gf", "core/Memory/bytes.gf"];
        var sources = names.Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard", name))))
            .Append(new SourceFile("binary-read-to-tests.gf", code));
        return Compiler.Emit(sources, options: new() { NoStdLib = true, NoBuiltins = true, InternalizeFunctions = true });
    }
}
