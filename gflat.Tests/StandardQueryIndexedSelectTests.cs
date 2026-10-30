namespace gflat.Tests;

public sealed class StandardQueryIndexedSelectTests
{
    private static void Run(string code, int optimization)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Standard"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("indexed-select.gf", code));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }

    private const string Memory = """
        using Standard; using Standard::Text; using Standard::Memory; using Standard::Queries; using Standard::Collections;
        extern void*? malloc(nuint bytes);
        extern void free(void* storage);
        int allocated = 0;
        int released = 0;
        bool denied = false;
        namespace Memory
        {
            public replace void*? Allocate(nuint bytes)
            {
                if (denied) return null;
                void*? result = malloc(bytes);
                if (result != null) allocated++;
                return result;
            }
            public replace void Free(void* storage) nothrow { released++; free(storage); }
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SeededIndexedValuesChainsAndMixedSelectorAritiesMatchHost(int optimization)
    {
        var random = new Random(73731);
        int[] values = Enumerable.Range(0, 32).Select(_ => random.Next(-100, 101)).ToArray();
        int[] expected = values.Select((value, index) => value * 3 + index).ToArray();
        int chain = values.Where(value => value >= 0).Select((value, index) => value - index).Sum();
        Run(Memory + $$$"""
            int calls = 0;
            int Project(int value, nuint index) { calls++; return value * 3 + (int)index; }
            int main()
            {
                int[32] data = [{{{string.Join(", ", values)}}}];
                int[32] expected = [{{{string.Join(", ", expected)}}}];
                Span<int> source = new(data, 32);
                {
                    var ordinary = source.AsEnumerable().Select((int value) => value + 1);
                    var indexed = source.AsEnumerable().Select(Project);
                    var second = source.AsEnumerable().Select((int value, nuint index) => value - (int)index);
                    var plainAgain = source.AsEnumerable().Select((int value) => value - 1);
                    denied = true;
                    if (ordinary.First() != data[0] + 1 || plainAgain.First() != data[0] - 1) return 1;
                    if (second.First() != data[0]) return 2;
                    if (indexed.Count() != 32 || calls != 0 || !indexed.Any() || calls != 0) return 3;
                    var left = indexed.GetEnumerator();
                    var right = indexed.GetEnumerator();
                    if (!left.MoveNext() || !left.MoveNext() || left.Current != expected[1]) return 4;
                    if (!right.MoveNext() || right.Current != expected[0]) return 5;
                    if (left.Current != expected[1] || calls != 3) return 6;
                    for (nuint length = 0; length <= 32; length++)
                    {
                        var cursor = source.Slice(0, length).AsEnumerable().Select(Project).GetEnumerator();
                        nuint position = 0;
                        while (cursor.MoveNext())
                        {
                            if (cursor.Current != expected[position]) return 7;
                            position++;
                        }
                        if (position != length) return 8;
                    }
                    int sum = 0;
                    foreach (int value in source.AsEnumerable().Where((int value) => value >= 0)
                        .Select((int value, nuint index) => value - (int)index)) sum += value;
                    if (sum != {{{chain}}}) return 9;
                    denied = false;
                }
                if (allocated != released) return 10;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MoveAndGetterFailuresRetainAssignedIndexAndInvalidateOnlyAsDocumented(int optimization)
        => Run(Memory + """
            int moves = 0;
            int reads = 0;
            int calls = 0;
            bool failMove = false;
            bool failRead = false;
            bool failSelector = false;
            struct Source
            {
                public Cursor GetEnumerator() => new();
                public struct Cursor
                {
                    private int position;
                    public Cursor() { position = -1; }
                    public bool MoveNext()
                    {
                        moves++;
                        if (failMove) { failMove = false; throw new Exception("move", 31); }
                        position++;
                        return position < 3;
                    }
                    public int Current
                    {
                        get
                        {
                            reads++;
                            if (failRead) { failRead = false; throw new Exception("read", 32); }
                            return position + 10;
                        }
                    }
                }
            }
            int Project(int value, nuint index)
            {
                calls++;
                if (failSelector) { failSelector = false; throw new Exception("selector", 33); }
                return value * 10 + (int)index;
            }
            int main()
            {
                Source source = new();
                var query = new Enumerable<int, Source, Source::Cursor>(source).Select(Project);
                var cursor = query.GetEnumerator();
                bool caught = false;
                try { cursor.Current; } catch (ref Exception error) { caught = true; }
                if (!caught || reads != 0 || calls != 0) return 1;
                failMove = true;
                caught = false;
                try { cursor.MoveNext(); } catch (ref Exception error) { caught = error.GetCode() == 31; }
                if (!caught || !cursor.MoveNext() || cursor.Current != 100) return 2;
                failRead = true;
                caught = false;
                try { cursor.Current; } catch (ref Exception error) { caught = error.GetCode() == 32; }
                if (!caught || cursor.Current != 100) return 3;
                failSelector = true;
                caught = false;
                try { cursor.Current; } catch (ref Exception error) { caught = error.GetCode() == 33; }
                if (!caught || cursor.Current != 100) return 4;
                failMove = true;
                caught = false;
                try { cursor.MoveNext(); } catch (ref Exception error) { caught = error.GetCode() == 31; }
                if (!caught) return 5;
                caught = false;
                try { cursor.Current; } catch (ref Exception error) { caught = true; }
                if (!caught || !cursor.MoveNext() || cursor.Current != 111) return 6;
                if (!cursor.MoveNext() || cursor.Current != 122 || cursor.MoveNext()) return 7;
                caught = false;
                try { cursor.Current; } catch (ref Exception error) { caught = true; }
                if (!caught || calls != 6 || reads != 7 || moves != 6) return 8;
                if (allocated != released) return 9;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OwningArgumentsResultsAndIteratorOwnersAreReleasedOnCallbackFailureAndEarlyExit(int optimization)
        => Run(Memory + """
            int calls = 0;
            bool fail = false;
            int cursorDead = 0;
            struct OwningSource
            {
                public Cursor GetEnumerator() => new();
                public struct Cursor
                {
                    private int* token;
                    private int position;
                    public Cursor() { token = new* int(7); position = -1; }
                    public ~Cursor() { cursorDead++; delete token; }
                    public bool MoveNext() { position++; return position < 3; }
                    public String Current { get => s"owned"; }
                }
            }
            String Project(String value, nuint index)
            {
                calls++;
                if (fail && index == 1) throw new Exception("projection", 41);
                value.AppendLiteral("!", 1);
                return move value;
            }
            int main()
            {
                {
                    Vector<String> words = new();
                    words.Add(s"a"); words.Add(s"bb"); words.Add(s"ccc");
                    var query = words.AsEnumerable().Select(Project);
                    int baseline = allocated - released;
                    if (query.Count() != 3 || calls != 0) return 1;
                    {
                        var cursor = query.GetEnumerator();
                        if (!cursor.MoveNext()) return 2;
                        String first = cursor.Current;
                        String again = cursor.Current;
                        if (first.Data == again.Data || first.Data == words[0].Data) return 3;
                        first[0] = 'X';
                        if (again[0] != 'a' || words[0][0] != 'a') return 4;
                    }
                    if (allocated - released != baseline) return 5;
                    fail = true;
                    bool caught = false;
                    try { query.ToVector(); }
                    catch (ref Exception error) { caught = error.GetCode() == 41; }
                    if (!caught || allocated - released != baseline) return 6;
                    if (words[0].Length != 1 || words[1].Length != 2 || words[2].Length != 3) return 7;
                    fail = false;
                    foreach (String item in query.Take(1)) { if (item.Length != 2) return 8; }
                    if (allocated - released != baseline) return 9;
                    OwningSource source = new();
                    var owned = new Enumerable<String, OwningSource, OwningSource::Cursor>(source).Select(Project);
                    foreach (String item in owned.Take(1)) { if (item.Length != 6) return 11; }
                    if (cursorDead != 1 || allocated - released != baseline) return 12;
                    fail = true;
                    caught = false;
                    try { owned.ToVector(); }
                    catch (ref Exception error) { caught = error.GetCode() == 41; }
                    if (!caught || cursorDead != 2 || allocated - released != baseline) return 13;
                }
                if (allocated != released) return 10;
                return 0;
            }
            """, optimization);
}
