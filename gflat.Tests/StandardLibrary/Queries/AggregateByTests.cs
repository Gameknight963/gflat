using System.Text;

namespace gflat.Tests.Std.Queries;

public sealed partial class AggregateByTests
{
    private static void Run(string program, int optimization)
    {
        var sources = StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/Collections/HashMap.gf", "core/Queries/enumerable_aggregate_by.gf"))
            .Append(new SourceFile("aggregate-by-tests.gf", program));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"AggregateBy exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("QUERY_AGGREGATE_BY.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EmptyBorrowedInputDoesNotAllocateOrCallCallbacks(int optimization)
        => Run("""
            using Standard;
            using Standard::Collections;
            using Standard::Memory;
            using Standard::Queries;

            extern "C" void free(void* storage);
            int attempts = 0;
            int callbacks = 0;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes) { attempts++; return null; }
                public replace void Free(void* storage) nothrow { free(storage); }
            }
            int Key(int value) { callbacks++; return value; }
            long Seed(int key) { callbacks++; return (long)key; }
            long Reduce(long total, int value) { callbacks++; return total + (long)value; }
            int main()
            {
                Span<int> empty = new();
                HashMap<int, long> fixedSeed = empty.AsEnumerable().AggregateBy(Key, 10l, Reduce);
                HashMap<int, long> factorySeed = empty.AsEnumerable().AggregateBy(Key, Seed, Reduce);
                if (!fixedSeed.IsEmpty || !factorySeed.IsEmpty || attempts != 0 || callbacks != 0) return 1;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BothSeedFormsMatchLinqAndEvaluateEachElementOnce(int optimization)
    {
        List<int[]> samples = [[], [1], [3, 3, 3], [-1, 0, 1, int.MinValue, int.MaxValue]];
        var random = new Random(127833);
        for (int index = 0; index < 64; index++)
            samples.Add(Enumerable.Range(0, random.Next(50)).Select(_ => random.Next(-1000, 1001)).ToArray());
        using var packet = new MemoryStream();
        using (var writer = new BinaryWriter(packet, Encoding.UTF8, true))
            foreach (int[] input in samples)
            {
                var fixedSeed = input.AggregateBy(value => value % 7, 10L, (total, value) => unchecked(total * 31 + value)).ToDictionary();
                var factorySeed = input.AggregateBy(value => value % 7, key => (long)key * 100, (total, value) => unchecked(total * 31 + value)).ToDictionary();
                writer.Write((byte)input.Length);
                writer.Write((byte)fixedSeed.Count);
                foreach (int value in input) writer.Write(value);
                foreach (var pair in fixedSeed)
                {
                    writer.Write(pair.Key);
                    writer.Write(pair.Value);
                    writer.Write(factorySeed[pair.Key]);
                }
            }
        string hex = Convert.ToHexString(packet.ToArray()).ToLowerInvariant();
        Run($$"""
            using Standard;
            using Standard::Collections;
            using Standard::Queries;

            int reads = 0;
            int moves = 0;
            int starts = 0;
            int ends = 0;
            int selections = 0;
            int reductions = 0;
            int seeds = 0;
            struct Source
            {
                private int* values;
                private nuint count;
                public Source(int* input, nuint length) { values = input; count = length; }
                public Iterator GetEnumerator() => new Iterator(values, count);
                public struct Iterator
                {
                    private int* values;
                    private nuint count;
                    private nuint position;
                    public Iterator(int* input, nuint length) { values = input; count = length; position = 0; starts++; }
                    public bool MoveNext() { moves++; if (position == count) return false; position++; return true; }
                    public int Current { get { reads++; return values[position - 1]; } }
                    ~Iterator() { ends++; }
                }
            }
            int Key(int value) { selections++; return value % 7; }
            long Seed(int key) { seeds++; return (long)key * 100l; }
            long Fold(long total, int value) { reductions++; return total * 31l + (long)value; }
            int ReadByte(readonly(char)* data, ref nuint position)
            {
                int result = 0;
                for (int i = 0; i < 2; i++)
                {
                    char digit = data[position]; position++;
                    int value = (int)digit - (int)'0';
                    if (digit >= 'a') value = (int)digit - (int)'a' + 10;
                    result = result * 16 + value;
                }
                return result;
            }
            long ReadLong(readonly(char)* data, ref nuint position, int bytes)
            {
                ulong bits = 0ul;
                for (int i = 0; i < bytes; i++) bits = bits | ((ulong)ReadByte(data, ref position) << (i * 8));
                return (long)bits;
            }
            int main()
            {
                readonly(char)* data = "{{hex}}";
                nuint position = 0;
                int[50] values = default(int[50]);
                for (int sample = 0; sample < {{samples.Count}}; sample++)
                {
                    int count = ReadByte(data, ref position);
                    int groups = ReadByte(data, ref position);
                    for (int i = 0; i < count; i++) values[i] = (int)ReadLong(data, ref position, 4);
                    var query = new Enumerable<int, Source, Source::Iterator>(new Source(values, (nuint)count));
                    reads = 0; moves = 0; selections = 0; reductions = 0; seeds = 0;
                    HashMap<int, long> fixedSeed = query.AggregateBy(Key, 10l, Fold);
                    if (reads != count || moves != count + 1 || selections != count || reductions != count || seeds != 0 || starts != ends) return 1;
                    reads = 0; moves = 0; selections = 0; reductions = 0;
                    HashMap<int, long> factorySeed = query.AggregateBy(Key, Seed, Fold);
                    if (reads != count || moves != count + 1 || selections != count || reductions != count || seeds != groups || starts != ends) return 2;
                    HashMap<int, long> freeFixed = AggregateBy(query, Key, 10l, Fold);
                    HashMap<int, long> freeFactory = AggregateBy(query, Key, Seed, Fold);
                    if (fixedSeed.Count != (nuint)groups || factorySeed.Count != (nuint)groups || freeFixed.Count != (nuint)groups || freeFactory.Count != (nuint)groups) return 3;
                    for (int i = 0; i < groups; i++)
                    {
                        int key = (int)ReadLong(data, ref position, 4);
                        long expectedFixed = ReadLong(data, ref position, 8);
                        long expectedFactory = ReadLong(data, ref position, 8);
                        if (fixedSeed[&key] != expectedFixed || freeFixed[&key] != expectedFixed) return 4;
                        if (factorySeed[&key] != expectedFactory || freeFactory[&key] != expectedFactory) return 5;
                    }
                }
                if (position != (nuint){{hex.Length}} || starts != ends) return 6;
                return 0;
            }
            """, optimization);
    }
}
