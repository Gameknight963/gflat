namespace gflat.Tests;

public sealed class StandardQueryIndexedWhereTests
{
    private static void Run(string code, int optimization)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Standard"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("indexed-where.gf", code));
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
    public void SeededFiltersUseAllEncounterIndicesAndIndependentCursorsWithoutAllocation(int optimization)
    {
        var random = new Random(73732);
        int[] values = Enumerable.Range(0, 32).Select(_ => random.Next(-100, 101)).ToArray();
        int[] expected = values.Where((value, index) => value >= 0 && index % 3 != 1).ToArray();
        int[] counts = Enumerable.Range(0, 33)
            .Select(length => values.Take(length).Where((value, index) => value >= 0 && index % 3 != 1).Count()).ToArray();
        int chain = values.Where(value => value >= 0).Where((_, index) => index % 2 == 0).Sum();
        Run(Memory + $$$"""
            int calls = 0;
            bool bad = false;
            bool Keep(int value, nuint index)
            {
                if (index != (nuint)calls) bad = true;
                calls++;
                return value >= 0 && index % 3 != 1;
            }
            int main()
            {
                int[32] data = [{{{string.Join(", ", values)}}}];
                int[{{{expected.Length}}}] expected = [{{{string.Join(", ", expected)}}}];
                int[33] counts = [{{{string.Join(", ", counts)}}}];
                Span<int> input = new(data, 32);
                var plain = input.AsEnumerable().Where((int value) => value >= 0);
                var indexed = input.AsEnumerable().Where(Keep);
                var even = input.AsEnumerable().Where((int value, nuint index) => index % 2 == 0);
                var plainAgain = input.AsEnumerable().Where((int value) => value < 0);
                denied = true;
                if (plain.Count() + plainAgain.Count() != 32) return 1;
                if (indexed.Count() != (nuint)counts[32] || calls != 32 || bad) return 2;
                for (nuint length = 0; length <= 32; length++)
                {
                    calls = 0;
                    var cursor = input.Slice(0, length).AsEnumerable().Where(Keep).GetEnumerator();
                    nuint position = 0;
                    while (cursor.MoveNext())
                    {
                        if (cursor.Current != expected[position] || cursor.Current != expected[position]) return 3;
                        position++;
                    }
                    if (position != (nuint)counts[length] || calls != (int)length || bad) return 4;
                }
                var left = even.GetEnumerator();
                var right = even.GetEnumerator();
                if (!left.MoveNext() || !left.MoveNext() || left.Current != data[2]) return 5;
                if (!right.MoveNext() || right.Current != data[0]) return 6;
                var copied = left;
                if (!left.MoveNext() || left.Current != data[4] || copied.Current != data[2]) return 9;
                if (!copied.MoveNext() || copied.Current != data[4]) return 10;
                int sum = 0;
                foreach (int value in input.AsEnumerable().Where((int value) => value >= 0)
                    .Where((int value, nuint index) => index % 2 == 0)) sum += value;
                if (sum != {{{chain}}}) return 7;
                denied = false;
                if (allocated != released) return 8;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PredicateAndInputFailuresConsumeTheirEncounterWhileAcceptedGetterCanRetry(int optimization)
        => Run(Memory + """
            bool failMove = false;
            bool failRead = false;
            bool failPredicate = false;
            int trace = 0;
            int calls = 0;
            struct Source
            {
                public Cursor GetEnumerator() => new();
                public struct Cursor
                {
                    private int position;
                    public Cursor() { position = -1; }
                    public bool MoveNext()
                    {
                        if (failMove) { failMove = false; throw new Exception("move", 31); }
                        position++;
                        return position < 6;
                    }
                    public int Current
                    {
                        get
                        {
                            if (failRead) { failRead = false; throw new Exception("read", 32); }
                            return 10 + position;
                        }
                    }
                }
            }
            bool Keep(int value, nuint index)
            {
                calls++;
                trace = trace * 10 + (int)index;
                if (value != 10 + (int)index) throw new Exception("wrong encounter");
                if (failPredicate) { failPredicate = false; throw new Exception("predicate", 33); }
                return index % 2 != 0;
            }
            int main()
            {
                Source source = new();
                var cursor = new Enumerable<int, Source, Source::Cursor>(source).Where(Keep).GetEnumerator();
                bool caught = false;
                try { cursor.Current; } catch (ref Exception error) { caught = true; }
                if (!caught || calls != 0) return 1;
                failMove = true;
                caught = false;
                try { cursor.MoveNext(); } catch (ref Exception error) { caught = error.GetCode() == 31; }
                if (!caught || !cursor.MoveNext() || cursor.Current != 11 || calls != 2) return 2;
                failRead = true;
                caught = false;
                try { cursor.Current; } catch (ref Exception error) { caught = error.GetCode() == 32; }
                if (!caught || cursor.Current != 11 || calls != 2) return 3;
                failRead = true;
                caught = false;
                try { cursor.MoveNext(); } catch (ref Exception error) { caught = error.GetCode() == 32; }
                if (!caught) return 4;
                caught = false;
                try { cursor.Current; } catch (ref Exception error) { caught = true; }
                if (!caught) return 5;
                failPredicate = true;
                caught = false;
                try { cursor.MoveNext(); } catch (ref Exception error) { caught = error.GetCode() == 33; }
                if (!caught) return 6;
                caught = false;
                try { cursor.Current; } catch (ref Exception error) { caught = true; }
                if (!caught || !cursor.MoveNext() || cursor.Current != 15) return 7;
                if (calls != 5 || trace != 1345 || cursor.MoveNext()) return 8;
                caught = false;
                try { cursor.Current; } catch (ref Exception error) { caught = true; }
                if (!caught || allocated != released) return 9;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OwningPredicateCopiesAndUpstreamCursorCleanupPreserveSourceValues(int optimization)
        => Run(Memory + """
            bool fail = false;
            int calls = 0;
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
            bool Keep(String value, nuint index)
            {
                calls++;
                value[0] = 'X';
                if (fail && index == 1) throw new Exception("predicate", 41);
                return index % 2 == 0;
            }
            int main()
            {
                {
                    Vector<String> words = new();
                    words.Add(s"a"); words.Add(s"bb"); words.Add(s"ccc");
                    var query = words.AsEnumerable().Where(Keep);
                    int baseline = allocated - released;
                    if (query.Count() != 2 || calls != 3) return 1;
                    if (allocated - released != baseline) return 2;
                    {
                        var cursor = query.GetEnumerator();
                        if (!cursor.MoveNext()) return 3;
                        String first = cursor.Current;
                        String again = cursor.Current;
                        if (first.Data == again.Data || first.Data == words[0].Data) return 4;
                        if (first[0] != 'a' || again[0] != 'a') return 5;
                    }
                    fail = true;
                    bool caught = false;
                    try { query.ToVector(); }
                    catch (ref Exception error) { caught = error.GetCode() == 41; }
                    if (!caught || allocated - released != baseline) return 6;
                    if (words[0][0] != 'a' || words[1][0] != 'b' || words[2][0] != 'c') return 7;
                    OwningSource source = new();
                    var owned = new Enumerable<String, OwningSource, OwningSource::Cursor>(source).Where(Keep);
                    fail = false;
                    foreach (String item in owned.Take(1)) { if (item[0] != 'o') return 8; }
                    if (cursorDead != 1 || allocated - released != baseline) return 9;
                    fail = true;
                    caught = false;
                    try { owned.ToVector(); }
                    catch (ref Exception error) { caught = error.GetCode() == 41; }
                    if (!caught || cursorDead != 2 || allocated - released != baseline) return 10;
                }
                if (allocated != released) return 11;
                return 0;
            }
            """, optimization);
}
