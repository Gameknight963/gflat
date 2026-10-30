using gflat.CompileExceptions;

namespace gflat.Tests.Std.IO;

public sealed class LineSequenceTests
{
    private const string Support = """
        using Standard::IO;
        using Standard::Text;
        using Standard::Queries;
        using Standard::Collections;
        using Standard::Binary;
        int allocations = 0;
        int releases = 0;
        namespace Memory
        {
            namespace Native
            {
                private extern void*? malloc(nuint bytes);
                private extern void free(void* storage);
            }
            public replace void*? Allocate(nuint bytes)
            {
                void*? storage = Native::malloc(bytes);
                if (storage != null) allocations++;
                return storage;
            }
            public replace void Free(void* storage) nothrow
            {
                releases++;
                Native::free(storage);
            }
        }
        struct Source : IByteReader
        {
            private readonly(char)* data;
            private nuint length;
            public nuint Position;
            public nuint Calls;
            public nuint Maximum;
            public bool FailNext;
            public bool OverreportNext;
            public Source(readonly(char)* bytes, nuint count)
            {
                data = bytes; length = count; Position = 0; Calls = 0; Maximum = 0;
                FailNext = false; OverreportNext = false;
            }
            public nuint Read(char* output, nuint count)
            {
                Calls++;
                if (count > Maximum) Maximum = count;
                if (FailNext)
                {
                    FailNext = false;
                    if (Position < length) Position++;
                    throw new Exception("Source failure after consumption", 73);
                }
                if (OverreportNext) { OverreportNext = false; return count + 1; }
                nuint take = length - Position;
                if (take > count) take = count;
                for (nuint index = 0; index < take; index++) output[index] = data[Position + index];
                Position += take;
                return take;
            }
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DeferredCountedLinesPreserveBoundariesAndStickyEof(int optimization)
        => Run("""
            int main()
            {
                {
                    char[16] bytes = ['\r','\n','A','\0',(char)(byte)255,'\r','X','\r','\n','b','\n','l','a','s','t','\r'];
                    Source source = new(bytes, 16);
                    IByteReader* input = &source;
                    LineSequence lines = input.EnumerateLines();
                    var iterator = lines.GetEnumerator();
                    if (source.Calls != 0 || allocations != 0) return 1;
                    bool caught = false;
                    try { iterator.Current; } catch (ref IOException error) { caught = true; }
                    if (!caught || source.Calls != 0) return 2;
                    if (!iterator.MoveNext() || iterator.Current.Length != 0 || source.Position != 2) return 3;
                    if (!iterator.MoveNext() || iterator.Current.Length != 5 || source.Position != 9) return 4;
                    if (iterator.Current[0] != 'A' || iterator.Current[1] != '\0'
                        || (byte)iterator.Current[2] != (byte)255 || iterator.Current[3] != '\r'
                        || iterator.Current[4] != 'X') return 5;
                    if (!iterator.MoveNext() || iterator.Current.Length != 1 || iterator.Current[0] != 'b'
                        || source.Position != 11) return 6;
                    if (!iterator.MoveNext() || iterator.Current.Length != 5 || iterator.Current[4] != '\r') return 7;
                    if (iterator.MoveNext()) return 8;
                    nuint ended = source.Calls;
                    if (iterator.MoveNext() || source.Calls != ended || source.Maximum != 1) return 9;
                    caught = false;
                    try { iterator.Current; } catch (ref IOException error) { caught = true; }
                    if (!caught || source.Calls != ended) return 10;
                    Source empty = new("", 0);
                    var end = EnumerateLines(&empty).GetEnumerator();
                    int before = allocations;
                    if (end.MoveNext() || end.MoveNext() || empty.Calls != 1 || allocations != before) return 11;
                }
                return allocations - releases;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ReusesLongestLineAndCopiesRetainIndependentContentsAfterMove(int optimization)
    {
        string text = new string('a', 1300) + "\\n\\nb\\n" + new string('c', 513) + "\\n";
        Run($$"""
            int main()
            {
                {
                    Source source = new("{{text}}", 1818);
                    var iterator = EnumerateLines(&source).GetEnumerator();
                    if (!iterator.MoveNext() || iterator.Current.Length != 1300) return 20;
                    int before = allocations;
                    readonly(char)* address = iterator.Current.Data;
                    if (iterator.Current[1299] != 'a' || iterator.Current.Data != address || allocations != before) return 21;
                    String saved = iterator.Current;
                    if (saved.Data == address || saved.Length != 1300) return 22;
                    var moved = move iterator;
                    before = allocations;
                    if (moved.Current.Data != address || !moved.MoveNext() || moved.Current.Length != 0) return 23;
                    if (!moved.MoveNext() || moved.Current.Length != 1 || moved.Current[0] != 'b') return 24;
                    if (!moved.MoveNext() || moved.Current.Length != 513 || moved.Current[512] != 'c') return 25;
                    if (moved.Current.Data != address || allocations != before || saved[0] != 'a') return 26;
                    saved[0] = 'Q';
                    if (moved.Current[0] != 'c' || moved.MoveNext()) return 27;
                }
                return allocations - releases;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ErrorsInvalidateCurrentAndRetryFromTheConsumedCursor(int optimization)
        => Run("""
            int main()
            {
                {
                    Source source = new("first\n?tail\nlast\n", 17);
                    var iterator = EnumerateLines(&source).GetEnumerator();
                    if (!iterator.MoveNext() || iterator.Current.Length != 5 || source.Position != 6) return 30;
                    source.FailNext = true;
                    bool caught = false;
                    try { iterator.MoveNext(); } catch (ref Exception error) { caught = error.code == 73; }
                    if (!caught || source.Position != 7) return 31;
                    nuint before = source.Calls;
                    caught = false;
                    try { iterator.Current; } catch (ref IOException error) { caught = true; }
                    if (!caught || source.Calls != before) return 32;
                    if (!iterator.MoveNext() || iterator.Current.Length != 4 || iterator.Current[0] != 't'
                        || source.Position != 12) return 33;
                    source.OverreportNext = true;
                    caught = false;
                    try { iterator.MoveNext(); } catch (ref IOException error) { caught = true; }
                    if (!caught || source.Position != 12) return 34;
                    caught = false;
                    try { iterator.Current; } catch (ref IOException error) { caught = true; }
                    if (!caught || !iterator.MoveNext() || iterator.Current.Length != 4
                        || iterator.Current[0] != 'l' || source.Position != 17) return 35;
                    if (iterator.MoveNext()) return 36;
                }
                return allocations - releases;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BorrowedSequencesShareTheCursorAndForeachBreakPreservesTheNextLine(int optimization)
        => Run("""
            int main()
            {
                Source source = new("one\ntwo\nthree\n", 14);
                LineSequence first = EnumerateLines(&source);
                LineSequence copied = first;
                foreach (String line in first)
                {
                    if (line.Length != 3 || line[0] != 'o') return 40;
                    break;
                }
                if (source.Position != 4 || allocations != releases) return 41;
                {
                    var left = copied.GetEnumerator();
                    var right = first.GetEnumerator();
                    if (!left.MoveNext() || left.Current[0] != 't' || left.Current.Length != 3) return 42;
                    if (!right.MoveNext() || right.Current.Length != 5 || source.Position != 14) return 43;
                    if (left.Current.Length != 3 || left.Current[0] != 't' || left.MoveNext()) return 44;
                }
                if (ReadByte(&source) != -1) return 45;
                return allocations - releases;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void QueriesStayDeferredCopySavedLinesAndStopAtLogicalBoundaries(int optimization)
        => Run("""
            bool Fail(String line) { throw new Exception("Predicate failure", 74); }
            int main()
            {
                Source source = new("one\ntwo\nthree\nlast\n", 19);
                {
                    var query = source.EnumerateLines().AsEnumerable();
                    var empty = query.Take(0);
                    if (empty.Count() != 0 || source.Calls != 0 || allocations != 0) return 50;
                    Vector<String> saved = query.Take(2).ToVector();
                    if (saved.Count != 2 || saved[0].Length != 3 || saved[1][0] != 't' || source.Position != 8) return 51;
                    saved[0][0] = 'O';
                    if (saved[1][0] != 't') return 52;
                    bool caught = false;
                    try { query.Where(Fail).Count(); } catch (ref Exception error) { caught = error.code == 74; }
                    if (!caught || source.Position != 14) return 53;
                    if (query.Count() != 1 || source.Position != 19 || saved[0][0] != 'O') return 54;
                }
                return allocations - releases;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void IteratorCopiesOwnIndependentSnapshotsAndShareOnlyTheSourceCursor(int optimization)
        => Run("""
            int main()
            {
                Source source = new("one\ntwo\nthree\n", 14);
                {
                    var first = EnumerateLines(&source).GetEnumerator();
                    if (!first.MoveNext() || first.Current[0] != 'o') return 70;
                    var copied = first;
                    if (copied.Current.Length != 3 || copied.Current[0] != 'o'
                        || copied.Current.Data == first.Current.Data) return 71;
                    if (!copied.MoveNext() || copied.Current[0] != 't' || source.Position != 8) return 72;
                    if (first.Current[0] != 'o' || first.Current.Length != 3) return 73;
                    readonly(char)* copiedAddress = copied.Current.Data;
                    var moved = move copied;
                    if (moved.Current.Data != copiedAddress || moved.Current.Length != 3) return 74;
                    if (!first.MoveNext() || first.Current.Length != 5 || source.Position != 14) return 75;
                    if (moved.Current.Length != 3 || moved.Current[0] != 't') return 76;
                    if (first.MoveNext() || moved.MoveNext()) return 77;
                }
                return allocations - releases;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BufferedSourcesRetainPrefetchedTailThroughTheSameAdapter(int optimization)
        => Run("""
            int main()
            {
                {
                    Source source = new("head\nTAIL", 9);
                    char[13] scratch;
                    scratch[0] = 'L'; scratch[12] = 'R';
                    BufferedReader buffered = new(&source, &scratch[1], 11);
                    LineSequence lines = buffered.EnumerateLines();
                    var iterator = lines.GetEnumerator();
                    if (source.Calls != 0 || allocations != 0) return 60;
                    if (!iterator.MoveNext() || iterator.Current.Length != 4 || iterator.Current[0] != 'h') return 61;
                    if (buffered.PeekByte() != (int)'T' || buffered.ReadByte() != (int)'T') return 62;
                    if (!iterator.MoveNext() || iterator.Current.Length != 3 || iterator.Current[0] != 'A') return 63;
                    if (iterator.MoveNext() || scratch[0] != 'L' || scratch[12] != 'R') return 64;
                }
                return allocations - releases;
            }
            """, optimization);

    [Theory]
    [InlineData("iterator.Current[0] = 'X';", "immutable")]
    [InlineData("iterator.Current.Clear();", "immutable")]
    public void IteratorCannotMutateItsBorrowedCurrent(string invalid, string diagnostic)
    {
        string program = """
            int main()
            {
                Source source = new("line\n", 5);
                var iterator = EnumerateLines(&source).GetEnumerator();
                iterator.MoveNext();
            """ + invalid + "return 0;}";
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Sources(program), options: new() { NoStdLib = true }));
        Assert.Contains(diagnostic, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<SourceFile> Sources(string program)
        => Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "Standard"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("line-sequence-tests.gf", Support + program));

    private static void Run(string program, int optimization)
    {
        string ir = Compiler.Emit(Sources(program), options: new() { NoStdLib = true, InternalizeFunctions = true });
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }
}
