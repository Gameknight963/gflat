namespace gflat.Tests.Std.IO;

public sealed class BufferedReaderPeekTests
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
            public Source(readonly(char)* input, nuint count, nuint cap) {
                data = input; length = count; chunk = cap; Position = 0; Reads = 0; ZeroReads = 0;
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
    public void RepeatedPeeksPreserveBytesAndCursorAcrossCapacitiesAndShortRefills(int optimization)
    {
        string bytes = string.Join(",", Enumerable.Range(0, 256).Select(value => "(char)" + value));
        string expected = string.Join(",", Enumerable.Range(0, 256));
        Run(Source + $$"""
            int main() {
                char[256] bytes = [{{bytes}}]; int[256] expected = [{{expected}}];
                nuint[6] lengths = [0, 1, 2, 7, 17, 256];
                nuint[5] capacities = [1, 2, 4, 16, 513]; nuint[3] chunks = [1, 3, 257];
                for (nuint n = 0; n < 6; n++) for (nuint c = 0; c < 5; c++) for (nuint b = 0; b < 3; b++) {
                    Source source = new(bytes, lengths[n], chunks[b]);
                    {
                        BufferedReader reader = new(&source, capacities[c]); IByteReader* input = &reader;
                        int baselineAttempts = attempts; int baselineAllocations = allocations; deny = true;
                        nuint position = 0;
                        while (position < lengths[n]) {
                            if (reader.PeekByte() != expected[position] || reader.BufferedCount == 0) return 1;
                            nuint physical = source.Position; nuint pending = reader.BufferedCount; nuint reads = source.Reads;
                            if (reader.PeekByte() != expected[position] || reader.BufferedCount != pending || source.Position != physical || source.Reads != reads) return 2;
                            if (reader.Read(((nuint)1).reinterpret<char*>(), 0) != 0 || reader.BufferedCount != pending || source.Position != physical) return 3;
                            if (reader.PeekByte() != expected[position] || source.Position != physical || source.Reads != reads) return 4;
                            if (position % 3 == 0) {
                                if (input.ReadByte() != expected[position]) return 5;
                                position++;
                            } else {
                                nuint take = lengths[n] - position; if (take > 5) take = 5;
                                char[7] output; for (nuint i = 0; i < 7; i++) output[i] = '?';
                                ReadExactly(input, &output[1], take);
                                if (output[0] != '?' || output[take + 1] != '?') return 6;
                                for (nuint i = 0; i < take; i++) if (output[i + 1] != bytes[position + i]) return 7;
                                position += take;
                            }
                            if (source.Position != position + reader.BufferedCount) return 8;
                        }
                        for (int attempt = 0; attempt < 3; attempt++) {
                            if (reader.PeekByte() != -1 || input.ReadByte() != -1 || reader.BufferedCount != 0 || source.Position != lengths[n]) return 9;
                        }
                        if (attempts != baselineAttempts || allocations != baselineAllocations) return 10;
                        deny = false;
                    }
                }
                if (allocations != releases) return 11;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FailedRefillsAndZeroValidationPreventPeeksFromBypassingLatchedFailure(int optimization)
        => Run("""
            using Standard::IO;
            class InputFailure : IOException { public InputFailure() : base("peek input failed") { code = 81; } }
            struct Source : IByteReader {
                public int Mode; public nuint Position; public nuint Reads; public nuint ZeroReads;
                public Source() { Mode = 0; Position = 0; Reads = 0; ZeroReads = 0; }
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
                    Source source = new(); BufferedReader reader = new(&source, 4);
                    if (mode == 3 || mode == 4) {
                        if (reader.PeekByte() != 0 || reader.BufferedCount == 0) return 20;
                    }
                    source.Mode = mode; bool caught = false;
                    try {
                        if (mode == 3 || mode == 4) reader.Read(((nuint)1).reinterpret<char*>(), 0);
                        else reader.PeekByte();
                    }
                    catch (ref IOException error) { caught = true; if ((mode == 1 || mode == 4) && error.GetCode() != 81) return 21; }
                    if (!caught) return 22;
                    nuint position = source.Position; nuint reads = source.Reads; nuint zeros = source.ZeroReads;
                    char[3] output = ['?', '?', '?']; int rejected = 0;
                    try { reader.PeekByte(); } catch (ref IOException error) { rejected++; }
                    try { reader.ReadByte(); } catch (ref IOException error) { rejected++; }
                    try { reader.Read(&output[1], 1); } catch (ref IOException error) { rejected++; }
                    try { reader.Read(((nuint)1).reinterpret<char*>(), 0); } catch (ref IOException error) { rejected++; }
                    if (rejected != 4 || source.Position != position || source.Reads != reads || source.ZeroReads != zeros) return 23;
                    if (output[0] != '?' || output[1] != '?' || output[2] != '?') return 24;
                }
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MovingAfterPeekRetainsTheUnconsumedSuffixAndTheSingleOwnedBuffer(int optimization)
        => Run(Source + """
            int main() {
                char[6] bytes = ['\0', (char)255, '\n', 'a', 'b', 'c']; Source source = new(bytes, 6, 6);
                {
                    BufferedReader first = new(&source, 4);
                    if (first.PeekByte() != 0 || first.ReadByte() != 0 || first.PeekByte() != 255) return 30;
                    nuint pending = first.BufferedCount; nuint position = source.Position; nuint reads = source.Reads;
                    int baselineAttempts = attempts; int baselineAllocations = allocations; deny = true;
                    BufferedReader second = move first;
                    if (second.PeekByte() != 255 || second.BufferedCount != pending || source.Position != position || source.Reads != reads) return 31;
                    if (second.ReadByte() != 255) return 32;
                    char[4] tail; ReadExactly(&second, tail, 4);
                    if (tail[0] != '\n' || tail[1] != 'a' || tail[2] != 'b' || tail[3] != 'c' || second.PeekByte() != -1) return 33;
                    if (attempts != baselineAttempts || allocations != baselineAllocations) return 34;
                    deny = false;
                }
                if (allocations != releases) return 35;
                return 0;
            }
            """, optimization);

    private static void Run(string program, int optimization)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Standard"), "*.gf", SearchOption.AllDirectories)
            .Select(file => new SourceFile(file, File.ReadAllText(file))).Append(new SourceFile("buffered-reader-peek.gf", program + "\n" + MemoryHooks));
        string ir = Compiler.Emit(sources, options: new() { NoStdLib = true, InternalizeFunctions = true });
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }
}
