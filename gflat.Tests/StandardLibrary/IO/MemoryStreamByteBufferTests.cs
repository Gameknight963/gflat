namespace gflat.Tests.Std.IO;

public sealed class MemoryStreamByteBufferTests
{
    [Fact]
    public void GuideCompiles() => StandardLibraryGuideTests.CheckProgram("MEMORY_STREAM_BYTE_BUFFERS.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ByteBuffersPreserveAllValuesOverlapGapAndAllocationFailureSemantics(int optimization)
    {
        var sources = StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/IO/MemoryStream.gf", "core/IO/IByteReader.gf", "core/IO/IOException.gf",
                "core/IO/SeekOrigin.gf", "core/Collections/vector_resize.gf"))
            .Append(new SourceFile("memory-stream-byte-buffers.gf", """
                using Standard;
                using Standard::IO;
                using Standard::Memory;

                extern "C" void*? malloc(nuint bytes);
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
                int main()
                {
                    byte[256] input;
                    byte[258] output;
                    for (nuint i = 0; i < 256; i++) input[i] = (byte)i;
                    for (nuint length = 0; length <= 256; length++)
                    {
                        MemoryStream stream = new(512);
                        int before = attempts;
                        deny = true;
                        stream.Write(&input[0], length);
                        stream.WriteByte((byte)255);
                        if (stream.Position != (long)length + 1l || stream.Length != (long)length + 1l) return 1;
                        for (nuint i = 0; i < 258; i++) output[i] = (byte)73;
                        stream.Position = 0l;
                        if (stream.Read(&output[1], 257) != length + 1) return 2;
                        for (nuint i = 0; i < length; i++) if (output[i + 1] != (byte)i) return 3;
                        if (output[0] != (byte)73 || output[length + 1] != (byte)255) return 4;
                        for (nuint i = length + 2; i < 258; i++) if (output[i] != (byte)73) return 5;
                        if (stream.Read(&output[0], 1) != 0 || stream.ReadByte() != -1) return 6;
                        stream.Position = 9223372036854775807l;
                        stream.Write(((nuint)1).reinterpret<readonly(byte)*>(), 0);
                        if (stream.Read(((nuint)1).reinterpret<byte*>(), 0) != 0) return 7;
                        if (stream.Read(&output[0], 1) != 0 || stream.Position != 9223372036854775807l) return 8;
                        deny = false;
                        if (attempts != before) return 9;
                    }
                    for (nuint length = 1; length <= 12; length++)
                    for (nuint offset = 0; offset < length; offset++)
                    for (nuint count = 1; count <= length - offset; count++)
                    for (nuint destination = 0; destination <= length + 2; destination++)
                    {
                        MemoryStream stream = new(length);
                        stream.Write(&input[0], length);
                        Span<readonly(char)> view = stream.AsReadOnly();
                        readonly(byte)* source = (view.Data!).reinterpret<readonly(byte)*>();
                        stream.Position = (long)destination;
                        stream.Write(source + offset, count);
                        nuint expectedLength = length;
                        if (destination + count > length) expectedLength = destination + count;
                        if (stream.Length != (long)expectedLength || stream.Position != (long)(destination + count)) return 10;
                        Span<readonly(char)> result = stream.AsReadOnly();
                        for (nuint i = 0; i < expectedLength; i++)
                        {
                            byte expected = (byte)0;
                            if (i < length) expected = (byte)i;
                            if (i >= destination && i - destination < count) expected = (byte)(offset + i - destination);
                            if ((byte)result[i] != expected) return 11;
                        }
                    }
                    {
                        MemoryStream stream = new(4);
                        stream.Write(&input[0], 4);
                        Span<readonly(char)> view = stream.AsReadOnly();
                        readonly(byte)* borrowed = (view.Data!).reinterpret<readonly(byte)*>();
                        nuint capacity = stream.Capacity;
                        int oldLive = live;
                        bool caught = false;
                        deny = true;
                        try { stream.Write(borrowed, 4); }
                        catch (BadAllocException* error) { caught = true; }
                        deny = false;
                        if (!caught || live != oldLive || stream.Length != 4l || stream.Position != 4l || stream.Capacity != capacity) return 12;
                        if (stream.AsReadOnly().Data != view.Data) return 13;
                        for (nuint i = 0; i < 4; i++) if ((byte)view[i] != (byte)i) return 14;
                    }
                    return live;
                }
                """));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Memory byte buffers exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
