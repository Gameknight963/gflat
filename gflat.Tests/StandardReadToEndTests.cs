namespace gflat.Tests;

public sealed class StandardReadToEndTests
{
    private const string PatternReader = """
        using Standard; using Standard::IO; using Standard::Text;
        char Pattern(nuint offset) nothrow => (char)(offset * 73 + 19);
        struct Source : IByteReader {
            public nuint Position; public nuint Length; public nuint Calls;
            public nuint Maximum; public nuint ZeroCalls;
            private nuint chunk;
            public Source(nuint length, nuint block = 4096) {
                Position = 0; Length = length; Calls = 0; Maximum = 0; ZeroCalls = 0; chunk = block;
            }
            public nuint Read(char* output, nuint count) {
                Calls++; if (count > Maximum) Maximum = count; if (count == 0) ZeroCalls++;
                nuint take = Length - Position; if (take > count) take = count; if (take > chunk) take = chunk;
                for (nuint i = 0; i < take; i++) output[i] = Pattern(Position + i);
                Position += take; return take;
            }
        }
        bool Matches(ref immutable(String) text, nuint start, nuint count) {
            if (text.Length != count || text.Data[count] != '\0') return false;
            for (nuint i = 0; i < count; i++) if (text.Data[i] != Pattern(start + i)) return false;
            return true;
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ShortReadsAndChunkBoundariesPreserveCountedBytesAcrossCallForms(int optimization)
        => Run(PatternReader + """
            int main() {
                nuint[9] lengths = [0, 1, 255, 256, 4095, 4096, 4097, 8192, 8193];
                nuint[3] chunks = [1, 7, 4096];
                for (nuint n = 0; n < 9; n++) for (nuint c = 0; c < 3; c++) {
                    nuint length = lengths[n]; Source direct = new(length, chunks[c]);
                    String first = ReadToEnd(&direct);
                    if (!Matches(ref first, 0, length) || direct.Position != length) return 1;
                    Source borrowed = new(length, chunks[c]); IByteReader* input = &borrowed;
                    String second = input.ReadToEnd(length);
                    if (!Matches(ref second, 0, length) || borrowed.Position != length) return 2;
                    Source concrete = new(length, chunks[c]); Source* pointer = &concrete;
                    String third = pointer.ReadToEnd();
                    if (!Matches(ref third, 0, length) || concrete.Position != length) return 3;
                    if (direct.Maximum > 4096 || borrowed.Maximum > 4096 || concrete.Maximum > 4096) return 4;
                    if (direct.ZeroCalls != 0 || borrowed.ZeroCalls != 0 || concrete.ZeroCalls != 0) return 5;
                    if (length != 0) {
                        first[0] = '!';
                        if (second[0] != Pattern(0) || third[0] != Pattern(0)) return 6;
                    }
                }
                Source advanced = new(19, 3);
                if (advanced.ReadByte() != (int)(byte)Pattern(0)) return 7;
                String tail = advanced.ReadToEnd();
                if (!Matches(ref tail, 1, 18) || advanced.Position != 19) return 8;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ReusableDestinationRetainsCapacityAndRejectsOnlyOverLimitInput(int optimization)
        => Run(PatternReader + """
            int main() {
                String text = s"old"; text.Reserve(10000);
                readonly(char)* storage = text.Data; nuint capacity = text.Capacity;
                nuint[7] lengths = [0, 1, 4095, 4096, 4097, 8192, 8193];
                nuint[3] chunks = [1, 13, 4096];
                for (nuint n = 0; n < 7; n++) for (nuint c = 0; c < 3; c++) {
                    nuint length = lengths[n]; nuint belowLength = 0; if (length != 0) belowLength = length - 1;
                    nuint[5] limits = [0, belowLength, length, length + 1, (nuint)0 - (nuint)1];
                    for (nuint l = 0; l < 5; l++) {
                        nuint limit = limits[l]; Source source = new(length, chunks[c]); bool caught = false;
                        text.Append('!');
                        try { source.ReadToEnd(ref text, limit); }
                        catch (ref IOException error) {
                            caught = true;
                            if (!CString::Equals(error.GetMessage(), "Input exceeds maximum byte count")) return 10;
                        }
                        if (caught != (length > limit)) return 11;
                        if (text.Data != storage || text.Capacity != capacity) return 12;
                        if (!caught && !Matches(ref text, 0, length)) return 13;
                        if (caught) {
                            if (source.Position != limit + 1 || text.Length > limit) return 14;
                            if (!Matches(ref text, 0, text.Length)) return 15;
                            if (source.Position < length && source.ReadByte() != (int)(byte)Pattern(limit + 1)) return 16;
                        }
                        if (source.ZeroCalls != 0 || source.Maximum > 4096) return 17;
                    }
                }
                for (nuint c = 0; c < 3; c++) {
                    Source source = new(9000, chunks[c]); bool caught = false;
                    try { source.ReadToEnd(ref text, 5000); }
                    catch (ref IOException error) {
                        caught = CString::Equals(error.GetMessage(), "Input exceeds maximum byte count");
                    }
                    nuint committed = (5000 / chunks[c]) * chunks[c];
                    if (!caught || source.Position != 5001 || !Matches(ref text, 0, committed)) return 19;
                    if (text.Data != storage || text.Capacity != capacity) return 20;
                    if (source.ReadByte() != (int)(byte)Pattern(5001)) return 21;
                }
                {
                    Source source = new(20); char[8] scratch;
                    BufferedReader input = new(&source, scratch, 8); bool caught = false;
                    try { input.ReadToEnd(ref text, 2); }
                    catch (ref IOException error) {
                        caught = CString::Equals(error.GetMessage(), "Input exceeds maximum byte count");
                    }
                    if (!caught || text.Length != 0 || source.Position != 8 || input.BufferedCount != 5) return 22;
                    if (input.ReadByte() != (int)(byte)Pattern(3) || source.Position != 8 || source.Calls != 1) return 23;
                }
                Source empty = new(0);
                ReadToEnd(&empty, ref text);
                if (text.Length != 0 || text.Data != storage || text.Data[0] != '\0' || empty.Calls != 1) return 18;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ReaderFailuresAndOverReportsPreserveEarlierChunksWithoutFurtherReads(int optimization)
        => Run("""
            using Standard::IO; using Standard::Text;
            class InputFailure : IOException { public InputFailure() : base("source failed") { code = 73; } }
            struct Source : IByteReader {
                public nuint Position; public nuint Calls; private int mode;
                public Source(int kind) { Position = 0; Calls = 0; mode = kind; }
                public nuint Read(char* output, nuint count) {
                    Calls++;
                    if (Calls == 1 && mode != 3) {
                        for (nuint i = 0; i < count; i++) output[i] = 'a';
                        Position += count; return count;
                    }
                    output[0] = '!'; Position++;
                    if (mode == 0 || mode == 3) throw new InputFailure();
                    if (mode == 1) return count + 1;
                    return (nuint)0 - (nuint)1;
                }
            }
            int main() {
                for (int mode = 0; mode < 4; mode++) {
                    Source source = new(mode); String text = s"old"; text.Reserve(8192); bool caught = false;
                    try { ReadToEnd(&source, ref text); }
                    catch (ref InputFailure error) { if (mode != 0 && mode != 3) return 20; caught = error.GetCode() == 73; }
                    catch (ref IOException error) {
                        if (mode == 0 || mode == 3) return 21;
                        caught = CString::Equals(error.GetMessage(), "Byte reader returned more than requested");
                    }
                    nuint committed = 4096; if (mode == 3) committed = 0;
                    nuint expectedCalls = 2; if (mode == 3) expectedCalls = 1;
                    if (!caught || source.Position != committed + 1 || source.Calls != expectedCalls) return 22;
                    if (text.Length != committed || text.Data[text.Length] != '\0') return 23;
                    for (nuint i = 0; i < text.Length; i++) if (text.Data[i] != 'a') return 24;
                }
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AllocationFailureStopsAtCurrentChunkAndReleasesPartialOwningResult(int optimization)
        => Run(PatternReader + """
            extern void*? malloc(nuint size); extern void free(void* value);
            int allocated = 0; int released = 0; int denied = 0; int failAfter = -1;
            namespace Memory {
                public replace void*? Allocate(nuint count) {
                    if (failAfter == 0) { denied++; return null; }
                    if (failAfter > 0) failAfter--;
                    void*? result = malloc(count); if (result != null) allocated++; return result;
                }
                public replace void Free(void* storage) nothrow { released++; free(storage); }
            }
            int main() {
                for (int point = 0; point < 3; point++) {
                    Source source = new(9000); int live = allocated - released; bool caught = false;
                    int rejected = denied; failAfter = point;
                    try { String text = ReadToEnd(&source); }
                    catch (ref BadAllocException error) { caught = true; }
                    failAfter = -1;
                    if (!caught || denied == rejected || allocated - released != live) return 30;
                    nuint expected = (nuint)(point + 1) * 4096; if (expected > 9000) expected = 9000;
                    if (source.Position != expected || source.Calls != (nuint)(point + 1)) return 31;
                }
                {
                    String text = s"old"; text.Reserve(4096); Source source = new(9000); bool caught = false;
                    readonly(char)* storage = text.Data; failAfter = 0;
                    try { source.ReadToEnd(ref text); }
                    catch (ref BadAllocException error) { caught = true; }
                    failAfter = -1;
                    if (!caught || source.Position != 8192 || source.Calls != 2) return 32;
                    if (text.Data != storage || !Matches(ref text, 0, 4096)) return 33;
                }
                if (allocated != released) return 34;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BorrowedFileStaysOpenAndAByteLimitDoesNotDrainItsTail(int optimization)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-read-to-end-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        bool passed = false;
        try
        {
            string path = Path.Combine(directory, "input.bin");
            File.WriteAllBytes(path, [65, 0, 255, 13, 10, 66]);
            string literal = "\"" + path.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            Run($$"""
                using Standard::IO; using Standard::Text;
                int main() {
                    File file = File::OpenRead({{literal}}); IByteReader* input = &file;
                    String text = input.ReadToEnd(6);
                    if (!file.IsOpen || text.Length != 6 || text[1] != '\0' || (byte)text[2] != (byte)255) return 40;
                    if (text[3] != '\r' || text[4] != '\n' || input.ReadByte() != -1) return 41;
                    file.Close(); file = File::OpenRead({{literal}}); bool caught = false;
                    try { input.ReadToEnd(ref text, 2); }
                    catch (ref IOException error) { caught = true; }
                    if (!caught || !file.IsOpen || input.ReadByte() != 13) return 42;
                    file.Close(); caught = false;
                    try { input.ReadToEnd(ref text, 0); }
                    catch (ref IOException error) { caught = true; }
                    if (!caught || file.IsOpen || text.Length != 0) return 43;
                    return 0;
                }
                """, optimization);
            passed = true;
        }
        catch (Exception error) { error.Data["ReadToEndDirectory"] = directory; throw; }
        finally { if (passed) Directory.Delete(directory, recursive: true); }
    }

    private static void Run(string code, int optimization)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Standard"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("read-to-end-tests.gf", code));
        string ir = Compiler.Emit(sources, options: new() { NoStdLib = true, InternalizeFunctions = true });
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }
}
