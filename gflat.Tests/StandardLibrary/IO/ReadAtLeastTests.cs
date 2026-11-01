namespace gflat.Tests.Std.IO;

public sealed class ReadAtLeastTests
{
    private const string Source = """
        using Standard::IO;

        struct Source : IByteReader
        {
            private readonly(char)* data;
            private nuint length;
            private nuint chunk;
            public nuint Position;
            public nuint Calls;
            public nuint LastRequest;
            public nuint LastTake;
            public int Mode;
            public Source(readonly(char)* input, nuint size, nuint block, int mode = 0)
            {
                data = input; length = size; chunk = block;
                Position = 0; Calls = 0; LastRequest = 0; LastTake = 0; Mode = mode;
            }
            public nuint Read(char* output, nuint count)
            {
                Calls++; LastRequest = count; LastTake = 0;
                if (Mode == 1) throw new IOException("Source state error");
                if (Mode == 2) return count + 1;
                if (count == 0) return 0;
                if (Mode == 3 && Calls == 2)
                {
                    output[0] = 'X'; Position++;
                    throw new IOException("Source consumed a byte before failing");
                }
                if (Mode == 4 && Calls > 2) throw new IOException("Unexpected extra read");
                nuint take = length - Position;
                if (take > count) take = count;
                if (take > chunk) take = chunk;
                if (Mode == 4) { if (Calls == 1) take = 2; else take = 3; }
                for (nuint index = 0; index < take; index++) output[index] = data[Position + index];
                Position += take; LastTake = take;
                return take;
            }
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ShortReadsStopAtMinimumAndMayReturnExtraWithoutAllocating(int optimization)
        => Run(Source + """
            int allocationAttempts = 0;
            namespace Memory
            {
                public replace void*? Allocate(nuint count) { allocationAttempts++; return null; }
                public replace void Free(void* storage) nothrow {}
            }
            int main()
            {
                char[64] bytes;
                for (nuint index = 0; index < 64; index++) bytes[index] = (char)(byte)(index * 73 + 19);
                nuint[6] capacities = [0, 1, 2, 7, 16, 33];
                nuint[4] chunks = [1, 2, 5, 64];
                for (nuint row = 0; row < 6; row++)
                {
                    nuint capacity = capacities[row];
                    for (nuint minimum = 0; minimum <= capacity; minimum++)
                        for (nuint column = 0; column < 4; column++)
                        {
                            char[35] output;
                            for (nuint index = 0; index < 35; index++) output[index] = '?';
                            Source source = new(bytes, 64, chunks[column]);
                            nuint received = ReadAtLeast(&source, &output[1], capacity, minimum);
                            if (received < minimum || received > capacity || source.Position != received) return 1;
                            if (minimum == 0)
                            {
                                if (source.Calls != 1 || source.LastRequest != 0 || received != 0) return 2;
                            }
                            else
                            {
                                if (received - source.LastTake >= minimum) return 3;
                                if (source.LastRequest != capacity - (received - source.LastTake)) return 4;
                            }
                            if (output[0] != '?' || output[capacity + 1] != '?') return 5;
                            for (nuint index = 0; index < received; index++)
                                if (output[index + 1] != bytes[index]) return 6;
                            for (nuint index = received; index < capacity; index++)
                                if (output[index + 1] != '?') return 7;
                        }
                }
                char[16] output;
                Source prompt = new(bytes, 64, 64, 4);
                IByteReader* input = &prompt;
                if (input.ReadAtLeast(output, 16, 5) != 5 || prompt.Calls != 2 || prompt.LastRequest != 14) return 8;
                Source extra = new(bytes, 10, 64);
                if (ReadAtLeast(&extra, output, 16, 5) != 10 || extra.Calls != 1) return 9;
                if (allocationAttempts != 0) return 10;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EofBadCountsAndSourceFailuresPreserveBoundsWithoutRetrying(int optimization)
        => Run(Source + """
            int main()
            {
                char[6] bytes = ['A', '\0', 'C', 'D', 'E', 'F'];
                char[10] output;
                for (int mode = 0; mode < 2; mode++)
                {
                    for (nuint index = 0; index < 10; index++) output[index] = '?';
                    Source source = new(bytes, 3, 2);
                    bool caught = false;
                    nuint received = 99;
                    try { received = ReadAtLeast(&source, &output[1], 8, 4, mode == 0); }
                    catch (ref EndOfStreamException error) { caught = true; }
                    if (caught != (mode == 0) || (mode == 1 && received != 3)) return 1;
                    if (source.Position != 3 || source.Calls != 3) return 2;
                    if (output[0] != '?' || output[1] != 'A' || output[2] != '\0' || output[3] != 'C') return 3;
                    for (nuint index = 4; index < 10; index++) if (output[index] != '?') return 4;
                }
                Source immediate = new(bytes, 0, 2);
                if (ReadAtLeast(&immediate, &output[1], 8, 1, false) != 0 || immediate.Calls != 1) return 5;
                Source empty = new(bytes, 0, 2);
                bool emptyCaught = false;
                try { ReadAtLeast(&empty, &output[1], 8, 1); }
                catch (ref EndOfStreamException error) { emptyCaught = true; }
                if (!emptyCaught || empty.Calls != 1 || empty.Position != 0) return 13;
                Source invalid = new(bytes, 6, 2, 1);
                for (nuint index = 0; index < 10; index++) output[index] = '?';
                bool caught = false;
                try { ReadAtLeast(&invalid, &output[1], 8, 9); }
                catch (ref IOException error) { caught = true; }
                if (!caught || invalid.Calls != 0) return 6;
                for (nuint index = 0; index < 10; index++) if (output[index] != '?') return 7;
                caught = false;
                try { ReadAtLeast(&invalid, &output[1], 8, 0); }
                catch (ref IOException error) { caught = true; }
                if (!caught || invalid.Calls != 1) return 8;
                for (nuint minimum = 0; minimum <= 1; minimum++)
                {
                    Source badCount = new(bytes, 6, 2, 2);
                    caught = false;
                    try { ReadAtLeast(&badCount, &output[1], 8, minimum); }
                    catch (ref IOException error) { caught = true; }
                    if (!caught || badCount.Calls != 1 || badCount.Position != 0) return 9;
                }
                Source partial = new(bytes, 6, 2, 3);
                caught = false;
                try { ReadAtLeast(&partial, &output[1], 8, 4); }
                catch (ref IOException error) { caught = true; }
                if (!caught || partial.Calls != 2 || partial.Position != 3) return 10;
                if (output[0] != '?' || output[1] != 'A' || output[2] != '\0' || output[3] != 'X') return 11;
                for (nuint index = 4; index < 10; index++) if (output[index] != '?') return 12;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BorrowingRetainsNoncopyableReaderAndCallerBufferOwnership(int optimization)
        => Run("""
            using Standard::Collections;
            using Standard::IO;

            extern void*? malloc(nuint count);
            extern void free(void* storage);
            int allocationAttempts = 0;
            int liveAllocations = 0;
            int destroyedReaders = 0;
            bool denyAllocation = false;
            namespace Memory
            {
                public replace void*? Allocate(nuint count)
                {
                    allocationAttempts++;
                    if (denyAllocation) return null;
                    void*? result = malloc(count);
                    if (result != null) liveAllocations++;
                    return result;
                }
                public replace void Free(void* storage) nothrow { liveAllocations--; free(storage); }
            }
            struct OwnedReader : IByteReader
            {
                private Vector<char> bytes;
                public nuint Position;
                public bool FailNext;
                public OwnedReader()
                {
                    bytes = new(); Position = 0; FailNext = false;
                    for (int index = 0; index < 8; index++) bytes.Add((char)(65 + index));
                }
                public nuint Read(char* output, nuint count)
                {
                    if (count == 0) return 0;
                    if (FailNext)
                    {
                        FailNext = false; output[0] = 'X'; Position++;
                        throw new IOException("Owned source failure");
                    }
                    nuint take = bytes.Count - Position;
                    if (take > count) take = count;
                    if (take > 2) take = 2;
                    for (nuint index = 0; index < take; index++) output[index] = bytes[Position + index];
                    Position += take;
                    return take;
                }
                ~OwnedReader() { destroyedReaders++; }
            }
            int main()
            {
                {
                    OwnedReader source = new();
                    Vector<char> destination = new();
                    for (int index = 0; index < 8; index++) destination.Add('?');
                    char* output = destination.AsSpan().Data!;
                    int attempts = allocationAttempts;
                    int live = liveAllocations;
                    denyAllocation = true;
                    IByteReader* input = &source;
                    nuint received = input.ReadAtLeast(output, 8, 3);
                    denyAllocation = false;
                    if (received != 4 || source.Position != 4 || allocationAttempts != attempts) return 1;
                    if (destroyedReaders != 0 || liveAllocations != live || destination.Count != 8) return 2;
                    if (destination[0] != 'A' || destination[3] != 'D' || destination[4] != '?') return 3;
                    source.FailNext = true;
                    bool caught = false;
                    try { input.ReadAtLeast(output, 8, 1); }
                    catch (ref IOException error) { caught = true; }
                    if (!caught || source.Position != 5 || destroyedReaders != 0 || liveAllocations != live) return 4;
                    if (destination.Count != 8 || destination[0] != 'X') return 5;
                    char following = '\0';
                    if (source.Read(&following, 1) != 1 || following != 'F' || source.Position != 6) return 6;
                }
                if (destroyedReaders != 1 || liveAllocations != 0) return 7;
                return 0;
            }
            """, optimization);

    private static IEnumerable<SourceFile> Sources(string code)
        => new[] { "core/IO/IByteReader.gf", "core/IO/IOException.gf", "core/IO/EndOfStreamException.gf",
            "core/IO/byte_reader_read_at_least.gf", "core/Collections/Vector.gf", "core/Collections/IEnumerable.gf",
            "core/Memory/Span.gf", "core/Queries/Enumerable.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard", name))))
            .Append(new SourceFile("read-at-least.gf", code));

    private static void Run(string code, int optimization)
    {
        var result = CompilerTestHelper.Run(Sources(code), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }
}
