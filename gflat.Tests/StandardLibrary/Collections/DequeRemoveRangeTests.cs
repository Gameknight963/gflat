namespace gflat.Tests.Std.Collections;

public sealed class DequeRemoveRangeTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryWrappedRangePreservesSurvivorOrderAndDestroysOnlyRemovedOwners(int optimization)
    {
        var sources = StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/Collections/Deque.gf"))
            .Append(new SourceFile("deque-remove-range.gf", """
                using Standard;
                using Standard::Collections;
                using Standard::Text;

                extern "C" void*? malloc(nuint bytes);
                extern "C" void free(void* storage);
                int attempts = 0;
                int live = 0;
                int allocated = 0;
                int freed = 0;
                bool deny = false;
                namespace Memory
                {
                    public replace void*? Allocate(nuint bytes)
                    {
                        attempts++;
                        if (deny) return null;
                        void*? result = malloc(bytes);
                        if (result != null) allocated++;
                        return result;
                    }
                    public replace void Free(void* storage) nothrow { freed++; free(storage); }
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
                    for (nuint size = 0; size <= 13; size++)
                    for (nuint rotation = 0; rotation < 17; rotation++)
                    for (nuint start = 0; start <= size; start++)
                    for (nuint length = 0; length <= size - start; length++)
                    {
                        {
                            Deque<Owner> source = new();
                            source.Reserve(17);
                            for (nuint i = 0; i < size; i++) source.AddLast(new Owner((int)i));
                            if (size != 0)
                                for (nuint i = 0; i < rotation; i++) source.AddLast(source.RemoveFirst());
                            nuint capacity = source.Capacity;
                            int before = attempts;
                            deny = true;
                            source.RemoveRange(start, length);
                            deny = false;
                            if (attempts != before || source.Count != size - length || source.Capacity != capacity) return 1;
                            if (live != (int)(size - length)) return 2;
                            for (nuint i = 0; i < source.Count; i++)
                            {
                                nuint original = i;
                                if (i >= start) original += length;
                                if (source[i].Id != (int)((original + rotation) % size)) return 3;
                                if (!source[i].Text.Equals("owned")) return 4;
                            }
                            for (int mode = 0; mode < 3; mode++)
                            {
                                bool caught = false;
                                try
                                {
                                    if (mode == 0) source.RemoveRange(source.Count + 1, 0);
                                    else if (mode == 1) source.RemoveRange(source.Count, 1);
                                    else source.RemoveRange(0, (nuint)0 - (nuint)1);
                                }
                                catch (Exception* error) { caught = true; }
                                if (!caught || source.Count != size - length || live != (int)source.Count) return 5;
                            }
                            source.AddFirst(new Owner(101));
                            source.AddLast(new Owner(102));
                            if (source.RemoveFirst().Id != 101 || source.RemoveLast().Id != 102) return 6;
                            for (nuint i = 0; i < source.Count; i++)
                            {
                                nuint original = i;
                                if (i >= start) original += length;
                                if (source[i].Id != (int)((original + rotation) % size)) return 7;
                            }
                        }
                        if (live != 0 || allocated != freed) return 8;
                    }
                    return 0;
                }
                """));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }

    [Fact]
    public void GuideCompiles() => StandardLibraryGuideTests.CheckProgram("DEQUE_REMOVE_RANGE.md", 0);
}
