namespace gflat.Tests.Std.Collections;

public sealed class VectorFindAllTests
{
    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("VECTOR_FIND_ALL.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PredicateCopyAndAllocationFailuresCleanPartialOwners(int optimization)
    {
        var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/Collections/vector_find_all.gf"))
            .Append(new SourceFile("vector-find-all.gf", """
            using Standard;
            using Standard::Collections;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            int live = 0;
            int attempts = 0;
            int failAllocation = -1;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes)
                {
                    int attempt = attempts;
                    attempts++;
                    if (attempt == failAllocation) return null;
                    void*? result = malloc(bytes);
                    if (result != null) live++;
                    return result;
                }
                public replace void Free(void* storage) nothrow { live--; free(storage); }
            }
            int copies = 0;
            int destroyedCopies = 0;
            int predicateCalls = 0;
            int failCopy = -1;
            int failPredicate = -1;
            bool rejectAll = false;
            bool evensOnly = false;
            struct Item
            {
                public int Key;
                private bool copied;
                public Item(int key) { Key = key; copied = false; }
                public copy Item(immutable(Item)* source)
                {
                    if (source.Key == failCopy) throw new Exception("copy failed");
                    Key = source.Key;
                    copied = true;
                    copies++;
                }
                ~Item() { if (copied) destroyedCopies++; }
            }
            bool Select(immutable(Item)* value)
            {
                if (value.Key != predicateCalls) throw new Exception("predicate order");
                predicateCalls++;
                if (value.Key == failPredicate) throw new Exception("predicate failed");
                return !rejectAll && (!evensOnly || value.Key % 2 == 0);
            }
            Vector<Item> Filter(immutable(Vector<Item>)* source) => source.FindAll(Select);
            int main()
            {
                for (int mode = 0; mode < 5; mode++)
                for (int failure = 0; failure < 12; failure++)
                {
                    copies = 0; destroyedCopies = 0; predicateCalls = 0;
                    failCopy = -1; failPredicate = -1; failAllocation = -1;
                    evensOnly = mode == 4;
                    {
                        Vector<Item> source = new();
                        for (int i = 0; i < 12; i++) source.Add(new Item(i));
                        Vector<Item> target = new();
                        target.Add(new Item(99));
                        if (mode == 1) failCopy = failure;
                        if (mode == 4) failCopy = 1;
                        if (mode == 2) failPredicate = failure;
                        if (mode == 3 && failure < 3) failAllocation = attempts + failure;
                        int beforeLive = live;
                        bool caught = false;
                        try { target = Filter(&source); }
                        catch (Exception* error) { caught = true; }
                        failAllocation = -1;
                        bool shouldFail = mode == 1 || mode == 2 || (mode == 3 && failure < 3);
                        if (shouldFail)
                        {
                            if (!caught || target.Count != 1 || target[0].Key != 99 || live != beforeLive) return 1;
                            if (copies != destroyedCopies) return 2;
                        }
                        else
                        {
                            int expected = 12;
                            int step = 1;
                            if (mode == 4) { expected = 6; step = 2; }
                            if (caught || predicateCalls != 12 || target.Count != (nuint)expected || copies != expected) return 3;
                            for (nuint i = 0; i < target.Count; i++) if (target[i].Key != (int)i * step) return 4;
                            target[0].Key = 77;
                            if (source[0].Key != 0) return 5;
                        }
                        if (source.Count != 12) return 6;
                    }
                    if (copies != destroyedCopies || live != 0) return 7;
                }
                failCopy = -1; failPredicate = -1; failAllocation = -1;
                copies = 0; predicateCalls = 0; evensOnly = false;
                {
                    Vector<Item> empty = new();
                    int before = attempts;
                    Vector<Item> result = empty.FindAll(Select);
                    if (attempts != before || result.Count != 0 || predicateCalls != 0) return 8;
                    empty.Add(new Item(0));
                    rejectAll = true;
                    before = attempts;
                    result = FindAll<Item>(&empty, Select);
                    if (attempts != before || copies != 0 || result.Count != 0 || predicateCalls != 1) return 9;
                }
                if (live != 0) return 10;
                return 0;
            }
            """)), optimization);
        Assert.True(result.ExitCode == 0, $"FindAll exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
