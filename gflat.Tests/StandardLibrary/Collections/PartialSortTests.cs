using System.Globalization;

namespace gflat.Tests.Std.Collections;

public sealed class PartialSortTests
{
    private static IEnumerable<SourceFile> Sources(string program) =>
        StandardTestSources.Combine(StandardTestSources.Values, StandardTestSources.Load(
            "core/Collections/span_algorithms.gf", "core/Collections/partial_sort.gf",
            "core/Collections/default_ordering.gf", "core/Collections/Comparer.gf"))
            .Append(new SourceFile("partial_sort_tests.gf", program));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PrefixesMatchHostSortingAndPreserveEveryElementAndRangeBoundary(int optimization)
    {
        var cases = new List<(int[] Values, int Count, bool Descending)>();
        for (int length = 0; length <= 4; length++)
        for (int code = 0; code < (int)Math.Pow(3, length); code++)
        {
            int digits = code;
            var values = new int[length];
            for (int index = 0; index < length; index++) { values[index] = digits % 3 - 1; digits /= 3; }
            for (int count = 0; count <= length; count++)
            {
                cases.Add((values, count, false));
                cases.Add((values, count, true));
            }
        }
        var random = new Random(0x50415254);
        for (int index = 0; index < 80; index++)
        {
            int[] values = Enumerable.Range(0, random.Next(32)).Select(_ => random.Next(-9, 10)).ToArray();
            cases.Add((values, random.Next(values.Length + 1), index % 2 != 0));
        }
        var encoded = new List<int>();
        foreach (var item in cases)
        {
            encoded.AddRange([item.Descending ? 1 : 0, item.Values.Length, item.Count]);
            encoded.AddRange(item.Values);
            encoded.AddRange(item.Descending ? item.Values.OrderDescending() : item.Values.Order());
        }
        ulong expectedHash = 0;
        foreach (int value in encoded) expectedHash = unchecked(expectedHash * 33 + (uint)value);
        string packed = new string(encoded.Select(value => checked((char)(value + 64))).ToArray()).Replace("\\", "\\\\", StringComparison.Ordinal);
        string program = """
            using Standard::Collections;
            using Standard::Memory;

            int comparisons = 0;
            int Ascending(immutable(int)* first, immutable(int)* second)
            {
                comparisons++;
                if (*first < *second) return -7;
                if (*first > *second) return 19;
                return 0;
            }
            int Descending(immutable(int)* first, immutable(int)* second)
            {
                comparisons++;
                if (*first > *second) return -7;
                if (*first < *second) return 19;
                return 0;
            }
            int main()
            {
                readonly(char)* packed = "TABLE";
                ulong hash = 0;
                for (nuint index = 0; index < TABLE_LENGTH; index++) hash = hash * 33ul + (ulong)(uint)((int)packed[index] - 64);
                if (hash != EXPECTED_HASHul) return 1;
                nuint cursor = 0;
                for (int sample = 0; sample < CASE_COUNT; sample++)
                {
                    bool descending = packed[cursor++] != '@';
                    nuint length = (nuint)((int)packed[cursor++] - 64);
                    nuint count = (nuint)((int)packed[cursor++] - 64);
                    nuint input = cursor;
                    int[34] data;
                    data[0] = 12345;
                    data[length + 1] = -12345;
                    for (nuint index = 0; index < length; index++) data[index + 1] = (int)packed[cursor++] - 64;
                    Span<int> values = new(&data[1], length);
                    comparisons = 0;
                    if (descending) PartialSort<int>(values, count, Descending);
                    else if (sample % 3 == 0) PartialSort<int>(values, count);
                    else PartialSort<int>(values, count, Ascending);
                    if (data[0] != 12345 || data[length + 1] != -12345) return 2;
                    for (nuint index = 0; index < count; index++)
                        if (values[index] != (int)packed[cursor + index] - 64) return 3;
                    if (count == 0)
                    {
                        if (comparisons != 0) return 4;
                        for (nuint index = 0; index < length; index++)
                            if (values[index] != (int)packed[input + index] - 64) return 5;
                    }
                    for (int key = -9; key <= 9; key++)
                    {
                        int before = 0;
                        int after = 0;
                        for (nuint index = 0; index < length; index++)
                        {
                            if ((int)packed[input + index] - 64 == key) before++;
                            if (values[index] == key) after++;
                        }
                        if (before != after) return 6;
                    }
                    cursor += length;
                }
                if (cursor != TABLE_LENGTH) return 7;
                return 0;
            }
            """;
        program = program.Replace("TABLE_LENGTH", encoded.Count.ToString(CultureInfo.InvariantCulture))
            .Replace("CASE_COUNT", cases.Count.ToString(CultureInfo.InvariantCulture))
            .Replace("EXPECTED_HASH", expectedHash.ToString(CultureInfo.InvariantCulture))
            .Replace("TABLE", packed);
        var result = CompilerTestHelper.Run(Sources(program), optimization);
        Assert.True(result.ExitCode == 0, $"PartialSort oracle exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RelocationKeepsNoncopyableOwnersValidAcrossEveryComparisonFailure(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            using Standard;
            using Standard::Collections;
            using Standard::Memory;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            int allocations = 0;
            int live = 0;
            int moves = 0;
            int comparisons = 0;
            int budget = -1;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes) { allocations++; return malloc(bytes); }
                public replace void Free(void* storage) nothrow => free(storage);
            }
            struct Owner
            {
                public int Key;
                private int* address;
                public Owner(int key) nothrow { Key = key; address = &Key; live++; }
                public move Owner(Owner* source) nothrow { Key = source.Key; address = &Key; moves++; }
                public bool Valid() nothrow => address == &Key && *address == Key;
                ~Owner() { live--; }
            }
            int Compare(immutable(Owner)* first, immutable(Owner)* second)
            {
                if (budget == 0) throw new Exception("comparison failed", 77);
                if (budget > 0) budget--;
                comparisons++;
                if (first.Key < second.Key) return -7;
                if (first.Key > second.Key) return 19;
                return 0;
            }
            int Run(int limit, nuint count)
            {
                Owner[8] data = [new Owner(7), new Owner(0), new Owner(6), new Owner(1), new Owner(5), new Owner(2), new Owner(4), new Owner(3)];
                Span<Owner> values = new(data, 8);
                budget = limit;
                comparisons = 0;
                moves = 0;
                int beforeAllocations = allocations;
                bool caught = false;
                try { PartialSort<Owner>(values, count, Compare); }
                catch (Exception* error) { caught = true; if (error.GetCode() != 77) return 1; }
                budget = -1;
                if (count == 0)
                {
                    if (caught || comparisons != 0 || moves != 0 || allocations != beforeAllocations) return 2;
                }
                else if (limit >= 0 && !caught) return 3;
                else if (limit < 0 && (caught || allocations != beforeAllocations)) return 4;
                if (live != 8) return 5;
                int[8] seen = [0, 0, 0, 0, 0, 0, 0, 0];
                for (nuint index = 0; index < 8; index++)
                {
                    if (!data[index].Valid() || data[index].Key < 0 || data[index].Key >= 8) return 6;
                    seen[data[index].Key]++;
                }
                for (int key = 0; key < 8; key++) if (seen[key] != 1) return 7;
                if (!caught)
                    for (nuint index = 0; index < count; index++) if (data[index].Key != (int)index) return 8;
                return 0;
            }
            int main()
            {
                if (Run(-1, 3) != 0 || live != 0) return 1;
                int count = comparisons;
                for (int failure = 0; failure < count; failure++)
                    if (Run(failure, 3) != 0 || live != 0) return 2;
                if (Run(0, 0) != 0 || live != 0) return 3;
                return 0;
            }
            """), optimization);
        Assert.True(result.ExitCode == 0, $"PartialSort relocation exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InvalidCountsAreRejectedBeforeReadingOrComparing(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            using Standard;
            using Standard::Collections;
            using Standard::Memory;

            int comparisons = 0;
            int Compare(immutable(int)* first, immutable(int)* second)
            {
                comparisons++;
                throw new Exception("unexpected comparison", 77);
            }
            int main()
            {
                Span<int> empty = new(((nuint)1).reinterpret<int*>(), 0);
                PartialSort<int>(empty, 0, Compare);
                bool caught = false;
                try { PartialSort<int>(empty, (nuint)0 - (nuint)1, Compare); }
                catch (Exception* error) { caught = true; if (error.GetCode() != 3) return 1; }
                if (!caught || comparisons != 0) return 2;
                int[3] data = [3, 1, 2];
                Span<int> values = new(data, 3);
                caught = false;
                try { PartialSort<int>(values, 4, Compare); }
                catch (Exception* error) { caught = true; if (error.GetCode() != 3) return 3; }
                if (!caught || comparisons != 0 || data[0] != 3 || data[1] != 1 || data[2] != 2) return 4;
                return 0;
            }
            """), optimization);
        Assert.True(result.ExitCode == 0, $"PartialSort bounds exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
