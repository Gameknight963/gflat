namespace gflat.Tests.Std.Collections;

public sealed class VectorSortedSearchTests
{
    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("VECTOR_SORTED_SEARCH.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DescendingNoncopyableValuesBorrowKeysAndResultsAndPreserveFailedOutputs(int optimization)
    {
        var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/Collections/Comparer.gf", "core/Collections/span_algorithms.gf", "core/Collections/vector_sorted_search.gf"))
            .Append(new SourceFile("vector-bounds-owners.gf", """
            using Standard;
            using Standard::Collections;
            using Standard::Memory;

            int destroyed = 0;
            bool fail = false;
            struct Item
            {
                public int Key;
                public Item(int key) { Key = key; }
                ~Item() { destroyed++; }
            }
            int Descending(immutable(Item)* left, immutable(Item)* right)
            {
                if (fail) throw new Exception("comparison failed");
                if (left.Key > right.Key) return -1;
                if (left.Key < right.Key) return 1;
                return 0;
            }
            int main()
            {
                {
                    Vector<Item> values = new();
                    values.Add(new Item(9)); values.Add(new Item(5)); values.Add(new Item(5)); values.Add(new Item(2));
                    immutable(Item)* key = &values[2];
                    if (values.LowerBound(key, Descending) != 1 || values.UpperBound(key, Descending) != 3) return 1;
                    if (values.BinarySearch(key, Descending) != 1) return 2;
                    Span<immutable(Item)> matches = values.EqualRange(key, Descending);
                    if (matches.Length != 2 || matches.Data != &values[1]) return 3;
                    values[1].Key = 6;
                    if (matches[0].Key != 6) return 4;
                    values[1].Key = 5;
                    if (values.BinarySearch(2, 1, key, Descending) != 2) return 5;
                    if (Standard::Collections::LowerBound<Item>(&values, key, Descending) != 1) return 6;
                    if (Standard::Collections::UpperBound<Item>(&values, key, Descending) != 3) return 7;
                    fail = true;
                    nuint output = 99;
                    bool caught = false;
                    try { output = values.BinarySearch(key, Descending); }
                    catch (Exception* error) { caught = true; }
                    if (!caught || output != 99 || values.Count != 4 || destroyed != 0) return 8;
                    Span<immutable(Item)> old = matches;
                    caught = false;
                    try { matches = values.EqualRange(key, Descending); }
                    catch (Exception* error) { caught = true; }
                    if (!caught || matches.Data != old.Data || matches.Length != old.Length) return 9;
                }
                if (destroyed != 4) return 10;
                return 0;
            }
            """)), optimization);
        Assert.True(result.ExitCode == 0, $"Borrowed bounds exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DuplicateBoundsAndSubrangesMatchListInsertionPoints(int optimization)
    {
        string path = Path.Combine(Path.GetTempPath(), "gflat_vector_bounds_" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            for (int length = 0; length <= 12; length++)
            for (int start = 0; start <= length + 1; start++)
            for (int count = 0; count <= length + 1; count++)
            for (int key = -4; key <= 4; key++)
            {
                var source = Enumerable.Range(0, length).Select(i => i / 3 - 2).ToList();
                bool valid = start <= length && count <= length - start;
                int lower = 0, upper = 0, found = length;
                if (valid)
                {
                    var range = source.GetRange(start, count);
                    lower = start + range.TakeWhile(i => i < key).Count();
                    upper = start + range.TakeWhile(i => i <= key).Count();
                    if (lower < start + count && source[lower] == key) found = lower;
                    int index = source.BinarySearch(start, count, key, null);
                    Assert.Equal(found != length, index >= 0);
                    if (index < 0) Assert.Equal(lower, ~index);
                }
                foreach (int item in new[] { length, start, count, key, valid ? 1 : 0, lower, upper, found }) writer.Write(item);
                for (int i = 0; i < 12; i++) writer.Write(i < length ? source[i] : 0);
            }
            string literal = "\"" + path.Replace("\\", "\\\\") + "\"";
            var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
                StandardTestSources.Load("core/Collections/Comparer.gf", "core/Collections/span_algorithms.gf", "core/Collections/vector_sorted_search.gf"))
                .Append(new SourceFile("vector-sorted-search.gf", $$"""
                using Standard;
                using Standard::Collections;
                using Standard::Memory;

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
                    int[20] row;
                    Vector<int> values = new();
                    values.Reserve(12);
                    while (true)
                    {
                        nuint received = fread(&row[0], 1, 80, file!);
                        if (received == 0) break;
                        if (received != 80) return 2;
                        values.Clear();
                        for (int i = 0; i < row[0]; i++) values.Add(row[8 + i]);
                        for (int method = 0; method < 8; method++)
                        {
                            nuint result = 99;
                            Span<immutable(int)> matches = new();
                            calls = 0;
                            bool caught = false;
                            try
                            {
                                nuint start = (nuint)row[1];
                                nuint count = (nuint)row[2];
                                if (method == 0) result = values.LowerBound(start, count, &row[3]);
                                else if (method == 1) result = values.UpperBound(start, count, &row[3]);
                                else if (method == 2) result = values.BinarySearch(start, count, &row[3]);
                                else if (method == 3) matches = values.EqualRange(start, count, &row[3]);
                                else if (method == 4) result = values.LowerBound(start, count, &row[3], Compare);
                                else if (method == 5) result = values.UpperBound(start, count, &row[3], Compare);
                                else if (method == 6) result = values.BinarySearch(start, count, &row[3], Compare);
                                else matches = values.EqualRange(start, count, &row[3], Compare);
                            }
                            catch (Exception* error) { caught = true; }
                            if (row[4] == 0)
                            {
                                if (!caught || calls != 0 || result != 99 || !matches.IsEmpty) return 3;
                                continue;
                            }
                            if (caught) return 4;
                            int kind = method % 4;
                            if (kind == 0 && result != (nuint)row[5]) return 5;
                            if (kind == 1 && result != (nuint)row[6]) return 6;
                            if (kind == 2 && result != (nuint)row[7]) return 7;
                            if (kind == 3)
                            {
                                if (matches.Length != (nuint)(row[6] - row[5])) return 8;
                                if (matches.IsEmpty) { if (matches.Data != null) return 9; }
                                else if (matches.Data != &values[(nuint)row[5]]) return 10;
                            }
                        }
                        if (row[1] == 0 && row[2] == row[0])
                        {
                            if (values.LowerBound(&row[3]) != (nuint)row[5] || values.UpperBound(&row[3]) != (nuint)row[6]) return 11;
                            if (values.BinarySearch(&row[3]) != (nuint)row[7]) return 12;
                            if (values.EqualRange(&row[3]).Length != (nuint)(row[6] - row[5])) return 13;
                        }
                    }
                    if (ferror(file!) != 0) return 14;
                    return 0;
                }
                """)), optimization);
            Assert.True(result.ExitCode == 0, $"Sorted vector exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }
        finally { File.Delete(path); }
    }
}
