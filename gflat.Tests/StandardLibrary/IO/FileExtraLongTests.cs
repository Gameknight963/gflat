using System.Buffers.Binary;

namespace gflat.Tests.Std.IO;

public sealed class FileExtraLongTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void IndependentHostRecordsAndOldWordCodecsVerifyBothDirections(int optimization)
    {
        List<Int128> values = [0, 1, -1, Int128.MinValue, Int128.MaxValue];
        foreach (int bit in new[] { 1, 31, 63, 64, 65, 126 })
        {
            values.Add((Int128)1 << bit);
            values.Add(-((Int128)1 << bit));
        }
        var random = new Random(36217);
        byte[] bytes = new byte[16];
        for (int i = 0; i < 20; i++)
        {
            random.NextBytes(bytes);
            values.Add(BinaryPrimitives.ReadInt128LittleEndian(bytes));
        }
        WithDirectory(directory =>
        {
            byte[] records = new byte[values.Count * 32];
            for (int i = 0; i < values.Count; i++)
            {
                BinaryPrimitives.WriteInt128LittleEndian(records.AsSpan(i * 32), values[i]);
                BinaryPrimitives.WriteInt128BigEndian(records.AsSpan(i * 32 + 16), values[i]);
            }
            string inputPath = Path.Combine(directory, "input.bin");
            string outputPath = Path.Combine(directory, "output.bin");
            File.WriteAllBytes(inputPath, records);
            Run($$$"""
                using std; using std::IO;
                bool deny = false;
                namespace Memory {
                    public replace void*? Allocate(nuint count) {
                        if (deny) return null;
                        return malloc(count);
                    }
                    public replace void Free(void* value) nothrow { free(value); }
                    private extern void*? malloc(nuint count);
                    private extern void free(void* value);
                }
                int main() {
                    File input = File::OpenRead({{{Literal(inputPath)}}});
                    File reference = File::OpenRead({{{Literal(inputPath)}}});
                    File output = File::OpenWrite({{{Literal(outputPath)}}});
                    deny = true;
                    for (int row = 0; row < {{{values.Count}}}; row++) {
                        extralong actual = FileBinary::ReadExtraLongLittleEndian(&input);
                        ulong low = FileBinary::ReadUInt64LittleEndian(&reference);
                        ulong high = FileBinary::ReadUInt64LittleEndian(&reference);
                        if ((ulong)actual != low || (ulong)(actual >> 64) != high) return 1;
                        // Writing uses only the independently decoded old 64-bit words.
                        extralong expected = ((extralong)high << 64) | (extralong)low;
                        FileBinary::WriteExtraLongLittleEndian(&output, expected);
                        actual = FileBinary::ReadExtraLongBigEndian(&input);
                        high = FileBinary::ReadUInt64BigEndian(&reference);
                        low = FileBinary::ReadUInt64BigEndian(&reference);
                        if ((ulong)actual != low || (ulong)(actual >> 64) != high) return 2;
                        expected = ((extralong)high << 64) | (extralong)low;
                        FileBinary::WriteExtraLongBigEndian(&output, expected);
                    }
                    deny = false;
                    if (input.ReadByte() != -1 || reference.ReadByte() != -1) return 3;
                    if (!input.IsOpen || !reference.IsOpen || !output.IsOpen) return 4;
                    input.Close(); reference.Close(); output.Close();
                    return 0;
                }
                """, optimization);
            Assert.Equal(records, File.ReadAllBytes(outputPath));
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryTruncatedWidthAndStreamErrorPreservesAssignmentAndBorrow(int optimization)
    {
        WithDirectory(directory =>
        {
            var paths = new List<string>();
            for (int count = 0; count < 16; count++)
            {
                string path = Path.Combine(directory, $"short-{count}.bin");
                File.WriteAllBytes(path, Enumerable.Range(0, count).Select(i => (byte)(128 + i)).ToArray());
                paths.Add(Literal(path));
            }
            string target = Path.Combine(directory, "output.bin");
            Run($$$"""
                using std; using std::IO;
                int main() {
                    readonly(char)*[16] paths = [{{{string.Join(", ", paths)}}}];
                    for (nuint count = 0; count < 16; count++) {
                        for (int order = 0; order < 2; order++) {
                            File input = File::OpenRead(paths[count]);
                            extralong saved = (extralong)71;
                            try {
                                if (order == 0) saved = FileBinary::ReadExtraLongLittleEndian(&input);
                                else saved = FileBinary::ReadExtraLongBigEndian(&input);
                                return 1;
                            }
                            catch (ref EndOfStreamException error) { }
                            if (saved != (extralong)71 || !input.IsOpen || !input.EndOfFile || input.ReadByte() != -1) return 2;
                        }
                    }
                    File output = File::OpenWrite({{{Literal(target)}}});
                    extralong saved = (extralong)71;
                    try { saved = FileBinary::ReadExtraLongLittleEndian(&output); return 3; }
                    catch (ref EndOfStreamException error) { return 4; }
                    catch (ref IOException error) { }
                    if (saved != (extralong)71 || !output.IsOpen || output.EndOfFile) return 5;
                    output.ClearError();
                    FileBinary::WriteExtraLongBigEndian(&output, (extralong)0);
                    output.Close();
                    int caught = 0;
                    try { saved = FileBinary::ReadExtraLongLittleEndian(&output); }
                    catch (ref EndOfStreamException error) { return 6; }
                    catch (ref IOException error) { caught++; }
                    try { saved = FileBinary::ReadExtraLongBigEndian(&output); }
                    catch (ref EndOfStreamException error) { return 7; }
                    catch (ref IOException error) { caught++; }
                    try { FileBinary::WriteExtraLongLittleEndian(&output, (extralong)1); }
                    catch (ref IOException error) { caught++; }
                    try { FileBinary::WriteExtraLongBigEndian(&output, (extralong)1); }
                    catch (ref IOException error) { caught++; }
                    if (caught != 4 || saved != (extralong)71 || output.IsOpen) return 8;
                    File input = File::OpenRead({{{Literal(target)}}});
                    try { FileBinary::WriteExtraLongLittleEndian(&input, (extralong)1); return 9; }
                    catch (ref IOException error) { }
                    if (!input.IsOpen) return 10;
                    return 0;
                }
                """, optimization);
            Assert.Equal(new byte[16], File.ReadAllBytes(target));
        });
    }

    private static void Run(string code, int optimization)
    {
        var sources = new[] { "core/IO/IOException.gf", "core/IO/EndOfStreamException.gf", "core/IO/IByteReader.gf", "libc/IO/File.gf", "libc/IO/FileBinary.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", name))))
            .Append(new SourceFile("file-extra-long.gf", code));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }

    private static void WithDirectory(Action<string> action)
    {
        string path = Path.Combine(Path.GetTempPath(), "gflat-file-extra-long-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        try { action(path); }
        finally { Directory.Delete(path, true); }
    }

    private static string Literal(string value)
        => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
