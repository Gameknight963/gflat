namespace gflat.Tests.Std.Collections;

public sealed class SpanRangeSearchTests
{
    private readonly record struct Ordered(int Value) : IComparable<Ordered>
    {
        public int CompareTo(Ordered other) => Value.CompareTo(other.Value);
    }

    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("SPAN_RANGE_SEARCH.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BorrowedBoundsSupportNoncopyableValuesShortCircuitingAndFailures(int optimization)
    {
        var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/Collections/Comparer.gf", "core/Collections/default_ordering.gf", "core/Collections/span_range_search.gf"))
            .Append(new SourceFile("span-range-owners.gf", """
            using Standard;
            using Standard::Collections;
            using Standard::Memory;

            long trace = 0;
            bool fail = false;
            struct Item : IComparable<Item>
            {
                public int Value;
                public int Index;
                public Item(int value, int index) { Value = value; Index = index; }
                ~Item() { }
                public immutable int CompareTo(immutable(Item)* other)
                {
                    trace = trace * 100l + (long)((Index + 1) * 10 + other.Index + 1);
                    if (fail) throw new Exception("comparison failed");
                    if (Value < other.Value) return -1;
                    if (Value > other.Value) return 1;
                    return 0;
                }
            }
            int Compare(immutable(Item)* left, immutable(Item)* right) => left.CompareTo(right);
            int main()
            {
                Item[3] data = [new Item(2, 0), new Item(5, 1), new Item(9, 2)];
                Item lower = new(5, 0);
                Item upper = new(9, 1);
                Span<immutable(Item)> values = new(&data[0], 3);
                if (IndexOfAnyInRange<Item>(values, &lower, &upper) != 1 || trace != 112122l) return 1;
                trace = 0;
                if (LastIndexOfAnyInRange<Item>(values, &lower, &upper, Compare) != 2 || trace != 3132l) return 2;
                trace = 0;
                if (IndexOfAnyExceptInRange<Item>(values, &lower, &upper) != 0 || trace != 11l) return 3;
                trace = 0;
                if (LastIndexOfAnyExceptInRange<Item>(values, &lower, &upper) != 0 || trace != 3132212211l) return 4;
                trace = 0;
                if (!ContainsAnyInRange<Item>(values, &data[1], &data[2])) return 5;
                trace = 0;
                if (!ContainsAnyExceptInRange<Item>(values, &lower, &upper, Compare)) return 6;
                trace = 0;
                fail = true;
                Span<immutable(Item)> empty = new();
                if (ContainsAnyInRange<Item>(empty, &lower, &upper) || ContainsAnyExceptInRange<Item>(empty, &lower, &upper)) return 7;
                if (trace != 0) return 8;
                bool caught = false;
                nuint output = 99;
                try { output = IndexOfAnyInRange<Item>(values, &lower, &upper); }
                catch (Exception* error) { caught = true; }
                if (!caught || output != 99 || data[0].Value != 2 || lower.Value != 5) return 9;
                return 0;
            }
            """)), optimization);
        Assert.True(result.ExitCode == 0, $"Borrowed range exit {result.ExitCode}: {result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RangesMatchDotNetGenericComparisonIncludingReversedBounds(int optimization)
    {
        string path = Path.Combine(Path.GetTempPath(), "gflat_range_search_" + Guid.NewGuid().ToString("N"));
        try
        {
            int[] bounds = [int.MinValue, -2, -1, 0, 1, 2, int.MaxValue];
            var random = new Random(79542);
            using (var writer = new BinaryWriter(File.Create(path)))
            for (int length = 0; length <= 12; length++)
            for (int sample = 0; sample < 8; sample++)
            {
                var source = Enumerable.Range(0, length).Select(_ => new Ordered(bounds[random.Next(bounds.Length)])).ToArray();
                foreach (int lower in bounds)
                foreach (int upper in bounds)
                {
                    int Fix(int index) => index < 0 ? length : index;
                    int[] row = new int[19];
                    row[0] = length; row[1] = lower; row[2] = upper;
                    row[3] = Fix(MemoryExtensions.IndexOfAnyInRange<Ordered>(source.AsSpan(), new(lower), new(upper)));
                    row[4] = Fix(MemoryExtensions.LastIndexOfAnyInRange<Ordered>(source.AsSpan(), new(lower), new(upper)));
                    row[5] = Fix(MemoryExtensions.IndexOfAnyExceptInRange<Ordered>(source.AsSpan(), new(lower), new(upper)));
                    row[6] = Fix(MemoryExtensions.LastIndexOfAnyExceptInRange<Ordered>(source.AsSpan(), new(lower), new(upper)));
                    for (int i = 0; i < length; i++) row[7 + i] = source[i].Value;
                    foreach (int value in row) writer.Write(value);
                }
            }
            string literal = "\"" + path.Replace("\\", "\\\\") + "\"";
            var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
                StandardTestSources.Load("core/Collections/Comparer.gf", "core/Collections/default_ordering.gf", "core/Collections/span_range_search.gf"))
                .Append(new SourceFile("span-range-search.gf", $$"""
                using Standard;
                using Standard::Collections;
                using Standard::Memory;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" nuint fread(void* buffer, nuint size, nuint count, void* file);
                extern "C" int fclose(void* file);
                extern "C" int ferror(void* file);
                int Compare(immutable(int)* left, immutable(int)* right)
                {
                    if (*left < *right) return -1;
                    if (*left > *right) return 1;
                    return 0;
                }
                int main()
                {
                    void*? file = fopen({{literal}}, "rb");
                    if (file == null) return 1;
                    defer { fclose(file!); }
                    int[19] row;
                    while (true)
                    {
                        nuint received = fread(&row[0], 1, 76, file!);
                        if (received == 0) break;
                        if (received != 76) return 2;
                        Span<immutable(int)> values = new(&row[7], (nuint)row[0]);
                        if (IndexOfAnyInRange<int>(values, &row[1], &row[2]) != (nuint)row[3]) return 3;
                        if (LastIndexOfAnyInRange<int>(values, &row[1], &row[2]) != (nuint)row[4]) return 4;
                        if (IndexOfAnyExceptInRange<int>(values, &row[1], &row[2]) != (nuint)row[5]) return 5;
                        if (LastIndexOfAnyExceptInRange<int>(values, &row[1], &row[2]) != (nuint)row[6]) return 6;
                        if (ContainsAnyInRange<int>(values, &row[1], &row[2]) != (row[3] != row[0])) return 7;
                        if (ContainsAnyExceptInRange<int>(values, &row[1], &row[2]) != (row[5] != row[0])) return 8;
                        if (IndexOfAnyInRange<int>(values, &row[1], &row[2], Compare) != (nuint)row[3]) return 9;
                        if (LastIndexOfAnyInRange<int>(values, &row[1], &row[2], Compare) != (nuint)row[4]) return 10;
                        if (IndexOfAnyExceptInRange<int>(values, &row[1], &row[2], Compare) != (nuint)row[5]) return 11;
                        if (LastIndexOfAnyExceptInRange<int>(values, &row[1], &row[2], Compare) != (nuint)row[6]) return 12;
                    }
                    if (ferror(file!) != 0) return 13;
                    return 0;
                }
                """)), optimization);
            Assert.True(result.ExitCode == 0, $"Range search exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }
        finally { File.Delete(path); }
    }
}
