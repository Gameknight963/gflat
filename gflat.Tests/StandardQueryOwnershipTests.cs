namespace gflat.Tests;

public class StandardQueryOwnershipTests
{
    private const string AllocationCounters = """
        using std; using std::Text; using std::Queries; using std::Collections;
        extern void*? malloc(nuint bytes);
        extern void free(void* storage);
        int allocations = 0;
        int releases = 0;
        namespace Memory {
            public replace void*? Allocate(nuint bytes) {
                void*? result = malloc(bytes);
                if (result != null) allocations++;
                return result;
            }
            public replace void Free(void* storage) nothrow { releases++; free(storage); }
        }
        String Join(String accumulated, String item) {
            accumulated.AppendLiteral(item.Data, item.Length);
            return move accumulated;
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OwningStringTerminalsReturnIndependentValuesAndIgnoredResultsAreDestroyed(int optimization)
        => Run(AllocationCounters + """
            int main() {
                {
                    Vector<String> words = new();
                    words.Add(s"one"); words.Add(s"two"); words.Add(s"three");
                    var query = words.AsEnumerable();
                    int baseline = allocations - releases;
                    query.First(); query.Last(); query.Take(1).Single();
                    query.Aggregate(s"", Join); query.ToVector();
                    if (allocations - releases != baseline) return 1;
                    {
                        String first = query.First(); String last = query.Last();
                        String single = query.Skip(1).Take(1).Single();
                        if (first.Data == words[0].Data || last.Data == words[2].Data || single.Data == words[1].Data) return 2;
                        first[0] = 'X'; last[0] = 'Y'; single[0] = 'Z';
                        if (words[0][0] != 'o' || words[1][0] != 't' || words[2][0] != 't') return 3;
                        String result = s"unchanged";
                        readonly(char)* previous = result.Data;
                        if (query.Take(0).TryFirst(ref result) || result.Data != previous) return 4;
                        if (!query.TryFirst(ref result) || result.Data == words[0].Data || result.Length != 3) return 5;
                        String combined = query.Aggregate(s"", Join);
                        if (combined.Length != 11 || combined[0] != 'o' || combined[10] != 'e') return 6;
                        Vector<String> copied = query.ToVector();
                        if (copied.Count != 3 || copied[0].Data == words[0].Data) return 7;
                        copied[0][0] = 'Q';
                        if (words[0][0] != 'o') return 8;
                    }
                    if (allocations - releases != baseline) return 9;
                    // Assignment back into the first source element must copy before destruction.
                    if (!query.TryFirst(ref words[0]) || words[0].Length != 3 || words[0][0] != 'o') return 10;
                    if (allocations - releases != baseline) return 11;
                }
                if (allocations == 0 || allocations != releases) return 12;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CallbackExceptionsReleaseOwningArgumentsAccumulatorsAndPartialMaterialization(int optimization)
        => Run(AllocationCounters + """
            int calls = 0;
            bool FailPredicate(String text) {
                calls++;
                if (calls == 2) throw new Exception("predicate");
                return true;
            }
            String FailProjection(String text) {
                calls++;
                if (calls == 2) throw new Exception("projection");
                text.AppendLiteral("!", 1);
                return move text;
            }
            String FailFold(String accumulated, String item) {
                calls++;
                if (calls == 2) throw new Exception("fold");
                accumulated.AppendLiteral(item.Data, item.Length);
                return move accumulated;
            }
            void FailAction(String text) {
                calls++;
                if (calls == 2) throw new Exception("action");
            }
            int main() {
                {
                    Vector<String> words = new(); words.Add(s"a"); words.Add(s"bb"); words.Add(s"ccc");
                    var query = words.AsEnumerable();
                    int baseline = allocations - releases;
                    calls = 0; bool caught = false;
                    try { query.Where(FailPredicate).Last(); } catch { caught = true; }
                    if (!caught || calls != 2 || allocations - releases != baseline) return 1;
                    calls = 0; caught = false;
                    try { query.Select(FailProjection).ToVector(); } catch { caught = true; }
                    if (!caught || calls != 2 || allocations - releases != baseline) return 2;
                    calls = 0; caught = false;
                    try { query.Aggregate(s"seed", FailFold); } catch { caught = true; }
                    if (!caught || calls != 2 || allocations - releases != baseline) return 3;
                    calls = 0; caught = false;
                    try { query.All(FailPredicate); } catch { caught = true; }
                    if (!caught || calls != 2 || allocations - releases != baseline) return 4;
                    calls = 0; caught = false;
                    try { query.ForEach(FailAction); } catch { caught = true; }
                    if (!caught || calls != 2 || allocations - releases != baseline) return 5;
                    caught = false;
                    try { query.Single(); } catch { caught = true; }
                    if (!caught || allocations - releases != baseline) return 6;
                    String output = s"keep";
                    readonly(char)* previous = output.Data;
                    baseline = allocations - releases;
                    calls = 1; caught = false;
                    try { query.Select(FailProjection).TryFirst(ref output); } catch { caught = true; }
                    if (!caught || output.Data != previous || output.Length != 4 || allocations - releases != baseline) return 7;
                    if (words[0].Length != 1 || words[1].Length != 2 || words[2].Length != 3) return 8;
                }
                if (allocations != releases) return 9;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ResourceOwningIteratorsAreDestroyedExactlyOnceForEveryTerminalAndFailure(int optimization)
        => Run(AllocationCounters + """
            int created = 0; int destroyed = 0; int active = 0;
            struct Source {
                private int count;
                public Source(int length) { count = length; }
                public struct Iterator {
                    private String token;
                    private int remaining;
                    public Iterator(int count) { token = s"iterator"; remaining = count; created++; active++; }
                    public bool MoveNext() {
                        if (remaining == 0) return false;
                        remaining--; return true;
                    }
                    public String Current { get => s"element"; }
                    ~Iterator() { destroyed++; active--; }
                }
                public Source::Iterator GetEnumerator() => new Source::Iterator(count);
            }
            bool Reject(String text) => false;
            bool Fail(String text) { throw new Exception("fail"); }
            int main() {
                var query = new Enumerable<String, Source, Source::Iterator>(new Source(3));
                var one = new Enumerable<String, Source, Source::Iterator>(new Source(1));
                var empty = new Enumerable<String, Source, Source::Iterator>(new Source(0));
                query.First(); query.Last(); one.Single(); query.Any(); query.Count();
                query.All(Reject); query.Aggregate(s"", Join); query.ToVector();
                String output = s"old";
                if (!query.TryFirst(ref output)) return 1;
                foreach (String value in query) { break; }
                try { query.Single(); return 2; } catch {}
                try { query.Where(Fail).Last(); return 3; } catch {}
                try { empty.First(); return 4; } catch {}
                try { empty.Last(); return 5; } catch {}
                try { empty.Single(); return 6; } catch {}
                if (empty.TryFirst(ref output)) return 7;
                // output is the only remaining owned buffer.
                if (created != 16 || destroyed != created || active != 0 || allocations - releases != 1) return 8;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ThrowingElementCopiesCleanPartialFieldsAndLeaveTryFirstDestinationIntact(int optimization)
        => Run(AllocationCounters + """
            int live = 0; int copies = 0; int failAt = 0;
            struct Item {
                private String text;
                public Item(String value) { text = move value; live++; }
                public copy Item(immutable(Item)* source) {
                    copies++;
                    text = source.text;
                    if (copies == failAt) throw new Exception("copy");
                    live++;
                }
                public nuint Length { immutable get => text.Length; }
                ~Item() { live--; }
            }
            int main() {
                {
                    Vector<Item> values = new();
                    values.Add(new Item(s"a")); values.Add(new Item(s"bb")); values.Add(new Item(s"ccc"));
                    var query = values.AsEnumerable();
                    int baseline = allocations - releases;
                    copies = 0; failAt = 2; bool caught = false;
                    try { query.Last(); } catch { caught = true; }
                    if (!caught || copies != 2 || live != 3 || allocations - releases != baseline) return 1;
                    copies = 0; failAt = 2; caught = false;
                    try { query.ToVector(); } catch { caught = true; }
                    if (!caught || copies != 2 || live != 3 || allocations - releases != baseline) return 2;
                    Item output = new Item(s"unchanged");
                    baseline = allocations - releases;
                    copies = 0; failAt = 1; caught = false;
                    try { query.TryFirst(ref output); } catch { caught = true; }
                    if (!caught || output.Length != 9 || live != 4 || allocations - releases != baseline) return 3;
                    failAt = 0;
                    if (values.Count != 3 || values[0].Length != 1 || values[2].Length != 3) return 4;
                }
                if (live != 0 || allocations != releases) return 5;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ReenumeratingBorrowedViewsSeesEditsAndShortCircuitsWithoutOwningTheSource(int optimization)
        => Run(AllocationCounters + """
            int calls = 0;
            bool LongEnough(String word) { calls++; return word.Length >= 2; }
            String Project(String word) { calls++; word.AppendLiteral("!", 1); return move word; }
            int main() {
                {
                    Vector<String> words = new(); words.Add(s"a"); words.Add(s"bb"); words.Add(s"ccc");
                    Vector<String>* borrowed = &words;
                    int baseline = allocations - releases;
                    var query = borrowed.AsEnumerable().Where(LongEnough).Select(Project).Take(1);
                    // Query creation and Take(0) do not touch owning elements or callbacks.
                    if (calls != 0 || allocations - releases != baseline || query.Take(0).Any()) return 1;
                    query.First();
                    if (calls != 3 || allocations - releases != baseline) return 2;
                    calls = 0;
                    words[0].AppendLiteral("z", 1);
                    String first = query.First();
                    if (calls != 2 || first.Length != 3 || first[0] != 'a' || first[1] != 'z' || first[2] != '!') return 3;
                    if (words[0].Length != 2 || words[1].Length != 2 || words[2].Length != 3) return 4;
                    calls = 0;
                    if (!query.Any() || calls != 1) return 5;
                    calls = 0;
                    if (query.Count() != 1 || calls != 1) return 6;
                }
                if (allocations != releases) return 7;
                return 0;
            }
            """, optimization);

    private static void Run(string code, int optimization)
    {
        var sources = Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "Std/core"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("main.gf", code));
        string ir = Compiler.Emit(sources);
        string directory = Path.Combine(Path.GetTempPath(), "gflat-query-ownership-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "queries.ll"), output = Path.Combine(directory, "queries.exe");
            File.WriteAllText(input, ir);
            var compiled = NativeToolchain.Compile(input, output, optimizationLevel: optimization);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            var result = NativeToolchain.Run(output, []);
            Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
        }
        finally { Directory.Delete(directory, true); }
    }
}
