using gflat.CompileExceptions;

namespace gflat.Tests.Std.IO;

public sealed class BufferedReaderBorrowedTests
{
    private const string MemoryHooks = """
        int attempts = 0; int allocations = 0; int releases = 0; bool deny = false;
        namespace Memory {
            namespace Native { private extern void*? malloc(nuint count); private extern void free(void* data); }
            public replace void*? Allocate(nuint count) {
                attempts++; if (deny) return null;
                void*? result = Native::malloc(count); if (result != null) allocations++; return result;
            }
            public replace void Free(void* data) nothrow { releases++; Native::free(data); }
        }
        """;

    private const string Source = """
        using Standard::IO;
        struct Source : IByteReader {
            private readonly(char)* data; private nuint length; private nuint chunk;
            public nuint Position; public nuint Reads; public nuint ZeroReads;
            public Source(readonly(char)* input, nuint count, nuint block) {
                data = input; length = count; chunk = block; Position = 0; Reads = 0; ZeroReads = 0;
            }
            public nuint Read(char* output, nuint count) {
                if (count == 0) { ZeroReads++; return 0; }
                Reads++; nuint take = length - Position;
                if (take > count) take = count; if (take > chunk) take = chunk;
                for (nuint i = 0; i < take; i++) output[i] = data[Position + i];
                Position += take; return take;
            }
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CallerScratchReadsAndPeeksPreserveGuardsWithoutAllocatorCalls(int optimization)
    {
        byte[] expected = Enumerable.Range(0, 257).Select(i => (byte)(i * 73 + 19)).ToArray();
        Run(Source + $$"""
            int main() {
                char[257] input = [{{string.Join(",", expected.Select(value => "(char)" + value))}}];
                int[257] expected = [{{string.Join(",", expected)}}];
                nuint[6] lengths = [0, 1, 2, 7, 17, 257];
                nuint[5] capacities = [1, 2, 4, 16, 257]; nuint[3] chunks = [1, 3, 259];
                for (nuint n = 0; n < 6; n++) for (nuint c = 0; c < 5; c++) for (nuint b = 0; b < 3; b++) {
                    Source source = new(input, lengths[n], chunks[b]);
                    char[259] scratch; for (nuint i = 0; i < 259; i++) scratch[i] = '?';
                    int baselineAttempts = attempts; int baselineReleases = releases; deny = true;
                    {
                        BufferedReader reader = new(&source, &scratch[1], capacities[c]);
                        if (reader.Capacity != capacities[c] || reader.BufferedCount != 0 || source.Position != 0) return 1;
                        for (nuint i = 0; i < 259; i++) if (scratch[i] != '?') return 2;
                        nuint position = 0;
                        while (position < lengths[n]) {
                            if (reader.PeekByte() != expected[position]) return 3;
                            nuint physical = source.Position; nuint pending = reader.BufferedCount; nuint reads = source.Reads;
                            if (reader.PeekByte() != expected[position] || source.Position != physical || source.Reads != reads || reader.BufferedCount != pending) return 4;
                            if (reader.Read(((nuint)1).reinterpret<char*>(), 0) != 0 || source.Position != physical || reader.BufferedCount != pending) return 5;
                            if (position % 3 == 0) {
                                if (reader.ReadByte() != expected[position]) return 6;
                                position++;
                            } else {
                                nuint count = lengths[n] - position; if (count > 5) count = 5;
                                char[7] output; for (nuint i = 0; i < 7; i++) output[i] = '!';
                                ReadExactly(&reader, &output[1], count);
                                if (output[0] != '!' || output[count + 1] != '!') return 7;
                                for (nuint i = 0; i < count; i++) if (output[i + 1] != input[position + i]) return 8;
                                position += count;
                            }
                            if (source.Position != position + reader.BufferedCount || scratch[0] != '?' || scratch[capacities[c] + 1] != '?') return 9;
                        }
                        for (int attempt = 0; attempt < 2; attempt++) if (reader.PeekByte() != -1 || reader.ReadByte() != -1) return 10;
                        if (source.Position != lengths[n] || reader.BufferedCount != 0) return 11;
                    }
                    if (attempts != baselineAttempts || releases != baselineReleases || scratch[0] != '?' || scratch[capacities[c] + 1] != '?') return 12;
                    deny = false;
                }
                if (allocations != 0 || releases != 0) return 13;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BorrowedMovesRetainThePrefetchedSuffixAndNeverFreeCallerStorage(int optimization)
        => Run(Source + """
            int main() {
                char[6] data = ['\0', (char)255, '\n', 'a', 'b', 'c']; Source source = new(data, 6, 6);
                char[6] scratch = ['L', '?', '?', '?', '?', 'R'];
                int baselineAttempts = attempts; int baselineReleases = releases; deny = true;
                {
                    BufferedReader first = new(&source, &scratch[1], 4);
                    if (first.PeekByte() != 0 || first.ReadByte() != 0 || first.PeekByte() != 255) return 20;
                    nuint pending = first.BufferedCount; nuint physical = source.Position; nuint reads = source.Reads;
                    BufferedReader second = move first;
                    if (second.PeekByte() != 255 || second.BufferedCount != pending || source.Position != physical || source.Reads != reads) return 21;
                    if (second.ReadByte() != 255) return 22;
                    char[4] tail; ReadExactly(&second, tail, 4);
                    if (tail[0] != '\n' || tail[1] != 'a' || tail[2] != 'b' || tail[3] != 'c' || second.PeekByte() != -1) return 23;
                }
                deny = false;
                if (attempts != baselineAttempts || releases != baselineReleases || scratch[0] != 'L' || scratch[5] != 'R') return 24;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BorrowedFailuresPreserveScratchGuardsAndKeepTheExistingPoisonRules(int optimization)
        => Run("""
            using Standard::IO;
            class InputFailure : IOException { public InputFailure() : base("borrowed input failed") { code = 81; } }
            struct Source : IByteReader {
                public int Mode; public nuint Reads; public nuint ZeroReads; public nuint Position;
                public Source() { Mode = 0; Reads = 0; ZeroReads = 0; Position = 0; }
                public nuint Read(char* data, nuint count) {
                    if (count == 0) { ZeroReads++; if (Mode == 3) return 1; if (Mode == 4) throw new InputFailure(); return 0; }
                    Reads++;
                    if (Mode == 1) { data[0] = (char)255; Position++; throw new InputFailure(); }
                    if (Mode == 2) { data[0] = '\n'; return count + 1; }
                    if (Mode == 5) { data[0] = (char)255; return (nuint)0 - (nuint)1; }
                    for (nuint i = 0; i < count; i++) data[i] = (char)(Position + i);
                    Position += count; return count;
                }
            }
            int main() {
                for (int mode = 1; mode <= 5; mode++) {
                    Source source = new(); char[6] scratch = ['L', '?', '?', '?', '?', 'R'];
                    int before = attempts; BufferedReader reader = new(&source, &scratch[1], 4);
                    if (attempts != before) return 30;
                    if (mode == 3 || mode == 4) { if (reader.PeekByte() != 0 || reader.BufferedCount == 0) return 31; }
                    source.Mode = mode; bool caught = false;
                    try {
                        if (mode == 3 || mode == 4) reader.Read(((nuint)1).reinterpret<char*>(), 0);
                        else reader.PeekByte();
                    }
                    catch (ref IOException error) { caught = true; if ((mode == 1 || mode == 4) && error.GetCode() != 81) return 32; }
                    if (!caught || scratch[0] != 'L' || scratch[5] != 'R') return 33;
                    nuint reads = source.Reads; nuint zeros = source.ZeroReads; nuint position = source.Position;
                    char value = '!'; int rejected = 0;
                    try { reader.PeekByte(); } catch (ref IOException error) { rejected++; }
                    try { reader.ReadByte(); } catch (ref IOException error) { rejected++; }
                    try { reader.Read(&value, 1); } catch (ref IOException error) { rejected++; }
                    try { reader.Read(((nuint)1).reinterpret<char*>(), 0); } catch (ref IOException error) { rejected++; }
                    if (rejected != 4 || value != '!' || source.Reads != reads || source.ZeroReads != zeros || source.Position != position) return 34;
                    if (scratch[0] != 'L' || scratch[5] != 'R') return 35;
                }
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ZeroBorrowedCapacityRejectsWithoutTouchingScratchOrReadingSource(int optimization)
        => Run(Source + """
            int main() {
                char input = '*'; Source source = new(&input, 1, 1); char guard = 'Q';
                bool caught = false;
                try { BufferedReader invalid = new(&source, &guard, 0); }
                catch (ref IOException error) { caught = true; }
                if (!caught || guard != 'Q' || source.Position != 0 || source.Reads != 0 || source.ZeroReads != 0) return 40;
                caught = false;
                try { BufferedReader sentinel = new(&source, ((nuint)1).reinterpret<char*>(), 0); }
                catch (ref IOException error) { caught = true; }
                if (!caught || source.Position != 0 || source.Reads != 0 || source.ZeroReads != 0) return 41;
                return 0;
            }
            """, optimization);

    [Fact]
    public void BorrowingTheBufferDoesNotPermitCopiesOfOnePrefetchedCursor()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Sources(Source + """
            void Probe() {
                char input = '*'; Source source = new(&input, 1, 1); char scratch = '\0';
                BufferedReader first = new(&source, &scratch, 1); BufferedReader second = first;
            }
            """), options: new() { NoStdLib = true }));
        Assert.Contains("copy", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<SourceFile> Sources(string program)
        => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Standard"), "*.gf", SearchOption.AllDirectories)
            .Select(file => new SourceFile(file, File.ReadAllText(file))).Append(new SourceFile("borrowed-buffered-reader.gf", program + "\n" + MemoryHooks));

    private static void Run(string program, int optimization)
    {
        string ir = Compiler.Emit(Sources(program), options: new() { NoStdLib = true, InternalizeFunctions = true });
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }
}
