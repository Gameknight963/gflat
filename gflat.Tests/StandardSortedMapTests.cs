using System.Text;
using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class StandardSortedMapTests
{
    private const string Prelude = """
        using std; using std::Memory; using std::Numerics; using std::Queries; using std::Collections;
        extern int puts(immutable(char*) text);
        int made = 0; int dead = 0; int budget = -1;
        struct Owned {
            public int Value;
            public String Token;
            public Owned(int value) { Value = value; Token = s"owner"; made++; }
            ~Owned() { dead++; }
        }
        int Compare(immutable(Owned)* left, immutable(Owned)* right) {
            if (budget == 0) throw new Exception("comparison failed");
            if (budget > 0) budget--;
            if (left.Value < right.Value) return -1;
            if (left.Value > right.Value) return 1;
            return 0;
        }
        bool Matches(SortedMap<Owned,Owned>* map, readonly(int)* expected, nuint count) {
            if (map.Count != count || map.IsEmpty != (count == 0) || map.Capacity < count) return false;
            immutable(SortedMap<Owned,Owned>)* read = map;
            var iterator = read.GetEnumerator();
            for (nuint i = 0; i < count; i++) {
                if (!iterator.MoveNext()) return false;
                if (iterator.Current.Key.Value != expected[i * 2] || iterator.Current.ValueImmutable.Value != expected[i * 2 + 1]) return false;
                if (iterator.Current.Key.Token.Length != 5 || iterator.Current.ValueImmutable.Token.Length != 5) return false;
                if (!read.ContainsKey(iterator.Current.Key) || read.FindImmutable(iterator.Current.Key) == null) return false;
                Owned*? found = map.Find(iterator.Current.Key);
                if (found == null || found!.Value != expected[i * 2 + 1]) return false;
            }
            return !iterator.MoveNext();
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OwningMixedTraceMatchesSortedDictionary(int optimization)
    {
        var random = new Random(10037);
        var reference = new SortedDictionary<int, int>();
        var source = new StringBuilder(Prelude).AppendLine("int main() { { SortedMap<Owned,Owned> map = new(Compare); Owned probe = new(0);");
        int made = 1;
        for (int step = 0; step < 220; step++)
        {
            int key = random.Next(40), value = random.Next(-100, 101), operation = random.Next(100);
            source.AppendLine($"probe.Value = {key};");
            if (operation < 35)
            {
                reference[key] = value; made += 2;
                source.AppendLine($"map.Set(new Owned({key}),new Owned({value}));");
            }
            else if (operation < 59)
            {
                bool added = reference.TryAdd(key, value); made += 2;
                Check($"map.TryAdd(new Owned({key}),new Owned({value})) == {Boolean(added)}", $"insert {step}");
            }
            else if (operation < 77)
            {
                bool removed = reference.Remove(key);
                Check($"map.Remove(&probe) == {Boolean(removed)}", $"remove {step}");
            }
            else if (operation < 89)
            {
                source.AppendLine("{ Owned*? found = map.Find(&probe);");
                if (reference.ContainsKey(key)) { reference[key] = value; Check("found != null", $"find {step}"); source.AppendLine($"found!.Value = {value};"); }
                else Check("found == null && !map.ContainsKey(&probe)", $"missing {step}");
                source.AppendLine("}");
            }
            else if (operation < 97) source.AppendLine($"map.Reserve({random.Next(1, 80)});");
            else { reference.Clear(); source.AppendLine("map.Clear();"); }
            int[] expected = reference.SelectMany(pair => new[] { pair.Key, pair.Value }).ToArray();
            source.AppendLine($"{{ int[{Math.Max(1, expected.Length)}] expected = [" + string.Join(",", expected.Length == 0 ? [0] : expected) + "];");
            Check($"Matches(&map,(readonly(int)*)expected,{reference.Count})", $"state {step}");
            source.AppendLine("}");
            Check($"made == {made} && dead == {made - reference.Count * 2 - 1}", $"owners {step}");
        }
        source.AppendLine("} return made - dead; }");
        Run(source.ToString(), optimization);

        void Check(string condition, string label)
            => source.AppendLine($"if (!({condition})) {{ puts(\"{label}\"); return 1; }}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ThrowingComparisonsPreserveEntriesAndDestroyUnusedArguments(int optimization)
    {
        var source = new StringBuilder(Prelude).AppendLine("""
            int main() { { SortedMap<Owned,Owned> map = new(Compare);
                for (int i = 0; i < 8; i++) map.Add(new Owned(i),new Owned(i * 10));
                Owned probe = new(3); immutable(SortedMap<Owned,Owned>)* read = &map;
                int[16] expected = [0,0,1,10,2,20,3,30,4,40,5,50,6,60,7,70];
            """);
        string[] calls = ["map.ContainsKey(&probe)", "map.Find(&probe)", "read.FindImmutable(&probe)", "map.Remove(&probe)",
            "map[&probe]", "map.TryAdd(new Owned(3),new Owned(99))", "map.Add(new Owned(3),new Owned(99))", "map.Set(new Owned(3),new Owned(99))"];
        foreach (int budget in new[] { 0, 1, 2, 3 })
        foreach (string call in calls)
        {
            source.AppendLine($"{{ budget = {budget}; bool caught = false; try {{ {call}; }} catch {{ caught = true; }} budget = -1;");
            source.AppendLine("if (!caught || made - dead != 17 || !Matches(&map,(readonly(int)*)expected,8)) return 1; }");
        }
        source.AppendLine("""
                budget = 0; map.Reserve(80); budget = -1;
                if (!Matches(&map,(readonly(int)*)expected,8)) return 2;
                bool duplicate = false; try { map.Add(new Owned(3),new Owned(999)); } catch { duplicate = true; }
                if (!duplicate || made - dead != 17) return 3;
                probe.Value = 100; bool missing = false; try { map[&probe]; } catch { missing = true; }
                if (!missing || map.Remove(&probe) || map.Find(&probe) != null) return 4;
            } return made - dead; }
            """);
        Run(source.ToString(), optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FailedInsertionAllocationKeepsOwnersAndBorrowedValuesValid(int optimization)
        => Run(Prelude + """
            private extern void*? malloc(nuint bytes);
            private extern void free(void* storage);
            int allocations = 0; int releases = 0; bool fail = false;
            namespace Memory {
                public replace void*? Allocate(nuint bytes) {
                    if (fail) return null;
                    void*? result = malloc(bytes); if (result != null) allocations++; return result;
                }
                public replace void Free(void* storage) nothrow { releases++; free(storage); }
            }
            int main() { {
                SortedMap<Owned,Owned> map = new(Compare); map.Reserve(1);
                map.Add(new Owned(10),new Owned(20)); Owned probe = new(10);
                Owned* saved = map.Find(&probe)!;
                Owned incomingKey = new(5); Owned incomingValue = new(30);
                fail = true; bool caught = false;
                try { map.Add(move incomingKey,move incomingValue); } catch { caught = true; }
                fail = false;
                if (!caught || map.Count != 1 || map.Find(&probe) != saved || saved.Value != 20 || made - dead != 3) return 1;
                nuint capacity = map.Capacity; caught = false; fail = true;
                try { map.Reserve(100); } catch { caught = true; }
                fail = false;
                if (!caught || map.Capacity != capacity || map.Find(&probe) != saved || made - dead != 3) return 2;
                map.Add(new Owned(5),new Owned(30)); map.Add(new Owned(15),new Owned(40));
                probe.Value = 10; if (map[&probe].Value != 20) return 3;
                map.Clear(); map.Clear(); if (map.Count != 0 || made - dead != 1) return 4;
            }
            if (made != dead || allocations != releases) return 5;
            return 0; }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ComparerEquivalenceKeepsOriginalKeysAndMovesPreserveOwnership(int optimization)
        => Run(Prelude + """
            int AbsoluteCompare(immutable(Owned)* a, immutable(Owned)* b) {
                int left = a.Value; int right = b.Value;
                if (left < 0) left = -left; if (right < 0) right = -right;
                if (left < right) return -1; if (left > right) return 1; return 0;
            }
            int ReverseCompare(immutable(Owned)* a, immutable(Owned)* b) => Compare(b,a);
            int main() { {
                SortedMap<Owned,Owned> original = new(AbsoluteCompare);
                original.Add(new Owned(-5),new Owned(10));
                if (original.TryAdd(new Owned(5),new Owned(99)) || made - dead != 2) return 1;
                original.Set(new Owned(5),new Owned(20));
                var iterator = original.GetEnumerator(); if (!iterator.MoveNext() || iterator.Current.Key.Value != -5 || iterator.Current.ValueImmutable.Value != 20) return 2;
                SortedMap<Owned,Owned> moved = move original;
                Owned probe = new(5); moved[&probe].Value = 30;
                if (moved.FindImmutable(&probe)!.Value != 30) return 3;
                original = new SortedMap<Owned,Owned>(ReverseCompare);
                original.Add(new Owned(2),new Owned(2)); original.Add(new Owned(1),new Owned(1)); original.Add(new Owned(3),new Owned(3));
                var reverse = original.GetEnumerator(); int expected = 3; int invalid = 0;
                try { reverse.Current; } catch { invalid++; }
                while (reverse.MoveNext()) { if (reverse.Current.Key.Value != expected) return 4; expected--; }
                try { reverse.Current; } catch { invalid++; }
                if (expected != 0 || invalid != 2 || moved.Count != 1 || made - dead != 9) return 5;
            } return made - dead; }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ManagedValuesStayRootedAcrossShiftsGrowthAndRemoval(int optimization)
        => Run("""
            using std; using std::Memory; using std::Numerics; using std::Queries; using std::Collections;
            class Node { public int Value; public Node^? Next; public Node(int value) { Value = value; } }
            int Compare(readonly(int)* a, readonly(int)* b) => *a - *b;
            void Fill(SortedMap<int,Node^>* map) {
                for (int i = 0; i < 32; i++) {
                    Node^ value = new^ Node(i); value.Next = value;
                    map.Add(31 - i,value); GC::Collect();
                }
            }
            int main() {
                SortedMap<int,Node^> map = new(Compare); Fill(&map); map.Reserve(1000); GC::Collect();
                if (TracingCollector::ManagedCount != 32) return 1;
                for (int i = 0; i < 32; i++) if (map[&i].Value != 31-i) return 2;
                for (int i = 0; i < 16; i++) map.Remove(&i);
                GC::Collect(); if (TracingCollector::ManagedCount != 16) return 3;
                map.Clear(); GC::Collect(); if (TracingCollector::ManagedCount != 0) return 4;
                return 0;
            }
            """, optimization, managed: true);

    [Theory]
    [InlineData("iterator.Current.Key.Value = 99;")]
    [InlineData("iterator.Current.Value.Value = 99;")]
    [InlineData("SortedMap<Owned,Owned> copied = map;")]
    public void BorrowedEntriesCannotMutateKeysOrCopyOwners(string invalid)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Sources(Prelude + """
            int main() {
                SortedMap<Owned,Owned> map = new(Compare); map.Add(new Owned(1),new Owned(2));
                var iterator = map.GetEnumerator(); iterator.MoveNext();
            """ + invalid + "return 0; }")));
        Assert.True(error.Message.Contains("readonly", StringComparison.OrdinalIgnoreCase) || error.Message.Contains("copy", StringComparison.OrdinalIgnoreCase), error.Message);
    }

    private static string Boolean(bool value) => value ? "true" : "false";

    private static IEnumerable<SourceFile> Sources(string program, bool managed = false)
    {
        string[] files = ["Collections/IEnumerable.gf", "Queries/Enumerable.gf", "Collections/Vector.gf", "Memory/Span.gf", "Collections/SortedMap.gf",
            "IStringConvertable.gf", "IHashable.gf", "IEquatable.gf", "IComparable.gf", "Hashing/primitive_hashing.gf", "Text/String.gf", "Text/ByteSearch.gf"];
        IEnumerable<SourceFile> sources = files.Select(file => new SourceFile(file, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", file))));
        if (managed) sources = sources.Concat(new[] { "GC.gf", "TracingCollector.gf" }
            .Select(file => new SourceFile(file, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/libc", file)))));
        return sources.Append(new SourceFile("sorted.gf", program));
    }

    private static void Run(string program, int optimization, bool managed = false)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat_sorted_map_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string ir = Path.Combine(directory, "sorted.ll"), executable = Path.Combine(directory, "sorted.exe");
            File.WriteAllText(ir, Compiler.Emit(Sources(program, managed), options: new() { InternalizeFunctions = true }));
            var compilation = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
            Assert.True(compilation.ExitCode == 0, compilation.StandardError);
            var result = NativeToolchain.Run(executable, []);
            Assert.True(result.ExitCode == 0, $"Native sorted map exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }
        finally { Directory.Delete(directory, true); }
    }
}
