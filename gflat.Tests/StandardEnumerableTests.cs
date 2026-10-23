namespace gflat.Tests;

public sealed class StandardEnumerableTests
{
    private static IEnumerable<SourceFile> Sources(string program) => Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "Std", "core"), "*.gf", SearchOption.AllDirectories)
        .Select(path => new SourceFile(path, File.ReadAllText(path)))
        .Append(new SourceFile("main.gf", program));

    private static void Run(string program, string? output = null)
    {
        var result = CompilerTestHelper.Run(Sources(program));
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
        if (output != null) Assert.Equal(output, result.StandardOutput);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ImmutableRangeAdaptersOwnTheirSmallSourceAndRemainLazy(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std;
            int calls = 0; int allocations = 0;
            namespace Memory {
                public replace void*? Allocate(nuint size) { allocations++; return null; }
                public replace void Free(void* storage) nothrow {}
            }
            bool Even(int value) { calls++; return value % 2 == 0; }
            int Square(int value) => value * value;
            int Sum(int total, int value) => total + value;
            Enumerable<int, FilteredIntRange, FilteredIntRange::Enumerator> Make() {
                immutable(FilteredIntRange) local = new IntRange(0, 10).Where(Even).Skip(1).Take(2);
                return local.AsEnumerable();
            }
            int main() {
                immutable(IntRange) source = new(0, 10);
                var all = source.AsEnumerable();
                immutable(IntRange)* view = &source;
                if (view.AsEnumerable().Count() != 10 || all.Last() != 9) return 1;
                var query = Make().Select(Square);
                if (calls != 0) return 2;
                if (query.Aggregate(0, Sum) != 20 || calls != 5) return 3;
                if (query.Aggregate(0, Sum) != 20 || calls != 10) return 4;
                if (source.Start != 0 || source.Count != 10 || allocations != 0) return 5;
                return 0;
            }
            """), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }

    [Fact]
    public void LazyOperatorsAllocateNothingAndShortCircuitInOrder()
        => Run("""
            using std;
            extern void*? malloc(nuint size);
            extern void free(void* storage);
            extern int putchar(int c);
            namespace Memory {
                public replace void*? Allocate(nuint size) { putchar(65); return malloc(size); }
                public replace void Free(void* p) nothrow { free(p); }
            }
            bool Even(int n) { putchar(48 + n); return n % 2 == 0; }
            int main() {
                Vector<int> values = new Vector<int>(); values.Reserve(8);
                for (int i = 1; i <= 6; i++) values.Add(i);
                var query = values.AsEnumerable().Where(Even).Take(1);
                putchar(124);
                if (query.First() != 2) return 1;
                putchar(124);
                if (query.Count() != 1) return 2;
                putchar(124);
                if (values.AsEnumerable().Where(Even).Take(0).Any()) return 3;
                return 0;
            }
            """, "A|12|12|");

    [Fact]
    public void EmptySequencesTerminalsAndOperationOrderAgree()
        => Run("""
            using std;
            bool Positive(int n) => n > 0;
            int Sum(int total, int n) => total + n;
            int main() {
                IntRange range = new IntRange(1, 5);
                var all = range.AsEnumerable();
                if (!all.Any() || !all.All(Positive) || all.Count() != 5) return 1;
                if (all.Aggregate(10, Sum) != 25) return 2;
                if (all.Last() != 5 || all.Take(1).Single() != 1) return 11;
                try { all.Single(); return 12; } catch {}
                if (all.Take(2).Skip(1).First() != 2) return 3;
                if (all.Skip(2).Take(1).First() != 3) return 4;
                var empty = all.Skip(100);
                int result = 42;
                if (empty.Any() || empty.Count() != 0 || !empty.All(Positive)) return 5;
                if (empty.TryFirst(ref result) || result != 42) return 6;
                if (!all.TryFirst(ref result) || result != 1) return 7;
                if (empty.Aggregate(7, Sum) != 7) return 8;
                try { empty.First(); return 9; } catch {}
                try { empty.Single(); return 13; } catch {}
                try { empty.Last(); return 14; } catch {}
                int total = 0;
                foreach (int value in all.Skip(1).Take(2)) total += value;
                if (total != 5) return 10;
                return 0;
            }
            """);

    [Fact]
    public void TypedLambdasCanChangeTheElementAndAccumulatorTypes()
        => Run("""
            using std;
            int main() {
                int[3] buffer = [1, 2, 3];
                Span<int> span = new Span<int>(buffer, 3);
                var query = span.AsEnumerable().Select((int n) => (long)n * 2L);
                long total = query.Aggregate(10L, (long sum, long n) => sum + n);
                if (total != 22L || query.First() != 2L) return 1;
                return 0;
            }
            """);

    [Fact]
    public void ReadonlyViewsKeepTheirElementQualificationsThroughQueries()
        => Run("""
            using std;
            bool Even(readonly(int) n) => n % 2 == 0;
            int main() {
                Vector<int> values = new Vector<int>(); values.Add(2); values.Add(4);
                var query = values.AsImmutable().AsEnumerable().Where(Even);
                if (query.Count() != 2 || query.First() != 2 || query.Last() != 4) return 1;
                if (query.Take(1).Single() != 2) return 2;
                return 0;
            }
            """);

    [Fact]
    public void PointerElementsRemainBorrowedAndTheSourceVectorIsNotOwned()
        => Run("""
            using std;
            extern int putchar(int c);
            struct Item {
                public int Value;
                public Item(int value) { Value = value; }
                ~Item() { putchar(68); }
            }
            bool Keep(Item* item) => item.Value == 42;
            int main() {
                Item* item = new* Item(42);
                Vector<Item*>* values = new* Vector<Item*>(); values.Add(item);
                {
                    var query = values.AsEnumerable().Where(Keep);
                    Vector<Item*> result = query.ToVector();
                    if (result.Count != 1 || result[0] != item) return 1;
                }
                if (item.Value != 42 || values.Count != 1) return 2;
                delete values;
                putchar(124);
                delete item;
                return 0;
            }
            """, "|D");

    [Fact]
    public void MaterializingOwningElementsMakesIndependentCopies()
        => Run("""
            using std;
            int Length(String text) => (int)text.Length;
            int main() {
                Vector<String> words = new Vector<String>(); words.Add(s"hello");
                Vector<String> copy = words.AsEnumerable().ToVector();
                if (copy[0].Data == words[0].Data) return 1;
                copy[0][0] = 'j';
                if (words[0][0] != 'h') return 2;
                if (words.AsEnumerable().Select(Length).First() != 5) return 3;
                return 0;
            }
            """);

    [Fact]
    public void MaterializationCleansPartialResultsWhenProjectionThrows()
        => Run("""
            using std;
            extern void*? malloc(nuint size);
            extern void free(void* storage);
            int allocations = 0;
            int releases = 0;
            namespace Memory {
                public replace void*? Allocate(nuint size) {
                    void*? result = malloc(size);
                    if (result != null) allocations++;
                    return result;
                }
                public replace void Free(void* p) nothrow { releases++; free(p); }
            }
            String Project(int n) {
                if (n == 2) throw new Exception("projection failed");
                return s"first";
            }
            int main() {
                IntRange source = new IntRange(1, 3);
                try { source.AsEnumerable().Select(Project).ToVector(); return 1; }
                catch {}
                if (allocations == 0 || allocations != releases) return 2;
                return 0;
            }
            """);

    [Fact]
    public void ManagedProjectionResultsSurviveCollectionAndMaterialization()
    {
        var sources = Sources("""
            using std;
            class Item { public int Value; public Item(int value) { Value = value; } }
            Item^ Project(Item^ item) { GC::Collect(); return item; }
            int main() {
                Vector<Item^> source = new Vector<Item^>(); source.Add(new^ Item(42));
                var query = source.AsEnumerable().Select(Project);
                Vector<Item^> result = query.ToVector();
                source.Clear();
                GC::Collect();
                if (result.Count != 1 || result[0].Value != 42) return 1;
                return 0;
            }
            """).Concat(new[] { "libc/GC.gf", "libc/TracingCollector.gf" }
                .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", name)))));
        Assert.Equal(0, CompilerTestHelper.Run(sources).ExitCode);
    }

    [Theory]
    [InlineData("foreach (int value in query) { break; }", "D")]
    [InlineData("query.First();", "D")]
    [InlineData("query.Any();", "D")]
    [InlineData("try { query.Where(Fail).Count(); } catch {}", "D")]
    [InlineData("try { query.Select(Throw).First(); } catch {}", "D")]
    public void IteratorResourcesAreReleasedOnShortCircuitAndExceptions(string statement, string output)
        => Run("""
            using std;
            extern int putchar(int c);
            struct Source {
                public struct Iterator {
                    private int n;
                    public Iterator() { n = 0; }
                    public bool MoveNext() { n++; return n <= 3; }
                    public int Current { get => n; }
                    ~Iterator() { putchar(68); }
                }
                public Source::Iterator GetEnumerator() => new Source::Iterator();
            }
            bool Fail(int n) { throw new Exception(); }
            int Throw(int n) { throw new Exception(); }
            int main() {
                var query = new Enumerable<int, Source, Source::Iterator>(new Source());
            """ + statement + " return 0; }", output);

    [Fact]
    public void QueriesBorrowVectorsIncludingPointerReceivers()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std;
            bool Even(int n) => n % 2 == 0;
            int Double(int n) => n * 2;
            int main() {
                Vector<int> values = new Vector<int>();
                for (int i = 1; i <= 6; i++) values.Add(i);
                Vector<int>* pointer = &values;
                var query = pointer.AsEnumerable().Where(Even).Select(Double).Skip(1).Take(1);
                if (query.First() != 8 || query.Count() != 1) return 1;
                values[3] = 20;
                if (query.First() != 40) return 2;
                Vector<int> copy = query.ToVector();
                if (copy.Count != 1 || copy[0] != 40 || values.Count != 6) return 3;
                return 0;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }
}
