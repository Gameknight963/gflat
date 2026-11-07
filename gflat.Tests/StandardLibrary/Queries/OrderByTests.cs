using System.Text;

namespace gflat.Tests.Std.Queries;

public sealed partial class OrderByTests
{
    private static IEnumerable<SourceFile> Sources(string code) => StandardTestSources.Combine(StandardTestSources.Values,
        StandardTestSources.Load("core/Collections/Comparer.gf", "core/Queries/OrderBySource.gf",
            "core/Queries/enumerable_order_by.gf", "core/Queries/enumerable_slice.gf",
            "core/Queries/SliceSource.gf"))
        .Append(new SourceFile("query-order-by.gf", code));

    private static void Run(string code, int optimization)
    {
        var result = CompilerTestHelper.Run(Sources(code), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("QUERY_ORDER_BY.md", 0);

    [Fact]
    public void BufferedElementsAreImmutable()
    {
        var error = Assert.Throws<gflat.CompileExceptions.TypeCheckException>(() => Compiler.Emit(Sources("""
            using Standard::Memory;
            using Standard::Queries;

            struct Item
            {
                public int Id;
                public Item(int id) { Id = id; }
            }
            int Key(Item value) => value.Id;
            int main()
            {
                Item[1] input = [new Item(1)];
                var iterator = new Span<Item>(&input[0], 1).AsEnumerable().OrderBy(Key).GetEnumerator();
                iterator.MoveNext();
                iterator.Current.Id = 99;
                return 0;
            }
            """)));
        Assert.Contains("readonly", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void StableOrderingAndCustomComparisonsMatchLinqAcrossRepeatedEnumerations(int optimization)
    {
        var random = new Random(927195);
        var cases = Enumerable.Range(0, 65).Select(length => Enumerable.Range(0, length)
            .Select(id => id * 16 + random.Next(8)).ToArray()).ToList();
        cases.Add(Enumerable.Range(0, 64).Select(id => id * 16).ToArray());
        cases.Add(Enumerable.Range(0, 64).Select(id => id * 16 + 7 - id % 8).ToArray());
        using var packet = new MemoryStream();
        using (var writer = new BinaryWriter(packet, Encoding.UTF8, true))
        foreach (int[] input in cases)
        {
            writer.Write(input.Length);
            foreach (int value in input) writer.Write(value);
            foreach (int value in input.OrderBy(value => value % 16)) writer.Write(value);
            foreach (int value in input.OrderByDescending(value => value % 16)) writer.Write(value);
        }
        string hex = Convert.ToHexString(packet.ToArray());
        Run($$"""
            using Standard;
            using Standard::Collections;
            using Standard::Memory;
            using Standard::Queries;

            int calls = 0;
            int Key(int value) { calls++; return value % 16; }
            int Compare(immutable(int)* left, immutable(int)* right)
            {
                if (*left < *right) return -2147483647 - 1;
                if (*left > *right) return 2147483647;
                return 0;
            }
            int Digit(char value)
            {
                if (value <= '9') return value - '0';
                return value - 'A' + 10;
            }
            int Read(readonly(char)* data, ref nuint at)
            {
                uint result = 0u;
                for (int shift = 0; shift < 32; shift += 8)
                {
                    int value = Digit(data[at]) * 16 + Digit(data[at + 1]);
                    at += 2;
                    result = result | ((uint)value << shift);
                }
                return (int)result;
            }
            int main()
            {
                readonly(char)* data = "{{hex}}";
                nuint at = 0;
                for (int row = 0; row < {{cases.Count}}; row++)
                {
                    nuint count = (nuint)Read(data, ref at);
                    int[64] input = default(int[64]);
                    int[64] ascending = default(int[64]);
                    int[64] descending = default(int[64]);
                    for (nuint i = 0; i < count; i++) input[i] = Read(data, ref at);
                    for (nuint i = 0; i < count; i++) ascending[i] = Read(data, ref at);
                    for (nuint i = 0; i < count; i++) descending[i] = Read(data, ref at);
                    var source = new Span<int>(&input[0], count).AsEnumerable();
                    var query = source.OrderBy(Key);
                    var reverse = source.OrderByDescending(Key, Compare);
                    for (int pass = 0; pass < 2; pass++)
                    {
                        calls = 0;
                        var first = query.GetEnumerator();
                        var second = reverse.GetEnumerator();
                        var third = Standard::Queries::OrderBy(source, Key, Compare).GetEnumerator();
                        var fourth = Standard::Queries::OrderByDescending(source, Key).GetEnumerator();
                        if (calls != 0) return 1;
                        for (nuint i = 0; i < count; i++)
                        {
                            if (!first.MoveNext() || !second.MoveNext() || !third.MoveNext() || !fourth.MoveNext()) return 2;
                            if (first.Current != ascending[i] || third.Current != ascending[i]
                                || second.Current != descending[i] || fourth.Current != descending[i]) return 3;
                            if (first.Current != first.Current || calls != (int)count * 4) return 4;
                        }
                        if (first.MoveNext() || second.MoveNext() || third.MoveNext() || fourth.MoveNext()) return 5;
                        if (calls != (int)count * 4) return 6;
                        if (first.MoveNext() || second.MoveNext()) return 7;
                        bool caught = false;
                        try { first.Current; }
                        catch (Exception* error) { caught = true; }
                        if (!caught) return 8;
                    }
                }
                if (at != {{hex.Length}}) return 9;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CursorsSnapshotIndependentlyAndTakeZeroDoesNotStartTheSource(int optimization)
        => Run("""
            using Standard;
            using Standard::Collections;
            using Standard::Memory;
            using Standard::Queries;

            int starts = 0;
            int closes = 0;
            int reads = 0;
            int calls = 0;
            int phase = 0;
            int failure = 0;
            struct Source
            {
                public Enumerator GetEnumerator()
                {
                    starts++;
                    if (failure == 1) throw new Exception("GetEnumerator", 31);
                    return new Enumerator();
                }
                public struct Enumerator
                {
                    private int index;
                    public Enumerator() { index = 0; }
                    public bool MoveNext()
                    {
                        index++;
                        if (failure == 2 && index == 2) throw new Exception("MoveNext", 32);
                        return index <= 3;
                    }
                    public int Current
                    {
                        get
                        {
                            reads++;
                            if (failure == 3 && index == 2) throw new Exception("Current", 33);
                            return (4 - index) * 16 + phase;
                        }
                    }
                    ~Enumerator() { closes++; }
                }
            }
            int Key(int value)
            {
                calls++;
                if (starts != closes) throw new Exception("Source iterator still alive");
                return value;
            }
            int main()
            {
                var source = new Enumerable<int, Source, Source::Enumerator>(new Source());
                var query = source.OrderBy(Key);
                foreach (int value in query.Take(0)) return 1;
                if (starts != 0 || calls != 0) return 2;
                var first = query.GetEnumerator();
                bool invalid = false;
                try { first.Current; } catch (Exception* error) { invalid = true; }
                if (!invalid || starts != 0) return 3;
                if (!first.MoveNext() || first.Current != 16 || reads != 3 || calls != 3) return 4;
                phase = 1;
                var second = query.GetEnumerator();
                if (!second.MoveNext() || second.Current != 17 || calls != 6 || reads != 6) return 5;
                if (first.Current != 16 || !first.MoveNext() || first.Current != 32
                    || !first.MoveNext() || first.Current != 48 || first.MoveNext()) return 6;
                if (starts != 2 || closes != 2) return 7;
                for (failure = 1; failure <= 3; failure++)
                {
                    var failed = query.GetEnumerator();
                    int code = 0;
                    try { failed.MoveNext(); } catch (Exception* error) { code = error.GetCode(); }
                    int before = starts;
                    if (code != 30 + failure || failed.MoveNext() || starts != before) return 8;
                    if (calls != 6) return 9;
                }
                return 0;
            }
            """, optimization);
}
