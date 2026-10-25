namespace gflat.Tests;

public sealed class StandardLinkedListReverseTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ReversalPreservesBothLinksNodeValueIdentityAndNoncopyableOwnershipWithoutAllocation(int optimization)
        => Run("""
            using std; using std::Collections;
            private extern void*? malloc(nuint size);
            private extern void free(void* storage);
            int attempts = 0;
            int allocated = 0;
            int freed = 0;
            int made = 0;
            int dead = 0;
            bool deny = false;
            namespace Memory {
                public replace void*? Allocate(nuint size) {
                    attempts++; if (deny) return null;
                    void*? storage = malloc(size); if (storage != null) allocated++; return storage;
                }
                public replace void Free(void* storage) nothrow { freed++; free(storage); }
            }
            struct Owner {
                public int Id;
                public Owner(int id) { Id = id; made++; }
                ~Owner() { dead++; }
            }
            int main() {
                for (int length = 0; length <= 17; length++) {
                    LinkedList<Owner> list = new();
                    LinkedList<Owner>::Node*?[17] nodes;
                    Owner*?[17] values;
                    for (int id = 0; id < length; id++) {
                        LinkedList<Owner>::Node* node = list.AddLast(new Owner(id));
                        nodes[(nuint)id] = node; values[(nuint)id] = &node.Value;
                    }
                    int beforeAttempts = attempts; int beforeDead = dead;
                    deny = true; list.Reverse(); deny = false;
                    if (attempts != beforeAttempts || dead != beforeDead || list.Count != (nuint)length) return 1;
                    LinkedList<Owner>::Node*? node = list.First;
                    LinkedList<Owner>::Node*? previous = null;
                    for (int id = length - 1; id >= 0; id--) {
                        if (node == null || node != nodes[(nuint)id]) return 2;
                        if (node!.Value.Id != id || &node!.Value != values[(nuint)id] || node!.Previous != previous) return 3;
                        previous = node; node = node!.Next;
                    }
                    if (node != null || list.Last != previous) return 4;
                    node = list.Last; LinkedList<Owner>::Node*? next = null;
                    for (int id = 0; id < length; id++) {
                        if (node == null || node != nodes[(nuint)id] || node!.Next != next) return 5;
                        next = node; node = node!.Previous;
                    }
                    if (node != null || list.First != next) return 6;
                    deny = true; list.Reverse(); deny = false;
                    int expected = 0;
                    LinkedList<Owner>::Enumerator iterator = list.GetEnumerator();
                    while (iterator.MoveNext()) {
                        if (iterator.Current.Id != expected) return 7; expected++;
                    }
                    if (expected != length || attempts != beforeAttempts || dead != beforeDead) return 8;
                    list.Reverse(); LinkedList<Owner> moved = move list;
                    list = new(); list.Reverse();
                    if (!list.IsEmpty || list.First != null || list.Last != null) return 9;
                    if (length != 0) {
                        Owner removed = moved.Remove(nodes[(nuint)(length / 2)]!);
                        if (removed.Id != length / 2 || moved.Count != (nuint)(length - 1)) return 10;
                    }
                    moved.AddFirst(new Owner(100)); moved.AddLast(new Owner(200));
                    moved.Reverse();
                    if (moved.First!.Value.Id != 200 || moved.Last!.Value.Id != 100) return 11;
                    moved.Clear(); beforeAttempts = attempts; beforeDead = dead;
                    deny = true; moved.Reverse(); deny = false;
                    if (attempts != beforeAttempts || dead != beforeDead || !moved.IsEmpty || moved.First != null || moved.Last != null) return 12;
                }
                if (made != dead || allocated != freed) return 13;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ManagedValuesKeepRootsAcrossReversalAndMoveAndReleaseAfterRemovalAndClear(int optimization)
        => Run("""
            using std; using std::Collections;
            public static replace nuint std::TracingCollector::CollectionThreshold()=>0;
            void Populate(ref LinkedList<int^> values) {
                for (int id = 0; id < 17; id++) values.AddLast(new^ int(id));
            }
            bool Check(LinkedList<int^>* values, bool reversed) {
                LinkedList<int^>::Node*? node = values.First;
                LinkedList<int^>::Node*? previous = null;
                for (int index = 0; index < 17; index++) {
                    int expected = index; if (reversed) expected = 16 - index;
                    if (node == null || *node!.Value != expected || node!.Previous != previous) return false;
                    previous = node; node = node!.Next;
                }
                return node == null && values.Last == previous && values.Count == 17;
            }
            void RemoveMiddle(ref LinkedList<int^> values) {
                values.Remove(values.First!.Next!);
            }
            int main() {
                LinkedList<int^> values = new(); Populate(ref values);
                if (TracingCollector::ManagedCount != 17 || GC::Collect() != 0) return 1;
                values.Reverse();
                if (GC::Collect() != 0 || TracingCollector::ManagedCount != 17 || !Check(&values, true)) return 2;
                LinkedList<int^> moved = move values; values = new();
                moved.Reverse();
                if (GC::Collect() != 0 || !Check(&moved, false)) return 3;
                moved.Reverse();
                if (GC::Collect() != 0 || !Check(&moved, true)) return 4;
                RemoveMiddle(ref moved);
                if (GC::Collect() != 1 || TracingCollector::ManagedCount != 16 || moved.Count != 16) return 5;
                if (*moved.First!.Value != 16 || *moved.First!.Next!.Value != 14) return 6;
                moved.Clear();
                if (GC::Collect() != 16 || TracingCollector::ManagedCount != 0) return 7;
                return 0;
            }
            """, optimization, gc: true);

    private static void Run(string program, int optimization, bool gc = false)
    {
        var names = new List<string> {
            "core/Collections/IEnumerable.gf", "core/Queries/Enumerable.gf", "core/Collections/LinkedList.gf"
        };
        if (gc) names.AddRange(["libc/GC.gf", "libc/TracingCollector.gf"]);
        var sources = names.Select(name => new SourceFile(name,
                File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", name))))
            .Append(new SourceFile("linked-list-reverse.gf", program));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"LinkedList Reverse exit {result.ExitCode}: {result.StandardError}");
    }
}
