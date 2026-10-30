namespace gflat.Tests.Std.IO;

public sealed class LimitedReaderTests
{
    private const string NoAllocations = """
        int allocationAttempts = 0;
        namespace Memory {
            public replace void*? Allocate(nuint count) { allocationAttempts++; return null; }
            public replace void Free(void* storage) nothrow {}
        }
        """;

    private const string Source = """
        using Standard::IO;
        struct Source : IByteReader {
            private readonly(char)* data;
            private nuint length;
            private nuint chunk;
            public nuint Position;
            public nuint Calls;
            public nuint ZeroCalls;
            public nuint Maximum;
            public Source(readonly(char)* input, nuint count, nuint block) {
                data = input; length = count; chunk = block;
                Position = 0; Calls = 0; ZeroCalls = 0; Maximum = 0;
            }
            public nuint Read(char* output, nuint count) {
                if (count == 0) { ZeroCalls++; return 0; }
                Calls++; if (count > Maximum) Maximum = count;
                nuint take = length - Position;
                if (take > count) take = count;
                if (take > chunk) take = chunk;
                for (nuint i = 0; i < take; i++) output[i] = data[Position + i];
                Position += take;
                return take;
            }
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void LimitsShortReadsEarlyEofAndZeroRequestsPreserveBufferGuards(int optimization)
        => Run(Source + """
            int main() {
                char[64] input; for (nuint i = 0; i < 64; i++) input[i] = (char)(i * 71 + 23);
                nuint[6] lengths = [0, 1, 2, 7, 17, 64];
                nuint[6] limits = [0, 1, 3, 8, 65, 128];
                nuint[3] chunks = [1, 3, 129];
                nuint[4] requests = [1, 2, 9, (nuint)0 - (nuint)1];
                char* sentinel = ((nuint)1).reinterpret<char*>();
                for (nuint n = 0; n < 6; n++) for (nuint l = 0; l < 6; l++)
                for (nuint c = 0; c < 3; c++) for (nuint r = 0; r < 4; r++) {
                    Source source = new(input, lengths[n], chunks[c]);
                    LimitedReader reader = new(&source, limits[l]);
                    immutable(LimitedReader)* observed = &reader;
                    if (reader.Remaining != limits[l] || source.Calls != 0) return 1;
                    if (reader.Read(sentinel, 0) != 0 || source.Calls != 0 || source.ZeroCalls != 0) return 2;
                    char[132] output; for (nuint i = 0; i < 132; i++) output[i] = '?';
                    nuint received = 0;
                    while (true) {
                        nuint take = reader.Read(&output[1] + received, requests[r]);
                        if (take > requests[r] || take > limits[l] - received) return 3;
                        received += take;
                        if (take == 0) break;
                        nuint calls = source.Calls;
                        if (reader.Read(sentinel, 0) != 0 || source.Calls != calls || source.ZeroCalls != 0) return 4;
                    }
                    nuint expected = lengths[n]; if (expected > limits[l]) expected = limits[l];
                    if (received != expected || source.Position != expected || observed.Remaining != limits[l] - expected) return 5;
                    if (output[0] != '?' || output[expected + 1] != '?') return 6;
                    for (nuint i = 0; i < expected; i++) if (output[i + 1] != input[i]) return 7;
                    nuint block = chunks[c]; if (block > requests[r]) block = requests[r];
                    nuint expectedCalls = expected / block; if (expected % block != 0) expectedCalls++;
                    if (limits[l] > lengths[n]) expectedCalls++;
                    if (source.Calls != expectedCalls || source.ZeroCalls != 0 || source.Maximum > limits[l]) return 8;
                    if (reader.Remaining == 0) {
                        nuint calls = source.Calls;
                        if (reader.Read(sentinel, 1) != 0 || reader.Read(sentinel, 0) != 0 || source.Calls != calls) return 9;
                    }
                }
                return allocationAttempts;
            }
            """ + NoAllocations, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NestedLimitsPreserveBoundaryTailsAndDoNotOwnTheSource(int optimization)
        => Run(Source + """
            int main() {
                Source source = new("0123456789", 10, 99);
                LimitedReader outer = new(&source, 7);
                char[12] output; for (nuint i = 0; i < 12; i++) output[i] = '?';
                {
                    LimitedReader inner = new(&outer, 3);
                    if (inner.Read(&output[1], 10) != 3 || inner.Remaining != 0 || outer.Remaining != 4) return 10;
                    nuint calls = source.Calls;
                    if (inner.Read(((nuint)1).reinterpret<char*>(), 1) != 0 || source.Calls != calls) return 11;
                }
                if (outer.Read(&output[4], 10) != 4 || outer.Remaining != 0 || source.Position != 7) return 12;
                if (source.Read(&output[8], 3) != 3 || source.Position != 10) return 13;
                for (nuint i = 0; i < 10; i++) if (output[i + 1] != "0123456789"[i]) return 14;
                if (output[0] != '?' || output[11] != '?' || allocationAttempts != 0) return 15;
                nuint maximum = (nuint)0 - (nuint)1;
                Source small = new("x", 1, 1); LimitedReader wide = new(&small, maximum);
                char value = '\0';
                if (wide.Read(&value, 1) != 1 || value != 'x' || wide.Remaining != maximum - (nuint)1) return 16;
                if (wide.Read(&value, 1) != 0 || wide.Remaining != maximum - (nuint)1) return 17;
                return 0;
            }
            """ + NoAllocations, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CopiesHaveIndependentBudgetsWhileSharingTheBorrowedCursor(int optimization)
        => Run(Source + """
            int main() {
                Source source = new("abcdef", 6, 99);
                LimitedReader first = new(&source, 3);
                LimitedReader second = first;
                char[6] output;
                if (first.Read(output, 6) != 3 || first.Remaining != 0 || second.Remaining != 3) return 20;
                // Stop using first before handing consumption to the copy.
                if (second.Read(&output[3], 3) != 3 || second.Remaining != 0 || source.Position != 6) return 21;
                for (nuint i = 0; i < 6; i++) if (output[i] != "abcdef"[i]) return 22;
                return allocationAttempts;
            }
            """ + NoAllocations, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SourceExceptionsAndOverReportingKeepTheLastValidatedBudgetAndLatchFailure(int optimization)
        => Run("""
            using Standard::IO;
            class ReadFailure : IOException { public ReadFailure() : base("limited source failure") { code = 81; } }
            struct Faulty : IByteReader {
                public int Mode; public nuint Calls; public nuint Position;
                public Faulty(int mode) { Mode = mode; Calls = 0; Position = 0; }
                public nuint Read(char* data, nuint count) {
                    Calls++;
                    if (Mode == 1) { data[0] = 'H'; Position++; throw new ReadFailure(); }
                    if (Mode == 2) return count + 1;
                    if (Mode == 3) return (nuint)0 - (nuint)1;
                    throw new ReadFailure();
                }
            }
            int main() {
                for (int mode = 1; mode <= 4; mode++) {
                    Faulty source = new(mode);
                    LimitedReader reader = new(&source, 5);
                    char[8] output; for (nuint i = 0; i < 8; i++) output[i] = '?';
                    bool caught = false;
                    try { reader.Read(&output[1], 99); }
                    catch (ref IOException error) {
                        caught = true;
                        if ((mode == 1 || mode == 4) && error.GetCode() != 81) return 30;
                    }
                    if (!caught || source.Calls != 1 || reader.Remaining != 5 || output[0] != '?' || output[6] != '?') return 31;
                    if (mode == 1 && (source.Position != 1 || output[1] != 'H')) return 32;
                    for (int attempt = 0; attempt < 2; attempt++) {
                        caught = false;
                        nuint request = 0; if (attempt != 0) request = 1;
                        try { reader.Read(((nuint)1).reinterpret<char*>(), request); }
                        catch (ref IOException error) { caught = true; }
                        if (!caught || source.Calls != 1 || reader.Remaining != 5) return 33;
                    }
                }
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ReentrantFailureDoesNotClearTheOuterCallsValidationState(int optimization)
        => Run("""
            using Standard::IO;
            struct Source : IByteReader {
                public IByteReader*? Owner;
                public int Calls; public int Rejected;
                public Source() { Owner = null; Calls = 0; Rejected = 0; }
                public nuint Read(char* data, nuint count) {
                    Calls++;
                    try { Owner!.Read(((nuint)1).reinterpret<char*>(), 0); }
                    catch (ref IOException error) { Rejected++; }
                    data[0] = '*'; return 1;
                }
            }
            int main() {
                Source source = new(); LimitedReader reader = new(&source, 5); source.Owner = &reader;
                char value = '\0';
                if (reader.Read(&value, 1) != 1 || reader.Remaining != 4 || value != '*') return 40;
                if (reader.Read(&value, 1) != 1 || reader.Remaining != 3 || source.Calls != 2 || source.Rejected != 2) return 41;
                return 0;
            }
            """, optimization);

    [Fact]
    public void TheAdapterChecksWithoutAutomaticLibraryWhenExceptionMemoryIsProvided()
    {
        string[] files = ["core/Exception.gf", "core/IO/IOException.gf", "core/IO/IByteReader.gf", "core/IO/LimitedReader.gf"];
        var sources = files.Select(file => new SourceFile(file, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard", file))))
            .Append(new SourceFile("minimal-limited-reader.gf", """
                using Standard::IO;
                // Throwing still requires the host's explicit exception-storage allocator.
                namespace Memory {
                    private extern void*? HostAllocate(nuint count);
                    private extern void HostFree(void* storage);
                    public void*? Allocate(nuint count) => HostAllocate(count);
                    public void Free(void* storage) nothrow { HostFree(storage); }
                }
                struct Source : IByteReader { public nuint Read(char* data, nuint count) => 0; }
                int main() { Source source = new(); LimitedReader reader = new(&source, 1); return (int)reader.Remaining; }
                """));
        Compiler.Check(sources, options: new() { NoStdLib = true, NoBuiltins = true });
    }

    // Kept byte-for-byte with the guide's complete program.
    private const string FramingExample = """
        using Standard::Binary;
        using Standard::IO;
        
        // A consumer that reads until its input ends, without knowing the outer framing.
        nuint ReadPayload(IByteReader* input, char* output, nuint capacity)
        {
            nuint total = 0;
            while (total < capacity)
            {
                nuint count = input.Read(output + total, capacity - total);
                if (count > capacity - total) throw new IOException("Invalid payload byte count");
                if (count == 0) break;
                total += count;
            }
            return total;
        }
        
        int main()
        {
            // Little-endian length 3, payload "abc", then a marker for the next consumer.
            byte[6] packet = [3, 0, 97, 98, 99, 126];
            BinaryBufferReader wire = new(packet, 6);
            ushort length = 0;
            if (!wire.TryReadUInt16LittleEndian(ref length)) return 1;
            if (length > 8) return 2;
        
            char[8] payload;
            LimitedReader frame = new(&wire, (nuint)length);
            nuint received = ReadPayload(&frame, payload, 8);
            if (received != (nuint)length || frame.Remaining != 0) return 3;
            if (received != 3 || payload[0] != 'a' || payload[1] != 'b' || payload[2] != 'c') return 4;
        
            // The consumer's final read saw the frame boundary, not the marker.
            char untouched = '!';
            if (frame.Read(&untouched, 1) != 0 || untouched != '!') return 5;
            if (wire.Position != 5 || wire.Remaining != 1) return 6;
        
            byte marker = 0;
            if (!wire.TryReadByte(ref marker) || marker != 126 || wire.Remaining != 0) return 7;
            return 0;
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void LengthPrefixedBinaryBufferFrameRetainsTheFollowingMarker(int optimization)
    {
        // Preserve the whole example body while observing any normal-path allocation.
        string program = FramingExample.Replace("int main()", "int ReadFrameExample()") + NoAllocations + """
            int main() {
                int status = ReadFrameExample();
                if (allocationAttempts != 0) return 99;
                return status;
            }
            """;
        Run(program, optimization);
    }

    private static void Run(string code, int optimization)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Standard"), "*.gf", SearchOption.AllDirectories)
            .Select(file => new SourceFile(file, File.ReadAllText(file))).Append(new SourceFile("limited-reader-tests.gf", code));
        string ir = Compiler.Emit(sources, options: new() { NoStdLib = true, InternalizeFunctions = true });
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }
}
