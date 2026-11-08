namespace gflat.Tests.Std.Collections;

public sealed class DequeRemoveAllTests
{
    private static void Run(string code, int optimization)
    {
        var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/Collections/Deque.gf"))
            .Append(new SourceFile("deque-remove-all.gf", code)), optimization);
        Assert.True(result.ExitCode == 0, $"Deque RemoveAll exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Fact]
    public void GuideCompiles() => StandardLibraryGuideTests.CheckProgram("DEQUE_REMOVE_ALL.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EverySmallRemovalMaskAndRingHeadPreserveStableOrderWithoutAllocation(int optimization)
        => Run(Tracking + """
            int mask = 0;
            int calls = 0;
            bool Match(immutable(int)* value)
            {
                if (*value != calls) throw new Exception("wrong callback order");
                calls++;
                return (mask & (1 << *value)) != 0;
            }
            int main()
            {
                for (nuint capacity = 1; capacity <= 10; capacity++)
                for (nuint head = 0; head < capacity; head++)
                for (nuint count = 0; count <= capacity && count <= 8; count++)
                for (mask = 0; mask < 1 << (int)count; mask++)
                {
                    Deque<int> values = new();
                    values.Reserve(capacity);
                    for (nuint i = 0; i < capacity; i++) values.AddLast(-1);
                    for (nuint i = 0; i < head; i++) { int removed = values.RemoveFirst(); values.AddLast(-1); }
                    while (values.Count > count) { int removed = values.RemoveLast(); }
                    for (nuint i = 0; i < count; i++) values[i] = (int)i;
                    int before = attempts;
                    calls = 0; deny = true;
                    nuint removed = values.RemoveAll(Match);
                    deny = false;
                    nuint kept = 0;
                    for (nuint i = 0; i < count; i++)
                    {
                        if ((mask & (1 << (int)i)) == 0)
                        {
                            if (values[kept] != (int)i) return 1;
                            kept++;
                        }
                    }
                    if (values.Count != kept || removed != count - kept || calls != (int)count || attempts != before || values.Capacity != capacity) return 2;
                    values.AddFirst(99);
                    if (values.RemoveFirst() != 99) return 3;
                    values.AddLast(100);
                    if (values.RemoveLast() != 100) return 4;
                }
                return live;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NoncopyableOwnersRemainInitializedAndPredicateFailuresCommitOnlyCompletedRemovals(int optimization)
        => Run(Tracking + """
            int dead = 0;
            int calls = 0;
            int failAt = -1;
            bool bad = false;
            Deque<Owner>*? watched = null;
            struct Owner
            {
                public int* Data;
                public Owner(int id) { Data = new* int(id); }
                ~Owner() { dead++; delete Data; }
            }
            bool Match(immutable(Owner)* value)
            {
                int index = calls++;
                if (*value.Data != index || dead != 0) bad = true;
                int sum = 0;
                for (nuint i = 0; i < watched!.Count; i++) sum += *(*watched!)[i].Data;
                if (sum != 45) bad = true;
                if (index == failAt) throw new Exception("predicate", 77);
                return index % 3 == 1;
            }
            int main()
            {
                for (nuint head = 0; head < 12; head++)
                for (failAt = -1; failAt < 10; failAt++)
                {
                    {
                        Deque<Owner> values = new();
                        values.Reserve(12);
                        for (int i = 0; i < 12; i++) values.AddLast(new Owner(-1));
                        for (nuint i = 0; i < head; i++) { Owner removed = values.RemoveFirst(); values.AddLast(new Owner(-1)); }
                        while (values.Count > 10) { Owner removed = values.RemoveLast(); }
                        for (nuint i = 0; i < 10; i++) *values[i].Data = (int)i;
                        dead = 0; calls = 0; bad = false; watched = &values;
                        int baseline = live;
                        nuint removedCount = 0;
                        bool caught = false;
                        try { removedCount = values.RemoveAll(Match); }
                        catch (Exception* error) { if (error.GetCode() != 77) return 1; caught = true; }
                        if (caught != (failAt >= 0) || bad || values.Capacity != 12) return 2;
                        int expectedCalls = 10;
                        if (failAt >= 0) expectedCalls = failAt + 1;
                        if (calls != expectedCalls) return 3;
                        nuint kept = 0;
                        int removed = 0;
                        for (int i = 0; i < 10; i++)
                        {
                            if (i % 3 == 1 && (failAt < 0 || i < failAt)) removed++;
                            else
                            {
                                if (*values[kept].Data != i) return 4;
                                kept++;
                            }
                        }
                        if (values.Count != kept || dead != removed || live != baseline - removed) return 5;
                        if (failAt < 0 && removedCount != (nuint)removed) return 6;
                        values.AddFirst(new Owner(50));
                        { Owner item = values.RemoveFirst(); if (*item.Data != 50) return 7; }
                        values.AddLast(new Owner(60));
                        { Owner item = values.RemoveLast(); if (*item.Data != 60) return 8; }
                    }
                    if (dead != 12 || live != 0) return 9;
                }
                return live;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PredicateAndDestructorCollectionsKeepEveryLiveManagedOwnerRooted(int optimization)
    {
        var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/Collections/Deque.gf", "libc/GC.gf", "libc/TracingCollector.gf"))
            .Append(new SourceFile("deque-remove-all-managed.gf", """
            using Standard;
            using Standard::Memory;
            using Standard::Queries;
            using Standard::Collections;

            class Node{public int Id;public Node^? Next;public Node(int id){Id=id;}}
            class PredicateFailure:Exception{public PredicateFailure():base("predicate failed",77){}}
            int dead=0;int calls=0;int failAt=-1;int expectedLive=12;bool bad=false;
            struct Owner{
                public Node^ Root;
                public Owner(int id){Root=new^ Node(id);Root.Next=Root;}
                ~Owner(){GC::Collect();if(TracingCollector::ManagedCount!=(nuint)expectedLive)bad=true;expectedLive--;dead++;}
            }
            bool Match(immutable(Owner)* owner){
                GC::Collect();if(TracingCollector::ManagedCount!=12 || dead!=0 || owner.Root.Id!=calls)bad=true;
                int index=calls++;if(index==failAt)throw new PredicateFailure();return owner.Root.Id%2==1;
            }
            bool Scenario(int failure){
                dead=0;calls=0;failAt=failure;expectedLive=12;
                {
                    Deque<Owner> values=new();for(int i=0;i<12;i++)values.AddLast(new Owner(i));
                    if(failure<0){if(values.RemoveAll(Match)!=6)return false;}
                    else{bool caught=false;try{values.RemoveAll(Match);}catch(PredicateFailure* error){caught=true;}if(!caught)return false;}
                    GC::Collect();if(TracingCollector::ManagedCount!=(nuint)expectedLive || bad)return false;
                    nuint kept=0;for(int i=0;i<12;i++)if(i%2==0 || failure>=0 && i>=failure){if(values[kept].Root.Id!=i)return false;kept++;}
                    if(values.Count!=kept)return false;
                }
                GC::Collect();return dead==12 && expectedLive==0 && TracingCollector::ManagedCount==0 && !bad;
            }
            int main(){if(!Scenario(-1))return 1;if(!Scenario(7))return 2;return 0;}
            """)), optimization);
        Assert.Equal(0, result.ExitCode);
    }

    private const string Tracking = """
        using Standard;
        using Standard::Collections;

        extern "C" void*? malloc(nuint bytes);
        extern "C" void free(void* storage);
        int live = 0;
        int attempts = 0;
        bool deny = false;
        namespace Memory
        {
            public replace void*? Allocate(nuint bytes)
            {
                attempts++;
                if (deny) return null;
                void*? result = malloc(bytes);
                if (result != null) live++;
                return result;
            }
            public replace void Free(void* storage) nothrow { live--; free(storage); }
        }
        """;
}
