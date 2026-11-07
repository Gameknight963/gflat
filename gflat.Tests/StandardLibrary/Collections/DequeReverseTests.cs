namespace gflat.Tests.Std.Collections;

public sealed class DequeReverseTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EverySmallWrappedRangeReversesOwnersWithoutCopiesOrAllocation(int optimization)
    {
        var sources = StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/Collections/Deque.gf"))
            .Append(new SourceFile("deque-reverse.gf", """
                using Standard;
                using Standard::Collections;
                using Standard::Text;

                extern "C" void*? malloc(nuint bytes);
                extern "C" void free(void* storage);
                int attempts = 0;
                int live = 0;
                bool deny = false;
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
                    public String Text;
                    public Owner(int id) { Id = id; Text = s"owned"; live++; }
                    ~Owner() { live--; }
                }
                int main()
                {
                    for (nuint size = 0; size <= 17; size++)
                    for (nuint rotation = 0; rotation < 19; rotation++)
                    {
                        Deque<Owner> source = new();
                        source.Reserve(19);
                        for (nuint i = 0; i < size; i++) source.AddLast(new Owner((int)i));
                        if (size != 0)
                            for (nuint i = 0; i < rotation; i++) source.AddLast(source.RemoveFirst());
                        nuint capacity = source.Capacity;
                        for (nuint start = 0; start <= size; start++)
                        for (nuint length = 0; length <= size - start; length++)
                        {
                            int before = attempts;
                            deny = true;
                            source.Reverse(start, length);
                            if (attempts != before || source.Count != size || source.Capacity != capacity) return 1;
                            for (nuint i = 0; i < size; i++)
                            {
                                nuint original = i;
                                if (i >= start && i - start < length) original = start + length - 1 - (i - start);
                                if (source[i].Id != (int)((original + rotation) % size)) return 2;
                                if (!source[i].Text.Equals("owned")) return 3;
                            }
                            source.Reverse(start, length);
                            deny = false;
                        }
                        source.Reverse();
                        for (nuint i = 0; i < size; i++)
                            if (source[i].Id != (int)((size - 1 - i + rotation) % size)) return 4;
                        source.Reverse();
                        for (int mode = 0; mode < 3; mode++)
                        {
                            bool caught = false;
                            try
                            {
                                if (mode == 0) source.Reverse(size + 1, 0);
                                else if (mode == 1) source.Reverse(size, 1);
                                else source.Reverse(0, (nuint)0 - (nuint)1);
                            }
                            catch (Exception* error) { caught = true; }
                            if (!caught || source.Count != size || source.Capacity != capacity) return 5;
                            for (nuint i = 0; i < size; i++)
                                if (source[i].Id != (int)((i + rotation) % size)) return 6;
                        }
                        source.Clear();
                        if (live != 0) return 7;
                    }
                    return live;
                }
                """));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }

    [Fact]
    public void GuideCompiles() => StandardLibraryGuideTests.CheckProgram("DEQUE_REVERSE.md", 0);
}
