using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class StandardQueryTakeLastTests
{
    private const string Allocator = """
        using std; using std::Collections;
        extern void*? malloc(nuint bytes); extern void free(void* storage);
        int attempts = 0; int allocations = 0; int releases = 0; int failAt = 0;
        bool deny = false; nuint largest = 0;
        namespace Memory {
            public replace void*? Allocate(nuint bytes) {
                attempts++;
                if (bytes > largest) largest = bytes;
                if (deny || (failAt != 0 && attempts == failAt)) return null;
                void*? result = malloc(bytes);
                if (result != null) allocations++;
                return result;
            }
            public replace void Free(void* storage) nothrow { releases++; free(storage); }
        }
        """;

    private static IEnumerable<SourceFile> Sources(string program)
        => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std", "core"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("query-take-last.gf", program));

    private static void Run(string program, int optimization)
    {
        var result = CompilerTestHelper.Run(Sources(program), optimization);
        Assert.True(result.ExitCode == 0, $"TakeLast exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FiniteTailsMatchLinqWithDeferredIndependentEnumerationsAndBoundedStorage(int optimization)
    {
        var random = new Random(281903);
        var packet = new List<byte>();
        int rows = 0;
        for (int length = 0; length <= 17; length++)
        {
            int[] values = Enumerable.Range(0, length).Select(_ => random.Next(-100, 101)).ToArray();
            foreach (int count in new[] { 0, 1, Math.Max(0, length - 1), length, length + 1, 255 }.Distinct())
            {
                int[] expected = values.TakeLast(count).ToArray();
                packet.Add((byte)length);
                packet.Add((byte)count);
                packet.Add((byte)expected.Length);
                packet.AddRange(values.Select(value => (byte)(value + 100)));
                packet.AddRange(expected.Select(value => (byte)(value + 100)));
                rows++;
            }
        }
        string hex = Convert.ToHexString(packet.ToArray());
        Run(Allocator + $$$"""
            int starts = 0; int ends = 0; int moves = 0; int reads = 0;
            struct Source {
                private Span<int> values;
                public Source(Span<int> input) { values = input; }
                public struct Iterator {
                    private Span<int> values; private nuint position;
                    public Iterator(Span<int> input) { values = input; position = 0; starts++; }
                    public bool MoveNext() { moves++; if (position == values.Length) return false; position++; return true; }
                    public ref int Current { get { reads++; return values[position - 1]; } }
                    ~Iterator() { ends++; }
                }
                public Iterator GetEnumerator() => new Iterator(values);
            }
            nuint ReadByte(readonly(char)* text, ref nuint position) {
                nuint result = 0;
                for (int i = 0; i < 2; i++) {
                    char digit = text[position]; position++;
                    int value = (int)digit - (int)'0';
                    if (digit >= 'A') value = (int)digit - (int)'A' + 10;
                    result = result * 16 + (nuint)value;
                }
                return result;
            }
            int main() {
                readonly(char)* packet = "{{{hex}}}"; nuint offset = 0;
                int[19] data; int[19] saved; int[17] expected;
                for (int row = 0; row < {{{rows}}}; row++) {
                    nuint length = ReadByte(packet, ref offset);
                    nuint count = ReadByte(packet, ref offset);
                    if (count == 255) count = (nuint)0 - (nuint)1;
                    nuint resultCount = ReadByte(packet, ref offset);
                    for (nuint i = 0; i < 19; i++) data[i] = -777;
                    for (nuint i = 0; i < length; i++) data[i + 1] = (int)ReadByte(packet, ref offset) - 100;
                    for (nuint i = 0; i < resultCount; i++) expected[i] = (int)ReadByte(packet, ref offset) - 100;
                    for (nuint i = 0; i < 19; i++) saved[i] = data[i];
                    Span<int> view = new(&data[1], length);
                    var query = new Enumerable<int, Source, Source::Iterator>(new Source(view)).TakeLast(count);
                    starts = 0; ends = 0; moves = 0; reads = 0; int before = attempts;
                    deny = true;
                    if (query.Take(0).Count() != 0 || starts != 0 || moves != 0 || reads != 0 || attempts != before) return 1;
                    deny = false; largest = 0;
                    {
                        var first = query.GetEnumerator(); var second = query.GetEnumerator();
                        if (starts != 0 || attempts != before) return 2;
                        for (nuint i = 0; i < resultCount; i++) {
                            if (!first.MoveNext() || first.Current != expected[i] || first.Current != expected[i]) return 3;
                            if (!second.MoveNext() || second.Current != expected[i]) return 4;
                        }
                        if (first.MoveNext() || first.MoveNext() || second.MoveNext() || second.MoveNext()) return 5;
                        if (count == 0) {
                            if (starts != 0 || ends != 0 || moves != 0 || reads != 0 || attempts != before) return 6;
                        } else if (starts != 2 || ends != 2 || moves != (int)(2 * (length + 1)) || reads != (int)(2 * length)) return 7;
                    }
                    if (largest > (nuint)32 * (nuint)sizeof(int) || allocations != releases) return 8;
                    for (nuint i = 0; i < 19; i++) if (data[i] != saved[i]) return 9;
                }
                if (offset != {{{hex.Length}}}) return 10;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BufferedOwnersCopyIndependentlyAndCleanUpAcrossNormalAndEarlyExits(int optimization)
    {
        Run(Allocator + """
            int live = 0; int copies = 0;
            struct Owner {
                public int Id; public String Text;
                public Owner(int id) { Id = id; Text = s"owner"; live++; }
                public copy Owner(immutable(Owner)* input) { Id = input.Id; Text = input.Text; live++; copies++; }
                ~Owner() { live--; }
            }
            int First(Span<Owner> view) {
                foreach (Owner value in view.AsEnumerable().TakeLast(3)) return value.Id;
                return 0;
            }
            bool Fail(Owner value) { throw new Exception("body", 77); }
            int main() {
                {
                    Vector<Owner> values = new();
                    for (int i = 1; i <= 9; i++) values.Add(new Owner(i));
                    var query = values.AsEnumerable().TakeLast(3);
                    int balance = allocations - releases; int copied = copies;
                    {
                        var cursor = query.GetEnumerator();
                        if (!cursor.MoveNext() || live != 12 || copies != copied + 9) return 20;
                        {
                            Owner first = cursor.Current; Owner again = cursor.Current;
                            if (first.Id != 7 || again.Id != 7 || first.Text.Data == again.Text.Data || first.Text.Data == values[6].Text.Data) return 21;
                            first.Text[0] = 'X';
                            if (again.Text[0] != 'o' || values[6].Text[0] != 'o') return 22;
                        }
                        int expected = 8;
                        while (cursor.MoveNext()) { Owner value = cursor.Current; if (value.Id != expected) return 23; expected++; }
                        if (expected != 10 || live != 12) return 24;
                    }
                    if (live != 9 || allocations - releases != balance) return 25;
                    foreach (Owner value in query) { if (value.Id != 7) return 26; break; }
                    if (First(values.AsSpan()) != 7 || live != 9 || allocations - releases != balance) return 27;
                    bool caught = false;
                    try { query.Where(Fail).Any(); }
                    catch (ref Exception error) { if (error.GetCode() != 77) return 28; caught = true; }
                    if (!caught || live != 9 || allocations - releases != balance) return 29;
                    int expected = 7;
                    foreach (Owner value in query) { if (value.Id != expected) return 30; expected++; }
                    if (expected != 10 || live != 9 || allocations - releases != balance) return 31;
                }
                if (live != 0 || allocations != releases) return 32;
                return 0;
            }
            """, optimization);

        const string borrowed = """
            using std; using std::Collections;
            struct Unique { public int Id; public Unique(int id) { Id = id; } ~Unique() {} }
            int main() {
                Vector<Unique> input = new(); input.Add(new Unique(1));
                input.AsEnumerable().TakeLast(1).Count(); return 0;
            }
            """;
        Compiler.Check(Sources(borrowed.Replace(".TakeLast(1)", "", StringComparison.Ordinal)));
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Sources(borrowed)));
        Assert.Contains("AddLast", error.Message, StringComparison.Ordinal);
        Assert.Contains("cannot pass 'Unique' as 'Unique'", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InitializationFailuresCleanEveryStageAndRemainFinishedWhileCurrentCopyCanRetry(int optimization)
        => Run(Allocator + """
            int live = 0; int starts = 0; int ends = 0; int moves = 0; int mode = 0;
            struct Owner {
                public int Id; public String Text;
                public Owner(int id) { Id = id; Text = s"owner"; live++; }
                public copy Owner(immutable(Owner)* input) {
                    Id = input.Id; Text = input.Text;
                    if ((mode == 4 && Id == 3) || (mode == 5 && Id == 7)) throw new Exception("copy", 70 + mode);
                    live++;
                }
                ~Owner() { live--; }
            }
            struct Source {
                private Span<Owner> values;
                public Source(Span<Owner> input) { values = input; }
                public struct Iterator {
                    private Span<Owner> values; private nuint position; private String token;
                    public Iterator(Span<Owner> input) {
                        values = input; position = 0; token = s"iterator";
                        if (mode == 1) throw new Exception("construct", 71);
                        starts++;
                    }
                    public bool MoveNext() {
                        moves++;
                        if (mode == 2 && position == 2) throw new Exception("move", 72);
                        if (position == values.Length) return false;
                        position++; return true;
                    }
                    public ref Owner Current {
                        get { if (mode == 3 && position == 3) throw new Exception("current", 73); return values[position - 1]; }
                    }
                    ~Iterator() { ends++; }
                }
                public Iterator GetEnumerator() => new Iterator(values);
            }
            int main() {
                {
                    Vector<Owner> values = new(); for (int i = 1; i <= 9; i++) values.Add(new Owner(i));
                    var query = new Enumerable<Owner, Source, Source::Iterator>(new Source(values.AsSpan())).TakeLast(3);
                    int balance = allocations - releases;
                    mode = 1;
                    if (new Enumerable<Owner, Source, Source::Iterator>(new Source(values.AsSpan())).TakeLast(0).Count() != 0 || starts != 0) return 40;
                    mode = 0;
                    for (int failure = 1; failure <= 4; failure++) {
                        var cursor = query.GetEnumerator(); mode = failure; bool caught = false;
                        try { cursor.MoveNext(); }
                        catch (ref Exception error) { if (error.GetCode() != 70 + failure) return 41; caught = true; }
                        mode = 0; int priorMoves = moves; int priorStarts = starts;
                        if (!caught || cursor.MoveNext() || cursor.MoveNext() || moves != priorMoves || starts != priorStarts || starts != ends || live != 9 || allocations - releases != balance) return 42;
                        caught = false; try { cursor.Current; } catch { caught = true; }
                        if (!caught) return 43;
                    }
                    for (int kind = 0; kind < 2; kind++) {
                        nuint limit = 3;
                        if (kind == 1) limit = (nuint)0 - (nuint)1;
                        var allocationQuery = new Enumerable<Owner, Source, Source::Iterator>(new Source(values.AsSpan())).TakeLast(limit);
                        int points = 0;
                        { int before = attempts; var cursor = allocationQuery.GetEnumerator(); if (!cursor.MoveNext()) return 44; points = attempts - before; }
                        if (points == 0 || live != 9 || allocations - releases != balance) return 45;
                        for (int failure = 1; failure <= points; failure++) {
                            var cursor = allocationQuery.GetEnumerator(); failAt = attempts + failure; bool caught = false;
                            try { cursor.MoveNext(); } catch (ref BadAllocException error) { caught = true; }
                            failAt = 0; int priorMoves = moves;
                            if (!caught || cursor.MoveNext() || moves != priorMoves || starts != ends || live != 9 || allocations - releases != balance) return 46;
                        }
                    }
                    {
                        var cursor = query.GetEnumerator(); if (!cursor.MoveNext() || live != 12) return 47;
                        mode = 5; bool caught = false;
                        try { cursor.Current; } catch (ref Exception error) { if (error.GetCode() != 75) return 48; caught = true; }
                        mode = 0;
                        if (!caught || live != 12) return 49;
                        { Owner value = cursor.Current; if (value.Id != 7) return 50; }
                        if (!cursor.MoveNext()) return 51;
                        { Owner value = cursor.Current; if (value.Id != 8) return 52; }
                    }
                    if (live != 9 || starts != ends || allocations - releases != balance) return 53;
                    if (query.Count() != 3 || live != 9 || allocations - releases != balance) return 54;
                    for (nuint i = 0; i < values.Count; i++) if (values[i].Id != (int)i + 1 || values[i].Text.Length != 5) return 55;
                }
                if (live != 0 || starts != ends || allocations != releases) return 56;
                return 0;
            }
            """, optimization);
}
