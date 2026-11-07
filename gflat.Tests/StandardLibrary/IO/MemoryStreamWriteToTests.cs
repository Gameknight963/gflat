namespace gflat.Tests.Std.IO;

public sealed class MemoryStreamWriteToTests
{
    private static void Run(string program, int optimization)
    {
        var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/IO/IByteReader.gf", "core/IO/IOException.gf", "core/IO/SeekOrigin.gf", "core/IO/MemoryStream.gf", "core/Collections/vector_resize.gf"))
            .Append(new SourceFile("memory-stream-write-to.gf", program)), optimization);
        Assert.True(result.ExitCode == 0, $"WriteTo exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("MEMORY_STREAM_WRITE_TO.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EntireContentAndDestinationPositionMatchDotNetRegardlessOfSourcePosition(int optimization)
    {
        string path = Path.Combine(Path.GetTempPath(), "gflat_stream_write_to_" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            for (int length = 1; length <= 32; length++)
            foreach (int position in new[] { 0, length / 2, length, length + 8 })
            foreach (int targetPosition in new[] { 0, 2, 5, 9 })
            {
                byte[] data = Enumerable.Range(0, length).Select(i => (byte)(i * 37)).ToArray();
                using var source = new MemoryStream(data);
                using var target = new MemoryStream();
                target.Write(new byte[] { 201, 202, 203, 204, 205 });
                source.Position = position; target.Position = targetPosition;
                source.WriteTo(target);
                Assert.Equal(position, source.Position);
                writer.Write((long)length); writer.Write((long)position); writer.Write((long)targetPosition);
                writer.Write(target.Length); writer.Write(target.Position);
                writer.Write(data); writer.Write(target.ToArray());
            }
            string literal = "\"" + path.Replace("\\", "\\\\") + "\"";
            Run($$"""
                using Standard;
                using Standard::IO;
                using Standard::Memory;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" nuint fread(void* buffer, nuint size, nuint count, void* file);
                extern "C" int fclose(void* file);
                void Send(immutable(MemoryStream)* source, ITextWriter* target) => source.WriteTo(target);
                int main()
                {
                    void*? file = fopen({{literal}}, "rb");
                    if (file == null) return 1;
                    defer { fclose(file!); }
                    for (int row = 0; row < 512; row++)
                    {
                        long[5] header = default(long[5]);
                        char[32] data = default(char[32]);
                        char[41] expected = default(char[41]);
                        if (fread(&header[0], 8, 5, file!) != 5) return 2;
                        if (fread(&data[0], 1, (nuint)header[0], file!) != (nuint)header[0]) return 3;
                        if (fread(&expected[0], 1, (nuint)header[3], file!) != (nuint)header[3]) return 4;
                        MemoryStream source = new();
                        MemoryStream target = new();
                        source.Write(&data[0], (nuint)header[0]);
                        char[5] prefix = [(char)201, (char)202, (char)203, (char)204, (char)205];
                        target.Write(&prefix[0], 5);
                        source.Position = header[1]; target.Position = header[2];
                        nuint capacity = source.Capacity;
                        Send(&source, &target);
                        if (source.Position != header[1] || source.Length != header[0] || source.Capacity != capacity) return 5;
                        if (target.Length != header[3] || target.Position != header[4]) return 6;
                        Span<readonly(char)> output = target.AsReadOnly();
                        for (nuint i = 0; i < (nuint)target.Length; i++) { if (output[i] != expected[i]) return 7; }
                    }
                    return 0;
                }
                """, optimization);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EmptyAndThrowingWritersReceiveOneCallWithoutChangingTheSource(int optimization)
        => Run("""
            using Standard;
            using Standard::IO;
            using Standard::Memory;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            int attempts = 0;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes) { attempts++; return malloc(bytes); }
                public replace void Free(void* storage) nothrow { free(storage); }
            }
            struct Sink : ITextWriter
            {
                public int Calls;
                public nuint Bytes;
                public char First;
                public bool Fail;
                public Sink() { Calls = 0; Bytes = 0; First = '\0'; Fail = false; }
                public void Write(readonly(char)* data, nuint count)
                {
                    Calls++;
                    if (count != 0) { First = data[0]; Bytes++; }
                    if (Fail) throw new IOException("sink failed");
                    if (count != 0) Bytes += count - 1;
                }
            }
            int main()
            {
                MemoryStream source = new();
                source.Position = 50l;
                Sink sink = new();
                int before = attempts;
                source.WriteTo(&sink);
                if (sink.Calls != 1 || sink.Bytes != 0 || source.Position != 50l || source.Length != 0 || attempts != before) return 1;
                source.Position = 0l;
                source.Write("A\0B", 3);
                source.Position = 1l;
                before = attempts;
                source.WriteTo(&sink);
                if (sink.Calls != 2 || sink.Bytes != 3 || sink.First != 'A' || source.Position != 1l || attempts != before) return 2;
                sink.Fail = true;
                bool caught = false;
                try { source.WriteTo(&sink); }
                catch (Exception* error) { caught = true; }
                if (!caught || sink.Calls != 3 || sink.Bytes != 4 || source.Position != 1l || source.Length != 3) return 3;
                Span<readonly(char)> data = source.AsReadOnly();
                if (data[0] != 'A' || data[1] != '\0' || data[2] != 'B') return 4;
                return 0;
            }
            """, optimization);
}
