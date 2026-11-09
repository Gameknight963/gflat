namespace gflat.Tests.Std.Collections;

public sealed class DisjointSetGrowthTests
{
    private const string Counters = """
        using Standard;
        using Standard::Collections;

        extern "C" void*? malloc(nuint count);
        extern "C" void free(void* storage);
        int attempts = 0;
        int allocated = 0;
        int released = 0;
        int denied = 0;
        int failAfter = -1;
        namespace Memory
        {
            public replace void*? Allocate(nuint count)
            {
                attempts++;
                if (failAfter == 0)
                {
                    failAfter = -1;
                    denied++;
                    return null;
                }
                if (failAfter > 0) failAfter--;
                void*? storage = malloc(count);
                if (storage != null) allocated++;
                return storage;
            }
            public replace void Free(void* storage) nothrow
            {
                released++;
                free(storage);
            }
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AppendingPreservesExistingRepresentativesAndReusablePartitions(int optimization)
        => Run(Counters + """
            int main()
            {
                {
                    DisjointSet groups = new();
                    nuint[129] roots;
                    nuint[129] sizes;
                    for (nuint index = 0; index < 129; index++)
                    {
                        for (nuint old = 0; old < index; old++)
                        {
                            roots[old] = groups.Find(old);
                            sizes[old] = groups.SizeOf(old);
                        }
                        nuint setsBefore = groups.SetCount;
                        if (groups.Add() != index || groups.ElementCount != index + 1) return 1;
                        if (groups.SetCount != setsBefore + 1 || groups.Find(index) != index || groups.SizeOf(index) != 1) return 2;
                        for (nuint old = 0; old < index; old++)
                        {
                            if (groups.Find(old) != roots[old] || groups.SizeOf(old) != sizes[old]) return 3;
                            if (groups.Connected(old, index)) return 4;
                        }
                        if (index >= 5 && !groups.Union(index, index % 5)) return 5;
                    }
                    if (groups.SetCount != 5) return 6;
                    for (nuint first = 0; first < 129; first++)
                    {
                        nuint size = 0;
                        for (nuint second = 0; second < 129; second++)
                        {
                            bool connected = first % 5 == second % 5;
                            if (connected) size++;
                            if (groups.Connected(first, second) != connected) return 7;
                        }
                        if (groups.SizeOf(first) != size) return 8;
                    }
                    groups.Reset();
                    if (groups.ElementCount != 129 || groups.SetCount != 129) return 9;
                    for (nuint index = 0; index < 129; index++)
                        if (groups.Find(index) != index || groups.SizeOf(index) != 1) return 10;
                    if (groups.Add() != 129 || groups.SetCount != 130) return 11;
                }
                if (allocated != released) return 12;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryMeasuredAppendAllocationFailurePreservesThePartition(int optimization)
        => Run(Counters + """
            int main()
            {
                int points = 0;
                {
                    DisjointSet probe = new(4);
                    int before = attempts;
                    probe.Add();
                    points = attempts - before;
                }
                if (points == 0) return 1;
                for (int point = 0; point < points; point++)
                {
                    DisjointSet groups = new(4);
                    groups.Union(0, 3);
                    nuint root = groups.Find(0);
                    int live = allocated - released;
                    int deniedBefore = denied;
                    failAfter = point;
                    bool caught = false;
                    try { groups.Add(); }
                    catch (ref BadAllocException error) { caught = true; }
                    failAfter = -1;
                    if (!caught || denied != deniedBefore + 1 || allocated - released != live) return 2;
                    if (groups.ElementCount != 4 || groups.SetCount != 3 || groups.Find(0) != root) return 3;
                    if (!groups.Connected(0, 3) || groups.SizeOf(0) != 2) return 4;
                    if (groups.Find(1) != 1 || groups.Find(2) != 2) return 5;
                    if (groups.Add() != 4 || groups.ElementCount != 5 || groups.SetCount != 4) return 6;
                    if (groups.SizeOf(4) != 1 || groups.Connected(0, 4)) return 7;
                    if (!groups.Union(0, 4) || groups.SizeOf(3) != 3) return 8;
                }
                if (allocated != released) return 9;
                return 0;
            }
            """, optimization);

    private static void Run(string program, int optimization)
    {
        var result = CompilerTestHelper.Run(StandardTestSources.Collections.Append(
            new SourceFile("disjoint-set-growth.gf", program)), optimization);
        Assert.True(result.ExitCode == 0, $"DisjointSet growth exit {result.ExitCode}: {result.StandardError}");
    }
}
