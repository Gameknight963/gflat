namespace gflat.Tests.Std.IO;

public sealed class MemoryStreamBytesTests
{
    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("MEMORY_STREAM_BYTES.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SnapshotsOwnTheirStorageAndFailuresPreserveSourceAndDestination(int optimization)
    {
        var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/IO/MemoryStream.gf", "core/IO/IByteReader.gf", "core/IO/IOException.gf",
                "core/IO/SeekOrigin.gf", "core/Collections/vector_resize.gf"))
            .Append(new SourceFile("memory-stream-bytes.gf", """
            using Standard;
            using Standard::Collections;
            using Standard::IO;

            extern "C" void*? malloc(nuint size);
            extern "C" void free(void* storage);
            int live = 0;
            int attempts = 0;
            bool deny = false;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes)
                {
                    attempts++;
                    if (deny) return null;
                    void*? result = malloc(bytes);
                    if (result != null) live++;
                    return result;
                }
                public replace void Free(void* storage) nothrow { live--; free(storage); }
            }
            Vector<byte> Snapshot(immutable(MemoryStream)* source) => source.ToBytes();
            int main()
            {
                {
                    MemoryStream empty = new();
                    empty.Position = 100l;
                    int before = attempts;
                    deny = true;
                    Vector<byte> bytes = empty.ToBytes();
                    deny = false;
                    if (bytes.Count != 0 || attempts != before || empty.Position != 100l) return 1;
                }
                for (nuint length = 1; length <= 256; length++)
                {
                    Vector<byte> saved = new();
                    {
                        MemoryStream source = new(512);
                        char[256] input;
                        for (nuint i = 0; i < length; i++) input[i] = (char)(byte)i;
                        source.Write(&input[0], length);
                        for (int form = 0; form < 4; form++)
                        {
                            long position = 0l;
                            if (form == 1) position = (long)length / 2l;
                            else if (form == 2) position = (long)length;
                            else if (form == 3) position = (long)length + 10l;
                            source.Position = position;
                            Vector<byte> bytes = Snapshot(&source);
                            if (bytes.Count != length || source.Position != position || source.Length != (long)length) return 2;
                            for (nuint i = 0; i < length; i++) if (bytes[i] != (byte)i) return 3;
                            bytes[0] = (byte)255;
                            source.Position = 0l;
                            if (source.ReadByte() != 0) return 4;
                        }
                        saved.Add((byte)123);
                        source.Position = 999l;
                        int beforeLive = live;
                        bool caught = false;
                        deny = true;
                        try { saved = source.ToBytes(); }
                        catch (Exception* error) { caught = true; }
                        deny = false;
                        if (!caught || live != beforeLive || source.Position != 999l || source.Length != (long)length) return 5;
                        if (saved.Count != 1 || saved[0] != (byte)123) return 6;
                        saved = source.ToBytes();
                        source.Clear();
                        if (saved.Count != length) return 7;
                    }
                    for (nuint i = 0; i < length; i++) if (saved[i] != (byte)i) return 8;
                }
                if (live != 0) return 9;
                return 0;
            }
            """)), optimization);
        Assert.True(result.ExitCode == 0, $"Snapshot exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
