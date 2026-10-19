namespace gflat.Tests;

public sealed class StandardQueryIteratorLifetimeTests
{
    private const string Source = """
        using std;
        private extern void*? malloc(nuint bytes);
        private extern void free(void* storage);
        int allocations = 0; int releases = 0;
        int attempted = 0; int created = 0; int destroyed = 0; int active = 0;
        int moves = 0; int reads = 0; int failure = 0;
        namespace Memory {
            public replace void*? Allocate(nuint bytes) {
                void*? result = malloc(bytes); if (result != null) allocations++; return result;
            }
            public replace void Free(void* storage) { releases++; free(storage); }
        }
        struct Source {
            public struct Iterator {
                private String token;
                private int index;
                public Iterator() {
                    attempted++;
                    token = s"iterator";
                    if (failure == 1) throw new Exception("iterator construction");
                    index = 0; created++; active++;
                }
                public bool MoveNext() {
                    moves++;
                    if (failure == 2 && moves == 2) throw new Exception("iterator advance");
                    if (index == 3) return false;
                    index++; return true;
                }
                public String Current {
                    get {
                        reads++;
                        String scratch = s"getter local";
                        if (failure == 3) throw new Exception("iterator current");
                        if (index == 1) return s"one";
                        if (index == 2) return s"two";
                        return s"three";
                    }
                }
                ~Iterator() { destroyed++; active--; }
            }
            public Source::Iterator GetEnumerator() => new Source::Iterator();
        }
        bool Keep(String value) => true;
        String Identity(String value) => move value;
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void TakeZeroCleansConstructedIteratorsWithoutAdvancingOrReading(int optimization)
        => Run(Source + """
            int main() {
                var query = new Enumerable<String, Source, Source::Iterator>(new Source()).Where(Keep).Select(Identity).Skip(0);
                int baseline = allocations - releases;
                for (int i = 0; i < 5; i++) {
                    if (query.Take(0).Any() || query.Take(0).Count() != 0) return 1;
                    query.Take(0).ToVector();
                    if (active != 0 || created != destroyed || allocations - releases != baseline) return 2;
                }
                if (attempted != 15 || created != 15 || moves != 0 || reads != 0) return 3;
                return allocations - releases;
            }
            """, optimization);

    [Theory]
    [InlineData(0, 1)]
    [InlineData(0, 2)]
    [InlineData(0, 3)]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    [InlineData(2, 3)]
    public void IteratorFailuresReleasePartialConstructionAndEveryComposedOwner(int optimization, int failure)
        => Run(Source + """
            int main() {
                var query = new Enumerable<String, Source, Source::Iterator>(new Source()).Where(Keep).Select(Identity).Skip(0).Take(3);
                int baseline = allocations - releases;
            """ + $"failure = {failure};" + """
                for (int i = 0; i < 3; i++) {
                    moves = 0; reads = 0; bool caught = false;
                    try { query.ToVector(); } catch { caught = true; }
                    if (!caught || active != 0 || created != destroyed || allocations - releases != baseline) return 1;
                    if (attempted != i + 1) return 2;
                }
                failure = 0; moves = 0; reads = 0;
                {
                    Vector<String> result = query.ToVector();
                    if (result.Count != 3 || result[0][0] != 'o' || result[1][0] != 't' || result[2].Length != 5) return 3;
                    if (active != 0 || created != destroyed) return 4;
                }
                if (allocations - releases != baseline) return 5;
                return allocations - releases;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SimultaneousEnumeratorsHaveIndependentPositionsAndDestruction(int optimization)
        => Run(Source + """
            int main() {
                var query = new Enumerable<String, Source, Source::Iterator>(new Source()).Where(Keep).Select(Identity).Take(3);
                int baseline = allocations - releases;
                {
                    var first = query.GetEnumerator();
                    if (!first.MoveNext() || active != 1) return 1;
                    {
                        var second = query.GetEnumerator();
                        if (!second.MoveNext() || active != 2) return 2;
                        String a = first.Current; String b = second.Current;
                        if (a[0] != 'o' || b[0] != 'o' || a.Data == b.Data) return 3;
                        if (!first.MoveNext()) return 4;
                        String c = first.Current; String d = second.Current;
                        if (c[0] != 't' || d[0] != 'o') return 5;
                    }
                    if (active != 1 || destroyed != 1) return 6;
                    if (!first.MoveNext()) return 7;
                    String last = first.Current; if (last.Length != 5) return 8;
                }
                if (attempted != 2 || created != 2 || destroyed != 2 || active != 0 || allocations - releases != baseline) return 9;
                return allocations - releases;
            }
            """, optimization);

    private static void Run(string program, int optimization)
    {
        string[] files = ["Collections/IEnumerable.gf", "Collections/IEnumerator.gf", "Collections/Enumerable.gf",
            "Collections/EnumerableExtensions.gf", "Collections/WhereSource.gf", "Collections/SelectSource.gf", "Collections/SliceSource.gf",
            "Vector.gf", "Span.gf", "String.gf", "IStringConvertable.gf", "IHashable.gf", "IEquatable.gf", "IComparable.gf", "Hashing.gf"];
        var sources = files.Select(file => new SourceFile(file, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", file))))
            .Append(new SourceFile("iterator.gf", program));
        string directory = Path.Combine(Path.GetTempPath(), "gflat_query_iterator_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string ir = Path.Combine(directory, "iterator.ll"), executable = Path.Combine(directory, "iterator.exe");
            File.WriteAllText(ir, Compiler.Emit(sources, options: new() { InternalizeFunctions = true }));
            var compilation = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
            Assert.True(compilation.ExitCode == 0, compilation.StandardError);
            var result = NativeToolchain.Run(executable, []);
            Assert.True(result.ExitCode == 0, $"Native query iterator exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }
        finally { Directory.Delete(directory, true); }
    }
}
