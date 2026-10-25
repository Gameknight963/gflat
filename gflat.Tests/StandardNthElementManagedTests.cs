namespace gflat.Tests;

public sealed class StandardNthElementManagedTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CollectingComparatorsKeepEveryManagedIdentityRootedThroughSuccessAndFailure(int optimization)
    {
        string[] files = [
            "core/Collections/IEnumerable.gf", "core/Collections/Enumerable.gf",
            "core/Collections/Algorithms.gf", "core/Collections/NthElement.gf",
            "core/Collections/Vector.gf", "core/Span.gf", "libc/GC.gf", "libc/TracingCollector.gf"
        ];
        var sources = files.Select(file => new SourceFile("std/" + file,
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", file))))
            .Append(new SourceFile("nth-element-managed.gf", Program));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }

    private const string Program =
    """
    using std; using std::Collections;
    
    class Node
    {
        public int Value;
        public Node^? Next;
        public Node(int value) { Value = value; Next = null; }
    }
    
    Vector<Node^>*? watched = null;
    int calls = 0;
    int throwAt = 0;
    bool bad = false;
    
    int Compare(immutable(Node^)* left, immutable(Node^)* right)
    {
        // Collect between the algorithm's completed relocation steps.
        if (GC::Collect() != 0 || TracingCollector::ManagedCount != 32) bad = true;
        Span<immutable(Node^)> live = watched!.AsImmutable();
        bool[32] seen;
        for (nuint i = 0; i < 32; i++) seen[i] = false;
        for (nuint i = 0; i < live.Length; i++)
        {
            int value = live[i].Value;
            if (value < 0 || value >= 32) bad = true;
            else
            {
                if (seen[(nuint)value]) bad = true;
                seen[(nuint)value] = true;
                if (live[i].Next == null || live[i].Next!.Value != value) bad = true;
            }
        }
        calls++;
        if (calls == throwAt) throw new Exception("managed comparison failed", 37);
        if ((*left).Value < (*right).Value) return -1;
        if ((*left).Value > (*right).Value) return 1;
        return 0;
    }
    
    void Work(bool fail)
    {
        Vector<Node^> values = new();
        for (int i = 0; i < 32; i++)
        {
            Node^ node = new^ Node((i * 13) % 32);
            node.Next = node;
            values.Add(node);
        }
        watched = &values;
        calls = 0;
        throwAt = 0;
        bad = false;
        if (fail) throwAt = 7;
        if (GC::Collect() != 0 || TracingCollector::ManagedCount != 32)
            throw new Exception("initial roots");
        bool caught = false;
        try { NthElement<Node^>(values.AsSpan(), 16, Compare); }
        catch (ref Exception error)
        {
            if (!fail || error.GetCode() != 37) throw new Exception("wrong comparison error");
            caught = true;
        }
        if (caught != fail || bad || calls == 0 || values.Count != 32)
            throw new Exception("selection lifetime");
        bool[32] seen;
        for (nuint i = 0; i < 32; i++) seen[i] = false;
        for (nuint i = 0; i < 32; i++)
        {
            int value = values[i].Value;
            if (value < 0 || value >= 32) throw new Exception("invalid identity");
            if (seen[(nuint)value] || values[i].Next == null || values[i].Next!.Value != value)
                throw new Exception("lost identity");
            seen[(nuint)value] = true;
            if (!fail && i < 16 && value > 16) throw new Exception("left partition");
            if (!fail && i > 16 && value < 16) throw new Exception("right partition");
        }
        if (!fail && values[16].Value != 16) throw new Exception("selected rank");
        if (GC::Collect() != 0 || TracingCollector::ManagedCount != 32)
            throw new Exception("remaining roots");
        watched = null;
    }
    
    int main()
    {
        Work(false);
        if (GC::Collect() != 32 || TracingCollector::ManagedCount != 0) return 1;
        Work(true);
        if (GC::Collect() != 32 || TracingCollector::ManagedCount != 0) return 2;
        return 0;
    }
    """;
}
