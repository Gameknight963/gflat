using gflat.CompileExceptions;

namespace gflat.Tests.Std.Collections;

public sealed class HashSetRemoveWhereTests
{
    private const string Allocator = """
        using std; using std::Collections;
        private extern void*? malloc(nuint bytes);
        private extern void free(void* storage);
        bool denied = false;
        int attempts = 0;
        int allocations = 0;
        int releases = 0;
        namespace Memory {
            public replace void*? Allocate(nuint bytes) {
                attempts++;
                if (denied) return null;
                void*? result = malloc(bytes);
                if (result != null) allocations++;
                return result;
            }
            public replace void Free(void* storage) nothrow { releases++; free(storage); }
        }
        """;

    private const string Owners = """
        int made = 0;
        int dead = 0;
        int calls = 0;
        bool refuseHash = false;
        bool refuseEquality = false;
        struct Key : IHashable, IEquatable<Key> {
            public int Id;
            public String Label;
            public Key(int id) { Id = id; Label = s"owned"; made++; }
            ~Key() { dead++; }
            public immutable nuint GetHashCode() {
                if (refuseHash) throw new Exception("unexpected hash");
                return 1;
            }
            public immutable bool Equals(immutable(Key)* other) {
                if (refuseEquality) throw new Exception("unexpected equality");
                return Id == other.Id;
            }
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SeededRemovalMatchesHashSetOracleAndRetainsCapacityForReuse(int optimization)
    {
        var random = new Random(73142);
        var packet = new List<int>();
        for (int scenario = 0; scenario < 48; scenario++)
        {
            int[] values = Enumerable.Range(0, scenario % 25).Select(_ => random.Next(-35, 36)).ToArray();
            var oracle = values.ToHashSet();
            int originalCount = oracle.Count, divisor = scenario % 6 + 1;
            int removed = oracle.RemoveWhere(value => value % divisor == 0);
            packet.AddRange([scenario, divisor, values.Length, originalCount, removed, oracle.Count]);
            packet.AddRange(values);
            packet.AddRange(oracle.Order());
        }
        Run(Allocator + $$"""
            int divisor = 1;
            int calls = 0;
            bool Match(immutable(int)* value) { calls++; return *value % divisor == 0; }
            int main() {
                int[{{packet.Count}}] packet = [{{string.Join(',', packet)}}];
                nuint position = 0;
                while (position < {{packet.Count}}) {
                    int scenario = packet[position++]; divisor = packet[position++];
                    int inputCount = packet[position++];
                    int originalCount = packet[position++];
                    nuint removed = (nuint)packet[position++];
                    nuint expectedCount = (nuint)packet[position++];
                    HashSet<int> values = new(); values.Reserve(32);
                    for (int i = 0; i < inputCount; i++) values.Add(packet[position++]);
                    calls = 0;
                    nuint capacity = values.Capacity;
                    int before = attempts;
                    denied = true;
                    if (values.RemoveWhere(Match) != removed || values.Count != expectedCount || calls != originalCount) return scenario + 1;
                    if (values.Capacity != capacity || attempts != before) return scenario + 1;
                    denied = false;
                    for (nuint i = 0; i < expectedCount; i++) {
                        int key = packet[position++];
                        if (!values.Contains(&key)) return scenario + 1;
                    }
                    int added = 1000 + scenario;
                    if (!values.Add(added) || !values.Contains(&added) || !values.Remove(&added)) return scenario + 1;
                    if (values.Count != expectedCount) return scenario + 1;
                }
                if (allocations != releases) return 49;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CollidingNoncopyableKeysAreBorrowedWithoutAllocatingHashingOrEquality(int optimization)
        => Run(Allocator + Owners + """
            bool None(immutable(Key)* key) { calls++; return false; }
            bool Even(immutable(Key)* key) { calls++; return key.Id % 2 == 0; }
            bool All(immutable(Key)* key) { calls++; return true; }
            int main() {
                {
                    HashSet<Key> values = new(); values.Reserve(16);
                    for (int i = 0; i < 12; i++) values.Add(new Key(i));
                    nuint capacity = values.Capacity;
                    int before = attempts;
                    denied = true; refuseHash = true; refuseEquality = true;
                    if (values.RemoveWhere(None) != 0 || calls != 12 || dead != 0) return 1;
                    calls = 0;
                    if (values.RemoveWhere(Even) != 6 || calls != 12 || dead != 6 || values.Count != 6) return 2;
                    if (values.Capacity != capacity || attempts != before || made != 12) return 3;
                    var cursor = values.GetEnumerator();
                    while (cursor.MoveNext())
                        if (cursor.Current.Id % 2 == 0 || cursor.Current.Label.Length != 5) return 4;
                    calls = 0;
                    if (values.RemoveWhere(All) != 6 || calls != 6 || dead != 12 || !values.IsEmpty) return 5;
                    calls = 0;
                    if (values.RemoveWhere(All) != 0 || calls != 0 || attempts != before) return 6;
                    denied = false; refuseHash = false; refuseEquality = false;
                    values.Add(new Key(42));
                    if (values.Count != 1) return 7;
                }
                if (made != 13 || dead != 13 || allocations != releases) return 8;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EachPredicateFailureCommitsOnlyCompletedMatchesAndRetainsTheRest(int optimization)
        => Run(Allocator + Owners + """
            bool[12] visited;
            bool[12] removed;
            int failAt = 0;
            bool repeated = false;
            bool Match(immutable(Key)* key) {
                nuint id = (nuint)key.Id;
                if (visited[id]) repeated = true;
                visited[id] = true;
                int position = calls; calls++;
                if (position == failAt) throw new Exception("predicate failure", 73);
                bool result = key.Id % 3 != 1;
                removed[id] = result;
                return result;
            }
            int main() {
                for (int failure = 0; failure < 12; failure++) {
                    {
                        HashSet<Key> values = new(); values.Reserve(16);
                        for (int i = 0; i < 12; i++) {
                            visited[(nuint)i] = false; removed[(nuint)i] = false;
                            values.Add(new Key(i));
                        }
                        nuint capacity = values.Capacity;
                        int beforeDead = dead;
                        failAt = failure; calls = 0; repeated = false;
                        refuseHash = true; refuseEquality = true;
                        bool caught = false;
                        try { values.RemoveWhere(Match); }
                        catch (ref Exception error) { caught = error.GetCode() == 73; }
                        if (!caught || repeated || calls != failure + 1) return 1;
                        int expectedRemoved = 0;
                        for (nuint i = 0; i < 12; i++) if (removed[i]) expectedRemoved++;
                        if (values.Count != (nuint)(12 - expectedRemoved) || dead - beforeDead != expectedRemoved) return 2;
                        if (values.Capacity != capacity) return 3;
                        var cursor = values.GetEnumerator();
                        nuint seen = 0;
                        while (cursor.MoveNext()) {
                            if (removed[(nuint)cursor.Current.Id] || cursor.Current.Label.Length != 5) return 4;
                            seen++;
                        }
                        if (seen != values.Count) return 5;
                        refuseHash = false; refuseEquality = false;
                        for (int i = 0; i < 12; i++) {
                            Key probe = new Key(i);
                            if (values.Contains(&probe) == removed[(nuint)i]) return 6;
                        }
                        values.Add(new Key(99)); values.Clear();
                        if (!values.IsEmpty) return 7;
                    }
                    if (made != dead || allocations != releases) return 8;
                }
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RemovedManagedKeyFieldsReleaseTheirRootsWhileSurvivorsStayAlive(int optimization)
        => Run("""
            using std; using std::Collections;
            public static replace nuint std::TracingCollector::CollectionThreshold() => 0;
            class Node { public int Id; public Node^? Next; public Node(int id) { Id = id; } }
            struct Key : IHashable, IEquatable<Key> {
                public int Id;
                public immutable(Node^) Item;
                public Key(int id, Node^ item) { Id = id; Item = item; }
                public immutable nuint GetHashCode() => 1;
                public immutable bool Equals(immutable(Key)* other) => Id == other.Id;
            }
            void Populate(ref HashSet<Key> values) {
                for (int i = 0; i < 4; i++) {
                    Node^ node = new^ Node(i); node.Next = node;
                    values.Add(new Key(i, node));
                }
            }
            bool Even(immutable(Key)* key) => key.Id % 2 == 0;
            int main() {
                HashSet<Key> values = new(); Populate(ref values);
                if (TracingCollector::ManagedCount != 4) return 1;
                if (values.RemoveWhere(Even) != 2 || values.Count != 2) return 2;
                if (GC::Collect() != 2 || TracingCollector::ManagedCount != 2) return 3;
                var cursor = values.GetEnumerator();
                while (cursor.MoveNext())
                    if (cursor.Current.Item.Id != cursor.Current.Id) return 4;
                values.Clear();
                if (GC::Collect() != 2 || TracingCollector::ManagedCount != 0) return 5;
                return 0;
            }
            """, optimization, managed: true);

    [Theory]
    [InlineData("bool Match(int* value) => true; void Bad(ref HashSet<int> values) { values.RemoveWhere(Match); }")]
    [InlineData("bool Match(immutable(int)* value) => true; void Bad(immutable(HashSet<int>)* values) { values.RemoveWhere(Match); }")]
    public void MutablePredicatesAndImmutableReceiversAreRejected(string body)
        => Assert.Throws<TypeCheckException>(() => Compiler.Check(Sources("using std; using std::Collections; " + body)));

    private static IEnumerable<SourceFile> Sources(string program, bool managed = false)
    {
        string[] files = ["IHashable.gf", "IEquatable.gf", "IComparable.gf", "Hashing.gf", "Collections/HashMap.gf", "Collections/HashSet.gf",
            "IStringConvertable.gf", "String.gf", "ByteSearch.gf"];
        IEnumerable<SourceFile> sources = files.Select(file => new SourceFile(file, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", file))));
        if (managed) sources = sources.Concat(new[] { "GC.gf", "TracingCollector.gf" }
            .Select(file => new SourceFile(file, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/libc", file)))));
        return sources.Append(new SourceFile("hashset-remove-where.gf", program));
    }

    private static void Run(string code, int optimization, bool managed = false)
    {
        var result = CompilerTestHelper.Run(Sources(code, managed), optimization);
        Assert.True(result.ExitCode == 0, $"HashSet RemoveWhere exit {result.ExitCode}: {result.StandardError}");
    }
}
