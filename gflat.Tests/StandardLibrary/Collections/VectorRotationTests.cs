namespace gflat.Tests.Std.Collections;

public sealed class VectorRotationTests
{
    [Fact]
    public void GuideCompiles() => StandardLibraryGuideTests.CheckProgram("VECTOR_ROTATION.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EverySmallRangeAndSplitPermutesNoncopyableOwnersWithoutAllocating(int optimization)
    {
        var sources = StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/Collections/Comparer.gf", "core/Collections/span_algorithms.gf",
                "core/Collections/vector_ordering.gf"))
            .Append(new SourceFile("vector-rotation.gf", """
                using Standard;
                using Standard::Collections;
                using Standard::Text;

                extern "C" void*? malloc(nuint bytes);
                extern "C" void free(void* storage);
                int attempts = 0;
                int live = 0;
                int allocations = 0;
                int releases = 0;
                bool deny = false;
                namespace Memory
                {
                    public replace void*? Allocate(nuint bytes)
                    {
                        attempts++;
                        if (deny) return null;
                        void*? result = malloc(bytes);
                        if (result != null) allocations++;
                        return result;
                    }
                    public replace void Free(void* storage) nothrow { releases++; free(storage); }
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
                    for (nuint start = 0; start <= size; start++)
                    for (nuint length = 0; length <= size - start; length++)
                    for (nuint middle = 0; middle <= length; middle++)
                    {
                        {
                            Vector<Owner> source = new();
                            source.Reserve(32);
                            for (nuint i = 0; i < size; i++) source.Add(new Owner((int)i));
                            nuint capacity = source.Capacity;
                            int before = attempts;
                            deny = true;
                            source.Rotate(start, length, middle);
                            if (source.Count != size || source.Capacity != capacity || attempts != before || live != (int)size) return 1;
                            for (nuint i = 0; i < size; i++)
                            {
                                nuint expected = i;
                                if (i >= start && i - start < length) expected = start + (i - start + middle) % length;
                                if (source[i].Id != (int)expected || !source[i].Text.Equals("owned")) return 2;
                            }
                            Standard::Collections::Rotate<Owner>(&source, start, length, length - middle);
                            for (nuint i = 0; i < size; i++) if (source[i].Id != (int)i) return 3;
                            source.Rotate(middle);
                            Standard::Collections::Rotate<Owner>(&source, size - middle);
                            for (nuint i = 0; i < size; i++) if (source[i].Id != (int)i) return 4;
                            if (attempts != before) return 5;
                            deny = false;
                            if (start == 0 && length == 0)
                            {
                                for (int mode = 0; mode < 4; mode++)
                                {
                                    bool caught = false;
                                    try
                                    {
                                        if (mode == 0) source.Rotate(size + 1);
                                        else if (mode == 1) source.Rotate(size + 1, 0, 0);
                                        else if (mode == 2) source.Rotate(size, 1, 0);
                                        else source.Rotate(0, size, size + 1);
                                    }
                                    catch (Exception* error) { caught = true; }
                                    if (!caught || source.Count != size || source.Capacity != capacity) return 6;
                                    for (nuint i = 0; i < size; i++) if (source[i].Id != (int)i) return 7;
                                }
                            }
                        }
                        if (live != 0 || allocations != releases) return 8;
                    }
                    return 0;
                }
                """));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }
}
