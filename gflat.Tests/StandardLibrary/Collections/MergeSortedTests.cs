using System.Globalization;
using gflat.CompileExceptions;

namespace gflat.Tests.Std.Collections;

public sealed class MergeSortedTests
{
    private static IEnumerable<SourceFile> Sources(string program) =>
        StandardTestSources.Combine(StandardTestSources.Values, StandardTestSources.Load(
            "core/Collections/Comparer.gf", "core/Collections/merge_sorted.gf", "core/Collections/merge_sorted_default.gf"))
            .Append(new SourceFile("merge_sorted_tests.gf", program));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ExhaustiveSmallAndSeededInputsMatchHostOrderingWithoutMutatingInputs(int optimization)
    {
        var arrays = new List<int[]> { Array.Empty<int>() };
        for (int length = 1; length <= 3; length++)
        for (int code = 0; code < (int)Math.Pow(3, length); code++)
        {
            int digits = code;
            var values = new int[length];
            for (int index = 0; index < length; index++) { values[index] = digits % 3 - 1; digits /= 3; }
            if (values.SequenceEqual(values.Order())) arrays.Add(values);
        }
        var cases = new List<(int[] First, int[] Second, bool Descending)>();
        foreach (var first in arrays)
        foreach (var second in arrays)
        {
            cases.Add((first, second, false));
            cases.Add((first.Reverse().ToArray(), second.Reverse().ToArray(), true));
        }
        var random = new Random(0x4D455247);
        for (int index = 0; index < 80; index++)
        {
            bool descending = index % 2 != 0;
            int[] Sorted() => descending
                ? Enumerable.Range(0, random.Next(32)).Select(_ => random.Next(-9, 10)).OrderDescending().ToArray()
                : Enumerable.Range(0, random.Next(32)).Select(_ => random.Next(-9, 10)).Order().ToArray();
            cases.Add((Sorted(), Sorted(), descending));
        }
        var encoded = new List<int>();
        foreach (var item in cases)
        {
            encoded.AddRange([item.Descending ? 1 : 0, item.First.Length, item.Second.Length]);
            encoded.AddRange(item.First);
            encoded.AddRange(item.Second);
            encoded.AddRange(item.Descending ? item.First.Concat(item.Second).OrderDescending() : item.First.Concat(item.Second).Order());
        }
        ulong expectedHash = 0;
        foreach (int value in encoded) expectedHash = unchecked(expectedHash * 33 + (uint)value);
        string packed = new string(encoded.Select(value => checked((char)(value + 64))).ToArray()).Replace("\\", "\\\\", StringComparison.Ordinal);
        string program = """
            using Standard::Collections;
            using Standard::Memory;

            int comparisons = 0;
            int Ascending(immutable(int)* left, immutable(int)* right)
            {
                comparisons++;
                if (*left < *right) return -7;
                if (*left > *right) return 19;
                return 0;
            }
            int Descending(immutable(int)* left, immutable(int)* right)
            {
                comparisons++;
                if (*left > *right) return -7;
                if (*left < *right) return 19;
                return 0;
            }
            ulong Hash(immutable(int)* data, nuint count)
            {
                ulong hash = 0;
                for (nuint index = 0; index < count; index++) hash = hash * 33ul + (ulong)(uint)data[index];
                return hash;
            }
            int main()
            {
                readonly(char)* packed = "TABLE";
                int[TABLE_LENGTH] encoded;
                for (nuint index = 0; index < TABLE_LENGTH; index++) encoded[index] = (int)packed[index] - 64;
                int* data = &encoded[0];
                ulong before = Hash(data, TABLE_LENGTH);
                if (before != EXPECTED_HASHul) return 6;
                nuint cursor = 0;
                for (int index = 0; index < CASE_COUNT; index++)
                {
                    bool descending = data[cursor++] != 0;
                    nuint firstCount = (nuint)data[cursor++];
                    nuint secondCount = (nuint)data[cursor++];
                    Span<immutable(int)> first = new(data + cursor, firstCount);
                    cursor += firstCount;
                    Span<immutable(int)> second = new(data + cursor, secondCount);
                    cursor += secondCount;
                    comparisons = 0;
                    Vector<int> merged = new();
                    if (descending) merged = MergeSorted<int>(first, second, Descending);
                    else if (index % 3 == 0) merged = MergeSorted<int>(first, second);
                    else merged = MergeSorted<int>(first, second, Ascending);
                    nuint count = firstCount + secondCount;
                    if (merged.Count != count) return 1;
                    for (nuint item = 0; item < count; item++)
                        if (merged[item] != data[cursor + item]) return 2;
                    if (count != 0 && comparisons > (int)(count - 1)) return 3;
                    if ((firstCount == 0 || secondCount == 0) && comparisons != 0) return 4;
                    if (count != 0) merged[0] = 123456;
                    cursor += count;
                }
                if (cursor != TABLE_LENGTH || Hash(data, TABLE_LENGTH) != before) return 5;
                return 0;
            }
            """;
        program = program.Replace("TABLE_LENGTH", encoded.Count.ToString(CultureInfo.InvariantCulture))
            .Replace("CASE_COUNT", cases.Count.ToString(CultureInfo.InvariantCulture))
            .Replace("EXPECTED_HASH", expectedHash.ToString(CultureInfo.InvariantCulture))
            .Replace("TABLE", packed);
        var result = CompilerTestHelper.Run(Sources(program), optimization);
        Assert.True(result.ExitCode == 0, $"MergeSorted oracle exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EmptyInputsAvoidAllocationAndOverflowAvoidsComparisons(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            using Standard;
            using Standard::Collections;
            using Standard::Memory;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            int attempts = 0;
            int comparisons = 0;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes)
                {
                    attempts++;
                    return malloc(bytes);
                }
                public replace void Free(void* storage) nothrow => free(storage);
            }
            int NeverCompare(immutable(int)* left, immutable(int)* right)
            {
                comparisons++;
                throw new Exception("Unexpected comparison", 71);
            }
            int main()
            {
                immutable(int)* invalid = ((nuint)1).reinterpret<immutable(int)*>();
                Span<immutable(int)> empty = new(invalid, 0);
                Vector<int> none = MergeSorted<int>(empty, empty, NeverCompare);
                if (none.Count != 0 || attempts != 0 || comparisons != 0) return 1;
                nuint maximum = (nuint)0 - (nuint)1;
                Span<immutable(int)> huge = new(invalid, maximum);
                Span<immutable(int)> one = new(invalid, 1);
                bool caught = false;
                try { Vector<int> value = MergeSorted<int>(huge, one, NeverCompare); }
                catch (Exception* error) { caught = true; }
                if (!caught || comparisons != 0) return 2;
                caught = false;
                try { Vector<int> value = MergeSorted<int>(huge, empty, NeverCompare); }
                catch (Exception* error) { caught = true; }
                if (!caught || comparisons != 0) return 3;
                int[3] values = [1, 2, 3];
                Span<immutable(int)> input = new(values, 3);
                Vector<int> left = MergeSorted<int>(input, empty, NeverCompare);
                Vector<int> right = MergeSorted<int>(empty, input, NeverCompare);
                if (left.Count != 3 || right.Count != 3 || left[2] != 3 || right[0] != 1 || comparisons != 0) return 4;
                Vector<int> duplicated = MergeSorted<int>(input, input);
                if (duplicated.Count != 6) return 5;
                for (nuint index = 0; index < 6; index++)
                    if (duplicated[index] != (int)(index / (nuint)2) + 1) return 6;
                if (values[0] != 1 || values[1] != 2 || values[2] != 3) return 7;
                return 0;
            }
            """), optimization);
        Assert.True(result.ExitCode == 0, $"MergeSorted bounds exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Fact]
    [Trait("Optimization", "Independent")]
    public void ElementCopyingIsRequiredOnlyWhenTheMergeIsInstantiated()
    {
        const string declarations = """
            using Standard::Collections;
            using Standard::Memory;

            struct Owner
            {
                public int Value;
                public Owner(int value) { Value = value; }
                ~Owner() {}
            }
            int Compare(immutable(Owner)* left, immutable(Owner)* right) => left.Value - right.Value;
            """;
        Compiler.Check(Sources(declarations + "\nint Read() { Owner value = new(7); Span<Owner> view = new(&value, 1); return view.AsImmutable()[0].Value; }"));
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Sources(declarations +
            "\nvoid Copy() { Span<immutable(Owner)> empty = new(); Vector<Owner> value = MergeSorted<Owner>(empty, empty, Compare); }")));
        Assert.Contains("copy", error.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DefaultFloatingOrderingKeepsNaNPayloadsAndSignedZeroesStable(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            using Standard::Collections;
            using Standard::Memory;

            int main()
            {
                uint[3] firstBits = [0x7fc00001u, 0x80000000u, 0x3f800000u];
                uint[3] secondBits = [0x7fc00002u, 0x00000000u, 0x40000000u];
                Span<immutable(float)> first = new((&firstBits[0]).reinterpret<immutable(float)*>(), 3);
                Span<immutable(float)> second = new((&secondBits[0]).reinterpret<immutable(float)*>(), 3);
                Vector<float> merged = MergeSorted<float>(first, second);
                if (merged.Count != 6) return 1;
                Span<float> output = merged.AsSpan();
                readonly(uint)* bits = output.Data!.reinterpret<readonly(uint)*>();
                uint[6] expected = [0x7fc00001u, 0x7fc00002u, 0x80000000u, 0x00000000u, 0x3f800000u, 0x40000000u];
                for (nuint index = 0; index < 6; index++)
                    if (bits[index] != expected[index]) return 2;
                return 0;
            }
            """), optimization);
        Assert.True(result.ExitCode == 0, $"MergeSorted floating ordering exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EquivalentKeysAreStableAndEveryFailurePreservesInputsAndAssignmentTarget(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            using Standard;
            using Standard::Collections;
            using Standard::Memory;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            int liveBuffers = 0;
            int liveItems = 0;
            int copies = 0;
            int comparisons = 0;
            int copyBudget = -1;
            int comparisonBudget = -1;
            bool deny = false;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes)
                {
                    if (deny) return null;
                    void*? result = malloc(bytes);
                    if (result != null) liveBuffers++;
                    return result;
                }
                public replace void Free(void* storage) nothrow { liveBuffers--; free(storage); }
            }
            struct Item : IComparable<Item>
            {
                public int Key;
                public int Id;
                public Item(int key, int id) { Key = key; Id = id; liveItems++; }
                public copy Item(immutable(Item)* source)
                {
                    if (copyBudget == 0) throw new Exception("copy failed", 72);
                    if (copyBudget > 0) copyBudget--;
                    Key = source.Key;
                    Id = source.Id;
                    copies++;
                    liveItems++;
                }
                public immutable int CompareTo(immutable(Item)* other) => Compare(this, other);
                ~Item() { liveItems--; }
            }
            int Compare(immutable(Item)* first, immutable(Item)* second)
            {
                if (comparisonBudget == 0) throw new Exception("comparison failed", 71);
                if (comparisonBudget > 0) comparisonBudget--;
                comparisons++;
                if (first.Key < second.Key) return -7;
                if (first.Key > second.Key) return 19;
                return 0;
            }
            int main()
            {
                {
                    Vector<Item> first = new();
                    first.Add(new Item(1, 10));
                    first.Add(new Item(3, 11));
                    first.Add(new Item(3, 12));
                    Vector<Item> second = new();
                    second.Add(new Item(1, 20));
                    second.Add(new Item(2, 21));
                    second.Add(new Item(3, 22));
                    Vector<Item> target = new();
                    target.Add(new Item(99, 99));
                    int beforeItems = liveItems;
                    int beforeBuffers = liveBuffers;
                    comparisons = 0;
                    {
                        Vector<Item> merged = MergeSorted<Item>(first.AsImmutable(), second.AsImmutable());
                        int[6] expected = [10, 20, 21, 11, 12, 22];
                        if (merged.Count != 6) return 1;
                        for (nuint index = 0; index < 6; index++)
                            if (merged[index].Id != expected[index]) return 2;
                        if (liveItems != beforeItems + 6) return 3;
                    }
                    if (liveItems != beforeItems || liveBuffers != beforeBuffers) return 4;
                    int comparisonCount = comparisons;
                    for (int failure = 0; failure < comparisonCount; failure++)
                    {
                        comparisonBudget = failure;
                        bool caught = false;
                        try { target = MergeSorted<Item>(first.AsImmutable(), second.AsImmutable(), Compare); }
                        catch (Exception* error) { caught = true; if (error.GetCode() != 71) return 5; }
                        if (!caught || liveItems != beforeItems || liveBuffers != beforeBuffers) return 6;
                        if (target.Count != 1 || target[0].Id != 99) return 7;
                    }
                    comparisonBudget = -1;
                    for (int failure = 0; failure < 6; failure++)
                    {
                        copyBudget = failure;
                        bool caught = false;
                        try
                        {
                            if (failure % 2 == 0) target = MergeSorted<Item>(first.AsImmutable(), second.AsImmutable());
                            else target = MergeSorted<Item>(first.AsImmutable(), second.AsImmutable(), Compare);
                        }
                        catch (Exception* error) { caught = true; if (error.GetCode() != 72) return 8; }
                        if (!caught || liveItems != beforeItems || liveBuffers != beforeBuffers) return 9;
                        if (target.Count != 1 || target[0].Id != 99) return 10;
                    }
                    copyBudget = -1;
                    int beforeCopies = copies;
                    int beforeComparisons = comparisons;
                    deny = true;
                    bool caught = false;
                    try { target = MergeSorted<Item>(first.AsImmutable(), second.AsImmutable(), Compare); }
                    catch (Exception* error) { caught = true; }
                    deny = false;
                    if (!caught || copies != beforeCopies || comparisons != beforeComparisons) return 11;
                    if (liveItems != beforeItems || liveBuffers != beforeBuffers || target[0].Id != 99) return 12;
                    if (first.Count != 3 || second.Count != 3 || first[0].Id != 10 || first[1].Id != 11 || first[2].Id != 12 ||
                        second[0].Id != 20 || second[1].Id != 21 || second[2].Id != 22) return 13;
                    copyBudget = 0;
                    caught = false;
                    try { first = MergeSorted<Item>(first.AsImmutable(), second.AsImmutable()); }
                    catch (Exception* error) { caught = true; if (error.GetCode() != 72) return 15; }
                    copyBudget = -1;
                    if (!caught || first.Count != 3 || first[0].Id != 10 || liveItems != beforeItems || liveBuffers != beforeBuffers) return 16;
                    first = MergeSorted<Item>(first.AsImmutable(), second.AsImmutable());
                    if (first.Count != 6 || first[0].Id != 10 || first[5].Id != 22 || second.Count != 3 || target[0].Id != 99) return 17;
                    if (liveItems != beforeItems + 3 || liveBuffers != beforeBuffers) return 18;
                }
                if (liveItems != 0 || liveBuffers != 0) return 14;
                return 0;
            }
            """), optimization);
        Assert.True(result.ExitCode == 0, $"MergeSorted stability/cleanup exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
