using System.Text;

namespace gflat.Tests.Std.Collections;

public sealed class InPlaceMergeTests
{
    private static IEnumerable<SourceFile> Sources(string program) =>
        StandardTestSources.Combine(StandardTestSources.Values, StandardTestSources.Load(
            "core/Collections/span_algorithms.gf", "core/Collections/in_place_merge.gf",
            "core/Collections/default_ordering.gf", "core/Collections/Comparer.gf"))
            .Append(new SourceFile("in_place_merge_tests.gf", program));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ExhaustiveAndSeededRangesMatchStableHostOrderIncludingEqualKeys(int optimization)
    {
        var cases = new List<(int[] Keys, int Middle, bool Descending)>();
        for (int length = 0; length <= 5; length++)
        for (int encoded = 0; encoded < (int)Math.Pow(3, length); encoded++)
        {
            int digits = encoded;
            var keys = new int[length];
            for (int index = 0; index < length; index++) { keys[index] = digits % 3; digits /= 3; }
            for (int middle = 0; middle <= length; middle++)
            {
                cases.Add((keys, middle, false));
                cases.Add((keys, middle, true));
            }
        }
        var random = new Random(0x4D455247);
        for (int sample = 0; sample < 80; sample++)
        {
            int[] keys = Enumerable.Range(0, random.Next(48)).Select(_ => random.Next(7)).ToArray();
            cases.Add((keys, random.Next(keys.Length + 1), sample % 2 != 0));
        }
        var packed = new StringBuilder();
        foreach (var item in cases)
        {
            int Key(int index) => item.Descending ? -item.Keys[index] : item.Keys[index];
            int[] input = Enumerable.Range(0, item.Middle).OrderBy(Key)
                .Concat(Enumerable.Range(item.Middle, item.Keys.Length - item.Middle).OrderBy(Key)).ToArray();
            packed.Append((char)(64 + item.Keys.Length)).Append((char)(64 + item.Middle))
                .Append(item.Descending ? '1' : '0');
            foreach (int index in input) packed.Append((char)(64 + item.Keys[index])).Append((char)(64 + index));
            foreach (int index in input.OrderBy(Key)) packed.Append((char)(64 + index));
        }
        string table = packed.ToString().Replace("\\", "\\\\", StringComparison.Ordinal);
        Run($$"""
            using Standard::Collections;
            using Standard::Memory;

            struct Entry { public int Key; public int Id; }
            bool descending = false;
            int Compare(immutable(Entry)* first, immutable(Entry)* second)
            {
                int order = 0;
                if (first.Key < second.Key) order = -7;
                if (first.Key > second.Key) order = 19;
                if (descending) return -order;
                return order;
            }
            int main()
            {
                readonly(char)* table = "{{table}}";
                nuint at = 0;
                for (int sample = 0; sample < {{cases.Count}}; sample++)
                {
                    nuint count = (nuint)(table[at++] - '@');
                    nuint middle = (nuint)(table[at++] - '@');
                    descending = table[at++] == '1';
                    Entry[50] data;
                    data[0].Key = 12345;
                    data[count + 1].Key = -12345;
                    for (nuint index = 0; index < count; index++)
                    {
                        data[index + 1].Key = (int)(table[at++] - '@');
                        data[index + 1].Id = (int)(table[at++] - '@');
                    }
                    InPlaceMerge<Entry>(new Span<Entry>(&data[1], count), middle, Compare);
                    if (data[0].Key != 12345 || data[count + 1].Key != -12345) return 1;
                    for (nuint index = 0; index < count; index++)
                        if (data[index + 1].Id != (int)(table[at++] - '@')) return 2;
                }
                if (at != {{packed.Length}}) return 3;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NoncopyableOwnersSurviveEveryComparisonFailureWithoutLosingIdentity(int optimization)
        => Run("""
            using Standard;
            using Standard::Collections;
            using Standard::Memory;

            int live = 0;
            int comparisons = 0;
            int budget = -1;
            struct Owner
            {
                public int Key;
                private int* address;
                public Owner(int key) nothrow { Key = key; address = &Key; live++; }
                public move Owner(Owner* source) nothrow { Key = source.Key; address = &Key; }
                public bool Valid() nothrow => address == &Key && *address == Key;
                ~Owner() { live--; }
            }
            int Compare(immutable(Owner)* first, immutable(Owner)* second)
            {
                if (budget == 0) throw new Exception("merge comparison failed", 77);
                if (budget > 0) budget--;
                comparisons++;
                if (first.Key < second.Key) return -7;
                if (first.Key > second.Key) return 19;
                return 0;
            }
            int Run(int limit)
            {
                Owner[8] data = [new Owner(0), new Owner(2), new Owner(4), new Owner(6),
                    new Owner(1), new Owner(3), new Owner(5), new Owner(7)];
                budget = limit;
                comparisons = 0;
                bool caught = false;
                try { InPlaceMerge<Owner>(new Span<Owner>(data, 8), 4, Compare); }
                catch (Exception* error) { caught = true; if (error.GetCode() != 77) return 1; }
                budget = -1;
                if (caught != (limit >= 0) || live != 8) return 2;
                int[8] seen = [0, 0, 0, 0, 0, 0, 0, 0];
                for (nuint index = 0; index < 8; index++)
                {
                    if (!data[index].Valid() || data[index].Key < 0 || data[index].Key >= 8) return 3;
                    seen[data[index].Key]++;
                    if (!caught && data[index].Key != (int)index) return 4;
                }
                for (int key = 0; key < 8; key++) if (seen[key] != 1) return 5;
                return 0;
            }
            int main()
            {
                if (Run(-1) != 0 || live != 0) return 1;
                int count = comparisons;
                for (int failure = 0; failure < count; failure++)
                    if (Run(failure) != 0 || live != 0) return 2;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BoundaryValidationEmptyRangesAndDefaultOrderingMatchTheContract(int optimization)
        => Run("""
            using Standard;
            using Standard::Collections;
            using Standard::Memory;

            int comparisons = 0;
            int Unexpected(immutable(int)* first, immutable(int)* second)
            {
                comparisons++;
                throw new Exception("unexpected comparison", 77);
            }
            int main()
            {
                Span<int> empty = new(((nuint)1).reinterpret<int*>(), 0);
                InPlaceMerge<int>(empty, 0, Unexpected);
                bool caught = false;
                try { InPlaceMerge<int>(empty, (nuint)0 - (nuint)1, Unexpected); }
                catch (Exception* error) { caught = true; if (error.GetCode() != 3) return 1; }
                if (!caught || comparisons != 0) return 2;
                int[4] data = [1, 3, 2, 4];
                Span<int> values = new(data, 4);
                InPlaceMerge<int>(values, 0, Unexpected);
                InPlaceMerge<int>(values, 4, Unexpected);
                caught = false;
                try { InPlaceMerge<int>(values, 5, Unexpected); }
                catch (Exception* error) { caught = true; if (error.GetCode() != 3) return 3; }
                if (!caught || comparisons != 0 || data[1] != 3 || data[2] != 2) return 4;
                InPlaceMerge<int>(values, 2);
                for (nuint index = 0; index < 4; index++) if (values[index] != (int)index + 1) return 5;
                return 0;
            }
            """, optimization);

    private static void Run(string program, int optimization)
    {
        var result = CompilerTestHelper.Run(Sources(program), optimization);
        Assert.True(result.ExitCode == 0, $"InPlaceMerge exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
