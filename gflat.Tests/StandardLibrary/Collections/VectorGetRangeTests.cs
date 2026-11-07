namespace gflat.Tests.Std.Collections;

public sealed class VectorGetRangeTests
{
    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("VECTOR_GET_RANGE.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RangesCopyIndependentlyAndFailuresReleaseTheirPrefixes(int optimization)
    {
        var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/Collections/vector_get_range.gf"))
            .Append(new SourceFile("vector-get-range.gf", """
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
            int copies = 0;
            int destroyed = 0;
            int failAt = -1;
            struct Item
            {
                public int Key;
                public Item(int key) { Key = key; }
                public copy Item(immutable(Item)* source)
                {
                    copies++;
                    if (source.Key == failAt) throw new Exception("copy failed");
                    Key = source.Key;
                }
                ~Item() { destroyed++; }
            }
            Vector<int> Snapshot(immutable(Vector<int>)* source, nuint start, nuint count)
                => source.GetRange(start, count);
            int main()
            {
                for (nuint length = 0; length <= 12; length++)
                {
                    Vector<int> source = new();
                    for (nuint i = 0; i < length; i++) source.Add((int)i * 7);
                    for (nuint start = 0; start <= length + 1; start++)
                    for (nuint count = 0; count <= length + 1; count++)
                    {
                        Vector<int> output = new();
                        output.Add(99);
                        int before = attempts;
                        bool valid = start <= length && count <= length - start;
                        bool caught = false;
                        try { output = Snapshot(&source, start, count); }
                        catch (Exception* error) { caught = true; }
                        if (!valid)
                        {
                            if (!caught || output.Count != 1 || output[0] != 99) return 1;
                        }
                        else
                        {
                            if (caught || output.Count != count || source.Count != length) return 2;
                            if (count == 0 && attempts != before) return 3;
                            for (nuint i = 0; i < count; i++) if (output[i] != (int)(start + i) * 7) return 4;
                            if (count != 0) { output[0] = -1; if (source[start] != (int)start * 7) return 5; }
                        }
                    }
                    nuint maximum = (nuint)0 - (nuint)1;
                    try { Vector<int> bad = source.GetRange(maximum, 1); return 6; }
                    catch (Exception* error) { }
                    source = source.GetRange(length / 2, length - length / 2);
                    for (nuint i = 0; i < source.Count; i++) if (source[i] != (int)(length / 2 + i) * 7) return 7;
                }
                {
                    Vector<Item> source = new();
                    Vector<Item> target = new();
                    for (int i = 0; i < 5; i++) source.Add(new Item(i));
                    target.Add(new Item(99));
                    bool invalidCaught = false;
                    try { target = source.GetRange(6, 0); }
                    catch (Exception* error) { invalidCaught = true; }
                    if (!invalidCaught || copies != 0 || destroyed != 0 || target[0].Key != 99) return 13;
                    for (int failure = 0; failure < 5; failure++)
                    {
                        failAt = failure;
                        int before = destroyed;
                        int beforeLive = live;
                        bool caught = false;
                        try { target = source.GetRange(0, 5); }
                        catch (Exception* error) { caught = true; }
                        if (!caught || destroyed != before + failure || live != beforeLive) return 8;
                        if (target.Count != 1 || target[0].Key != 99 || source.Count != 5) return 9;
                    }
                    failAt = -1;
                    deny = true;
                    int before = copies;
                    bool caught = false;
                    try { target = source.GetRange(0, 5); }
                    catch (Exception* error) { caught = true; }
                    deny = false;
                    if (!caught || copies != before || target[0].Key != 99) return 10;
                    {
                        Vector<Item> result = GetRange<Item>(&source, 1, 3);
                        if (copies != 18 || result.Count != 3 || result[0].Key != 1 || result[2].Key != 3) return 11;
                    }
                }
                if (destroyed != 19 || live != 0) return 12;
                return 0;
            }
            """)), optimization);
        Assert.True(result.ExitCode == 0, $"GetRange exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
