namespace gflat.Tests;

public sealed class StandardAdapterShrinkTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ShrinkingPreservesNoncopyableOwnersAndRemovalOrderWithoutComparisons(int optimization)
    {
        string checks = string.Join(Environment.NewLine, new[] { false, true }.Select(heap => $$$"""
            {
                {{{Declaration(heap)}}}
                values.Reserve(31);
                for (int i = 0; i < 7; i++) values.{{{Insert(heap)}}}(new Owner(i));
                for (int i = 0; i < 2; i++) {
                    Owner removed = values.{{{Remove(heap)}}}();
                    if (removed.Id != {{{Expected(heap, "i")}}} || *removed.Resource != removed.Id) return 1;
                }
                int oldDead = dead; int oldCalls = comparisons;
                values.ShrinkToFit();
                if (values.Count != 5 || values.Capacity != 5 || dead != oldDead || comparisons != oldCalls) return 2;
                int oldAttempts = attempts; int oldFreed = freed; deny = true;
                values.ShrinkToFit(); deny = false;
                if (attempts != oldAttempts || freed != oldFreed || comparisons != oldCalls) return 3;
                for (int i = 2; i < 7; i++) {
                    Owner removed = values.{{{Remove(heap)}}}();
                    if (removed.Id != {{{Expected(heap, "i")}}} || *removed.Resource != removed.Id) return 4;
                }
                oldAttempts = attempts; oldFreed = freed; oldCalls = comparisons; deny = true;
                values.ShrinkToFit(); deny = false;
                if (values.Count != 0 || values.Capacity != 0 || attempts != oldAttempts || freed != oldFreed + 1 || comparisons != oldCalls) return 5;
                oldFreed = freed; deny = true; values.ShrinkToFit(); deny = false;
                if (attempts != oldAttempts || freed != oldFreed) return 6;
            }
            """));
        Run(Common + $$$"""
            int main() {
                {{{checks}}}
                if (made != 14 || dead != made || allocated != freed) return 7;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FailedRelocationPreservesCapacityElementsAndExistingTopBorrow(int optimization)
    {
        string checks = string.Join(Environment.NewLine, new[] { false, true }.Select(heap => $$$"""
            {
                {{{Declaration(heap)}}}
                values.Reserve(31);
                for (int i = 0; i < 7; i++) values.{{{Insert(heap)}}}(new Owner(i));
                immutable(Owner)* top = &values.Top;
                nuint oldCapacity = values.Capacity;
                int oldDead = dead; int oldCalls = comparisons; bool caught = false;
                failNext = true;
                try { values.ShrinkToFit(); }
                catch (ref BadAllocException error) {
                    if (!CString::Equals(error.GetMessage(), "Vector allocation failed")) return 1;
                    caught = true;
                }
                if (!caught || failNext || values.Count != 7 || values.Capacity != oldCapacity || dead != oldDead || comparisons != oldCalls) return 2;
                if (top != &values.Top || top.Id != {{{(heap ? "0" : "6")}}} || *top.Resource != top.Id) return 3;
                for (int i = 0; i < 7; i++) {
                    Owner removed = values.{{{Remove(heap)}}}();
                    if (removed.Id != {{{Expected(heap, "i")}}} || *removed.Resource != removed.Id) return 4;
                }
            }
            """));
        Run(Common + $$$"""
            int main() {
                {{{checks}}}
                if (made != 14 || dead != made || allocated != freed) return 5;
                return 0;
            }
            """, optimization);
    }

    private static string Declaration(bool heap)
        => heap ? "PriorityQueue<Owner> values = new(Compare);" : "Stack<Owner> values = new();";
    private static string Insert(bool heap) => heap ? "Enqueue" : "Push";
    private static string Remove(bool heap) => heap ? "Dequeue" : "Pop";
    private static string Expected(bool heap, string index) => heap ? index : "6 - " + index;

    private static void Run(string code, int optimization)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("adapter-shrink.gf", code));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }

    private const string Common = """
        using std;
        bool deny = false; bool failNext = false;
        int attempts = 0; int allocated = 0; int freed = 0;
        int made = 0; int dead = 0; int comparisons = 0;
        extern void*? malloc(nuint count); extern void free(void* value);
        namespace Memory {
            public replace void*? Allocate(nuint count) {
                attempts++;
                if (failNext) { failNext = false; return null; }
                if (deny) return null;
                void*? result = malloc(count); if (result != null) allocated++;
                return result;
            }
            public replace void Free(void* value) nothrow { freed++; free(value); }
        }
        struct Owner {
            public int Id; public int* Resource;
            public Owner(int id) { Id = id; Resource = new* int(id); made++; }
            ~Owner() { dead++; delete Resource; }
        }
        int Compare(immutable(Owner)* first, immutable(Owner)* second) {
            comparisons++;
            if (first.Id < second.Id) return -1;
            if (first.Id > second.Id) return 1;
            return 0;
        }
        """;
}
