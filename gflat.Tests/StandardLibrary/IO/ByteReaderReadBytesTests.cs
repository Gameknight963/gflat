namespace gflat.Tests.Std.IO;

public sealed class ByteReaderReadBytesTests
{
    [Fact]
    public void GuideCompiles() => StandardLibraryGuideTests.CheckProgram("BYTE_READER_READ_BYTES.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BoundedShortReadsEofErrorsAndAllocationFailuresKeepOwnershipBalanced(int optimization)
    {
        var sources = StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/IO/IByteReader.gf", "core/IO/IOException.gf",
                "core/IO/byte_reader_read_bytes.gf"))
            .Append(new SourceFile("read-bytes.gf", """
                using Standard;
                using Standard::Collections;
                using Standard::IO;

                extern "C" void*? malloc(nuint bytes);
                extern "C" void free(void* storage);
                int live = 0;
                int attempts = 0;
                int failAfter = -1;
                namespace Memory
                {
                    public replace void*? Allocate(nuint bytes)
                    {
                        attempts++;
                        if (failAfter == 0) { failAfter = -1; return null; }
                        if (failAfter > 0) failAfter--;
                        void*? result = malloc(bytes);
                        if (result != null) live++;
                        return result;
                    }
                    public replace void Free(void* storage) nothrow { live--; free(storage); }
                }
                struct Reader : IByteReader
                {
                    public nuint Position;
                    public nuint Length;
                    public nuint Chunk;
                    public int Mode;
                    public int Calls;
                    public Reader(nuint length, nuint chunk, int mode = 0)
                    { Position = 0; Length = length; Chunk = chunk; Mode = mode; Calls = 0; }
                    public nuint Read(char* output, nuint count)
                    {
                        Calls++;
                        if (Mode == 1 || (Mode == 3 && Position >= Chunk)) throw new IOException("input failed");
                        if (Mode == 2) return count + 1;
                        nuint received = Length - Position;
                        if (received > count) received = count;
                        if (received > Chunk) received = Chunk;
                        for (nuint i = 0; i < received; i++) output[i] = (char)(byte)(Position + i);
                        Position += received;
                        return received;
                    }
                }
                int main()
                {
                    nuint[8] lengths = [0, 1, 7, 255, 4095, 4096, 4097, 8193];
                    nuint[9] requests = [0, 1, 7, 255, 4095, 4096, 4097, 8193, (nuint)0 - (nuint)1];
                    nuint[5] chunks = [1, 3, 255, 4096, 10000];
                    for (nuint a = 0; a < 8; a++)
                    for (nuint b = 0; b < 9; b++)
                    for (nuint c = 0; c < 5; c++)
                    {
                        {
                            Reader input = new(lengths[a], chunks[c]);
                            IByteReader* reader = &input;
                            Vector<byte> bytes = reader.ReadBytes(requests[b]);
                            nuint expected = lengths[a];
                            if (expected > requests[b]) expected = requests[b];
                            if (bytes.Count != expected || input.Position != expected) return 1;
                            nuint chunk = chunks[c];
                            if (chunk > 4096) chunk = 4096;
                            nuint calls = expected / chunk;
                            if (expected % chunk != 0) calls++;
                            if (requests[b] == 0 || requests[b] > lengths[a]) calls++;
                            if (input.Calls != (int)calls) return 9;

                            for (nuint i = 0; i < expected; i++) if (bytes[i] != (byte)i) return 2;
                            if (requests[b] == 0 && (input.Calls != 1 || bytes.Capacity != 0)) return 3;
                            Vector<byte> rest = Standard::IO::ReadBytes(reader, 2);
                            nuint remaining = lengths[a] - expected;
                            if (remaining > 2) remaining = 2;
                            if (rest.Count != remaining || input.Position != expected + remaining) return 4;
                        }
                        if (live != 0) return 5;
                    }
                    for (int mode = 1; mode <= 3; mode++)
                    {
                        bool caught = false;
                        Reader input = new(8193, 3, mode);
                        try { Vector<byte> bytes = ReadBytes(&input, 8193); }
                        catch (Exception* error) { caught = true; }
                        if (!caught || live != 0) return 6;
                    }
                    for (int mode = 1; mode <= 2; mode++)
                    {
                        bool caught = false;
                        Reader input = new(1, 1, mode);
                        try { Vector<byte> bytes = ReadBytes(&input, 0); }
                        catch (Exception* error) { caught = true; }
                        if (!caught || input.Position != 0 || live != 0) return 7;
                    }
                    int allocations = 0;
                    {
                        Reader input = new(8193, 3);
                        int before = attempts;
                        Vector<byte> bytes = ReadBytes(&input, 8193);
                        allocations = attempts - before;
                    }
                    for (int failure = 0; failure < allocations; failure++)
                    {
                        Reader input = new(8193, 3);
                        failAfter = failure;
                        bool caught = false;
                        try { Vector<byte> bytes = ReadBytes(&input, 8193); }
                        catch (Exception* error) { caught = true; }
                        failAfter = -1;
                        if (!caught || live != 0) return 8;
                    }
                    return live;
                }
                """));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }
}
