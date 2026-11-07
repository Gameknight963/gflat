namespace gflat.Tests.Std.Collections;

public sealed class VectorRangeSearchTests
{
    [Fact]
    public void GuideCompiles() => StandardLibraryGuideTests.CheckProgram("VECTOR_RANGE_SEARCH.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EverySmallRangeReturnsAbsoluteIndicesAndValidatesBeforeCallbacks(int optimization)
    {
        var sources = StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/Collections/EqualityComparer.gf", "core/Collections/vector_search.gf"))
            .Append(new SourceFile("vector-range-search.gf", """
                using Standard;
                using Standard::Collections;

                extern "C" void*? malloc(nuint bytes);
                extern "C" void free(void* storage);
                int attempts = 0;
                bool deny = false;
                int live = 0;
                namespace Memory
                {
                    public replace void*? Allocate(nuint bytes)
                    {
                        attempts++;
                        if (deny) return null;
                        return malloc(bytes);
                    }
                    public replace void Free(void* storage) nothrow => free(storage);
                }
                struct Owner
                {
                    public int Id;
                    public Owner(int id) { Id = id; live++; }
                    ~Owner() { live--; }
                }
                bool EqualOwner(immutable(Owner)* a, immutable(Owner)* b) nothrow => a.Id == b.Id;
                int calls = 0;
                int failAt = -1;
                bool Equal(immutable(int)* left, immutable(int)* right)
                {
                    calls++;
                    if (calls == failAt) throw new Exception("comparison failed");
                    return *left == *right;
                }
                int main()
                {
                    for (nuint size = 0; size <= 16; size++)
                    {
                        Vector<int> source = new();
                        for (nuint i = 0; i < size; i++) source.Add((int)(i % 5) - 2);
                        for (nuint start = 0; start <= size; start++)
                        for (nuint count = 0; count <= size - start; count++)
                        for (int key = -3; key <= 3; key++)
                        {
                            nuint expected = size;
                            for (nuint i = start; i < start + count; i++)
                                if (source[i] == key) { expected = i; break; }
                            calls = 0;
                            if (source.IndexOf(&key, start, count, Equal) != expected) return 1;
                            nuint comparisons = count;
                            if (expected != size) comparisons = expected - start + 1;
                            if ((nuint)calls != comparisons) return 2;
                            if (source.IndexOf(&key, start, count) != expected) return 3;
                            if (Standard::Collections::IndexOf<int>(&source, &key, start, count) != expected) return 4;
                            if (count == size - start)
                            {
                                if (source.IndexOf(&key, start) != expected || source.IndexOf(&key, start, Equal) != expected) return 5;
                            }
                        }
                        int absent = 99;
                        for (int mode = 0; mode < 4; mode++)
                        {
                            calls = 0;
                            failAt = 1;
                            bool caught = false;
                            try
                            {
                                if (mode == 0) source.IndexOf(&absent, size + 1, Equal);
                                else if (mode == 1) source.IndexOf(&absent, size + 1, 0, Equal);
                                else if (mode == 2) source.IndexOf(&absent, size, 1, Equal);
                                else source.IndexOf(&absent, 0, (nuint)0 - (nuint)1, Equal);
                            }
                            catch (Exception* error) { caught = true; }
                            failAt = -1;
                            if (!caught || calls != 0 || source.Count != size) return 6;
                        }
                        for (int failure = 1; failure <= (int)size; failure++)
                        {
                            calls = 0; failAt = failure;
                            bool caught = false;
                            try { source.IndexOf(&absent, 0, size, Equal); }
                            catch (Exception* error) { caught = true; }
                            failAt = -1;
                            if (!caught || calls != failure || source.Count != size) return 7;
                        }
                        for (nuint i = 0; i < size; i++) if (source[i] != (int)(i % 5) - 2) return 8;
                    }
                    {
                        Vector<Owner> source = new();
                        source.Add(new Owner(1)); source.Add(new Owner(2)); source.Add(new Owner(1));
                        Owner key = new(1);
                        int before = attempts;
                        deny = true;
                        if (source.IndexOf(&key, 1, EqualOwner) != 2 || source.IndexOf(&key, 1, 1, EqualOwner) != 3) return 9;
                        if (source.IndexOf(&key, 3, 0, EqualOwner) != 3 || attempts != before || live != 4) return 10;
                        deny = false;
                        if (source[0].Id != 1 || source[1].Id != 2 || source[2].Id != 1) return 11;
                    }
                    if (live != 0) return 12;
                    return 0;
                }
                """));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }
}
