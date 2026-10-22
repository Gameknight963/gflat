using System.Text;

namespace gflat.Tests;

public sealed class StandardHashSetRelationTests
{
    private const string Prelude = """
        using std;
        extern int puts(immutable(char*) message);
        int made = 0; int destroyed = 0; int allocations = 0; int releases = 0;
        bool failHash = false; bool failEquality = false;
        private extern void*? malloc(nuint bytes);
        private extern void free(void* storage);
        namespace Memory {
            public replace void*? Allocate(nuint bytes) { allocations++; return malloc(bytes); }
            public replace void Free(void* storage) { releases++; free(storage); }
        }
        struct Key : IHashable, IEquatable<Key> {
            public int Value;
            public String Label;
            public Key(int value) { Value = value; Label = s"owned"; made++; }
            ~Key() { destroyed++; }
            public immutable nuint GetHashCode() {
                if (failHash) throw new Exception("hash failed");
                return 1;
            }
            public immutable bool Equals(immutable(Key)* other) {
                if (failEquality) throw new Exception("equality failed");
                return Value == other.Value;
            }
        }
        """;

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public void ReadOnlyRelationsMatchDotNetWithoutCopyingOrAllocating(int optimization, bool collisions)
    {
        var random = new Random(73119);
        string prelude = collisions ? Prelude : Prelude.Replace("return 1;", "return (nuint)(Value * 17);", StringComparison.Ordinal);
        var source = new StringBuilder(prelude).AppendLine("int main() {");
        int created = 0;
        for (int scenario = 0; scenario < 80; scenario++)
        {
            int[] leftValues = scenario == 0 ? [] : Enumerable.Range(0, random.Next(24)).Select(_ => random.Next(16)).ToArray();
            int[] rightValues = scenario == 1 ? [] : (scenario % 4) switch
            {
                0 => leftValues,
                1 => leftValues.Where(value => value % 2 == 0).ToArray(),
                2 => Enumerable.Range(16, random.Next(12)).ToArray(),
                _ => Enumerable.Range(0, random.Next(24)).Select(_ => random.Next(16)).ToArray()
            };
            HashSet<int> left = [.. leftValues], right = [.. rightValues];
            source.AppendLine("{ HashSet<Key> left = new(); HashSet<Key> right = new();");
            foreach (int value in leftValues) source.AppendLine($"left.Add(new Key({value}));");
            foreach (int value in rightValues) source.AppendLine($"right.Add(new Key({value}));");
            created += leftValues.Length + rightValues.Length;
            source.AppendLine("immutable(HashSet<Key>)* a = &left; immutable(HashSet<Key>)* b = &right;");
            source.AppendLine("int beforeAllocations = allocations; int beforeReleases = releases;");
            Relations("a", "b", left, right, scenario);
            Relations("b", "a", right, left, scenario);
            Relations("a", "a", left, left, scenario);
            Check($"made == {created} && destroyed == {created - left.Count - right.Count}", $"keys unchanged {scenario}");
            Check("allocations == beforeAllocations && releases == beforeReleases", $"allocation-free {scenario}");
            source.AppendLine("}");
            Check($"made == {created} && destroyed == {created}", $"keys destroyed {scenario}");
        }
        Check("allocations == releases", "balanced allocations");
        source.AppendLine("return 0; }");
        Run(source.ToString(), optimization);

        void Relations(string a, string b, HashSet<int> left, HashSet<int> right, int scenario)
        {
            (string Method, bool Expected)[] relations = [
                ("SetEquals", left.SetEquals(right)), ("IsSubsetOf", left.IsSubsetOf(right)),
                ("IsSupersetOf", left.IsSupersetOf(right)), ("IsProperSubsetOf", left.IsProperSubsetOf(right)),
                ("IsProperSupersetOf", left.IsProperSupersetOf(right)), ("Overlaps", left.Overlaps(right))];
            foreach (var (method, expected) in relations)
                Check($"{a}.{method}({b}) == {(expected ? "true" : "false")}", $"{method} {a}/{b} {scenario}");
        }
        void Check(string condition, string label)
            => source.AppendLine($"if (!({condition})) {{ puts(\"{label}\"); return 1; }}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThrowingHashAndEqualityLeaveBothSetsIntact(bool equality)
    {
        string failFlag = equality ? "failEquality" : "failHash";
        var source = new StringBuilder(Prelude).AppendLine("""
            int main() { {
                HashSet<Key> smaller = new(); HashSet<Key> larger = new();
                smaller.Add(new Key(1)); smaller.Add(new Key(2));
                larger.Add(new Key(1)); larger.Add(new Key(2)); larger.Add(new Key(3));
                immutable(HashSet<Key>)* a = &smaller; immutable(HashSet<Key>)* b = &larger;
                int caught = 0;
            """);
        foreach (string call in new[] { "a.SetEquals(a)", "a.IsSubsetOf(b)", "b.IsSupersetOf(a)",
            "a.IsProperSubsetOf(b)", "b.IsProperSupersetOf(a)", "a.Overlaps(b)" })
        {
            source.AppendLine($"{failFlag} = true; try {{ bool ignored = {call}; }} catch (ref Exception error) {{ caught++; }} {failFlag} = false;");
            source.AppendLine("if (smaller.Count != 2 || larger.Count != 3 || made != 5 || destroyed != 0) return 1;");
            source.AppendLine("if (!a.IsSubsetOf(b) || !b.IsSupersetOf(a)) return 2;");
        }
        source.AppendLine("""
                if (caught != 6) return 3;
                HashSet<Key>::Enumerator iterator = a.GetEnumerator(); int sum = 0;
                while (iterator.MoveNext()) { sum += iterator.Current.Value; if (iterator.Current.Label.Length != 5) return 4; }
                if (sum != 3) return 5;
            }
            if (made != 5 || destroyed != 5 || allocations != releases) return 6;
            return 0; }
            """);
        Run(source.ToString(), 0);
    }

    private static void Run(string program, int optimization)
    {
        string[] files = ["IHashable.gf", "IEquatable.gf", "IComparable.gf", "Hashing.gf", "HashMap.gf", "HashSet.gf",
            "IStringConvertable.gf", "String.gf", "ByteSearch.gf"];
        var sources = files.Select(file => new SourceFile(file, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", file))))
            .Append(new SourceFile("relations.gf", program));
        string directory = Path.Combine(Path.GetTempPath(), "gflat_set_relations_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string ir = Path.Combine(directory, "relations.ll"), executable = Path.Combine(directory, "relations.exe");
            File.WriteAllText(ir, Compiler.Emit(sources, options: new() { InternalizeFunctions = true }));
            var compilation = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
            Assert.True(compilation.ExitCode == 0, compilation.StandardError);
            var result = NativeToolchain.Run(executable, []);
            Assert.True(result.ExitCode == 0, $"Native relations exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }
        finally { Directory.Delete(directory, true); }
    }
}
