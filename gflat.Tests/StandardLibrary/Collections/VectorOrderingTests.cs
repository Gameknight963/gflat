namespace gflat.Tests.Std.Collections;

public sealed class VectorOrderingTests
{
    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("VECTOR_ORDERING.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NoncopyableOwnersRemainOwnedThroughComparatorFailures(int optimization)
    {
        var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/Collections/Comparer.gf", "core/Collections/span_algorithms.gf", "core/Collections/vector_ordering.gf"))
            .Append(new SourceFile("vector-ordering-owners.gf", """
            using Standard;
            using Standard::Collections;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            int attempts = 0;
            bool deny = false;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes) { attempts++; if (deny) return null; return malloc(bytes); }
                public replace void Free(void* storage) nothrow { free(storage); }
            }
            int calls = 0;
            int failAt = -1;
            struct Item
            {
                public int Key;
                private int* destroyed;
                public Item(int key, int* counts) { Key = key; destroyed = counts; }
                ~Item() { destroyed[Key]++; }
            }
            int Compare(immutable(Item)* left, immutable(Item)* right)
            {
                calls++;
                if (calls == failAt) throw new Exception("comparison failed");
                if (left.Key < right.Key) return -1;
                if (left.Key > right.Key) return 1;
                return 0;
            }
            int main()
            {
                int[12] destroyed = default(int[12]);
                {
                    Vector<Item> values = new();
                    values.Reserve(12);
                    for (int i = 0; i < 12; i++) values.Add(new Item(11 - i, &destroyed[0]));
                    int before = attempts;
                    deny = true;
                    values.Sort(Compare);
                    for (nuint i = 0; i < 12; i++) if (values[i].Key != (int)i) return 1;
                    values.Reverse();
                    values.Reverse(1, 10);
                    values.Sort(1, 10, Compare);
                    if (attempts != before || values[0].Key != 11 || values[11].Key != 0) return 2;
                    deny = false;
                    for (int failure = 1; failure <= 12; failure++)
                    {
                        calls = 0; failAt = failure;
                        bool caught = false;
                        try { values.Sort(Compare); }
                        catch (Exception* error) { caught = true; }
                        if (!caught || calls != failure || values.Count != 12) return 3;
                        bool[12] seen = default(bool[12]);
                        for (nuint i = 0; i < 12; i++)
                        {
                            int key = values[i].Key;
                            if (key < 0 || key >= 12 || seen[key] || destroyed[key] != 0) return 4;
                            seen[key] = true;
                        }
                    }
                }
                for (nuint i = 0; i < 12; i++) if (destroyed[i] != 1) return 5;
                return 0;
            }
            """)), optimization);
        Assert.True(result.ExitCode == 0, $"Ordering owners exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AllSmallRangesMatchArrayReverseAndSortWithoutChangingStorage(int optimization)
    {
        string path = Path.Combine(Path.GetTempPath(), "gflat_vector_ordering_" + Guid.NewGuid().ToString("N"));
        try
        {
            var random = new Random(278441);
            int[] pool = [int.MinValue, -2, -1, 0, 1, 2, int.MaxValue];
            using (var writer = new BinaryWriter(File.Create(path)))
            for (int length = 0; length <= 10; length++)
            for (int start = 0; start <= length + 1; start++)
            for (int count = 0; count <= length + 1; count++)
            {
                int[] original = Enumerable.Range(0, length).Select(_ => pool[random.Next(pool.Length)]).ToArray();
                int[] reversed = (int[])original.Clone();
                int[] sorted = (int[])original.Clone();
                bool valid = start <= length && count <= length - start;
                if (valid) { Array.Reverse(reversed, start, count); Array.Sort(sorted, start, count); }
                foreach (int header in new[] { length, start, count, valid ? 1 : 0 }) writer.Write(header);
                foreach (int[] items in new[] { original, reversed, sorted })
                    for (int i = 0; i < 12; i++) writer.Write(i < length ? items[i] : 0);
            }
            string literal = "\"" + path.Replace("\\", "\\\\") + "\"";
            var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
                StandardTestSources.Load("core/Collections/Comparer.gf", "core/Collections/span_algorithms.gf", "core/Collections/vector_ordering.gf"))
                .Append(new SourceFile("vector-ordering.gf", $$"""
                using Standard;
                using Standard::Collections;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" nuint fread(void* buffer, nuint size, nuint count, void* file);
                extern "C" int fclose(void* file);
                extern "C" int ferror(void* file);
                int calls = 0;
                int Compare(immutable(int)* left, immutable(int)* right)
                {
                    calls++;
                    if (*left < *right) return -1;
                    if (*left > *right) return 1;
                    return 0;
                }
                int main()
                {
                    void*? file = fopen({{literal}}, "rb");
                    if (file == null) return 1;
                    defer { fclose(file!); }
                    int[40] row;
                    Vector<int> values = new();
                    values.Reserve(12);
                    while (true)
                    {
                        nuint received = fread(&row[0], 1, 160, file!);
                        if (received == 0) break;
                        if (received != 160) return 2;
                        for (int form = 0; form < 3; form++)
                        {
                            values.Clear();
                            for (int i = 0; i < row[0]; i++) values.Add(row[4 + i]);
                            int*? before = values.AsSpan().Data;
                            nuint capacity = values.Capacity;
                            calls = 0;
                            bool caught = false;
                            try
                            {
                                if (form == 0) values.Reverse((nuint)row[1], (nuint)row[2]);
                                else if (form == 1) values.Sort((nuint)row[1], (nuint)row[2]);
                                else values.Sort((nuint)row[1], (nuint)row[2], Compare);
                            }
                            catch (Exception* error) { caught = true; }
                            if (caught != (row[3] == 0)) return 3;
                            if (values.Count != (nuint)row[0] || values.Capacity != capacity || values.AsSpan().Data != before) return 4;
                            int expected = 4;
                            if (row[3] != 0)
                            {
                                expected = 16;
                                if (form != 0) expected = 28;
                            }
                            for (nuint i = 0; i < values.Count; i++)
                                if (values[i] != row[expected + (int)i]) return 5;
                            if ((row[3] == 0 || row[2] <= 1) && calls != 0) return 6;
                        }
                    }
                    if (ferror(file!) != 0) return 7;
                    values.Sort();
                    values.Reverse();
                    Standard::Collections::Sort<int>(&values, Compare);
                    Standard::Collections::Reverse<int>(&values);
                    return 0;
                }
                """)), optimization);
            Assert.True(result.ExitCode == 0, $"Vector ordering exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }
        finally { File.Delete(path); }
    }
}
