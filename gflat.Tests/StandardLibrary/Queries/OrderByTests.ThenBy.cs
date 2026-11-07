using System.Text;

namespace gflat.Tests.Std.Queries;

public sealed partial class OrderByTests
{
    [Fact]
    public void SecondaryOrderingRequiresAnOrderedQuery()
    {
        var error = Assert.Throws<gflat.CompileExceptions.TypeCheckException>(() => Compiler.Emit(Sources("""
            using Standard::Memory;
            using Standard::Queries;

            int Key(int value) => value;
            int main()
            {
                int[1] input = [1];
                new Span<int>(&input[0], 1).AsEnumerable().ThenBy(Key);
                return 0;
            }
            """)));
        Assert.Contains("ThenBy", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void KeyLevelsCanHaveDifferentTypesAndRetainEarlierPriorities(int optimization)
        => Run("""
            using Standard::Memory;
            using Standard::Queries;
            using Standard::Text;

            struct Item
            {
                public int Group;
                public String Name;
                public int Id;
                public Item(int group, String name, int id) { Group = group; Name = move name; Id = id; }
            }
            int Group(Item value) => value.Group;
            String Name(Item value) => value.Name;
            long Id(Item value) => (long)value.Id;
            int main()
            {
                Item[4] input = [new Item(2, s"A", 1), new Item(1, s"B", 3),
                    new Item(1, s"B", 2), new Item(1, s"C", 5)];
                var query = new Span<Item>(&input[0], 4).AsEnumerable()
                    .OrderBy(Group).ThenByDescending(Name).ThenBy(Id);
                int[4] expected = [5, 2, 3, 1];
                nuint index = 0;
                foreach (Item value in query)
                {
                    if (value.Id != expected[index]) return 1;
                    index++;
                }
                if (index != 4 || !input[0].Name.Equals("A") || input[0].Id != 1) return 2;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 2)]
    public void ChainedDirectionsBranchingAndPrimaryResetMatchLinq(bool valueOrdering, int optimization)
    {
        var random = new Random(817395);
        var cases = Enumerable.Range(0, 65).Select(length => Enumerable.Range(0, length)
            .Select(id => id * 256 + random.Next(64) * 4).ToArray()).ToList();
        cases.Add(Enumerable.Range(0, 64).Select(id => id * 256).ToArray());
        using var packet = new MemoryStream();
        using (var writer = new BinaryWriter(packet, Encoding.UTF8, true))
        foreach (int[] input in cases)
        {
            writer.Write(input.Length);
            foreach (int value in input) writer.Write(value);
            foreach (int value in input.OrderBy(value => value / 64 % 4)
                .ThenByDescending(value => value / 16 % 4).ThenBy(value => value / 4 % 4)) writer.Write(value);
            foreach (int value in input.OrderBy(value => value / 64 % 4)
                .ThenBy(value => value / 16 % 4).ThenByDescending(value => value / 4 % 4)) writer.Write(value);
            foreach (int value in input.OrderByDescending(value => value / 64 % 4)
                .ThenBy(value => value / 16 % 4).ThenByDescending(value => value / 4 % 4)) writer.Write(value);
            foreach (int value in input.OrderBy(value => value / 64 % 4)
                .ThenBy(value => value / 16 % 4).OrderBy(value => value / 4 % 4)) writer.Write(value);
        }
        string hex = Convert.ToHexString(packet.ToArray());
        Run($$"""
            using Standard;
            using Standard::Memory;
            using Standard::Queries;

            int calls = 0;
            int Primary(int value) { calls++; return value / 64 % 4; }
            int Secondary(int value) { calls++; return value / 16 % 4; }
            int Third(int value) { calls++; return value / 4 % 4; }
            int Compare(immutable(int)* left, immutable(int)* right)
            {
                if (*left < *right) return -2147483647 - 1;
                if (*left > *right) return 2147483647;
                return 0;
            }
            int ComparePrimary(immutable(int)* left, immutable(int)* right)
            {
                int a = *left / 64 % 4;
                int b = *right / 64 % 4;
                return Compare(&a, &b);
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
                    int[64] first = default(int[64]);
                    int[64] second = default(int[64]);
                    int[64] third = default(int[64]);
                    int[64] fourth = default(int[64]);
                    for (nuint i = 0; i < count; i++) input[i] = Read(data, ref at);
                    for (nuint i = 0; i < count; i++) first[i] = Read(data, ref at);
                    for (nuint i = 0; i < count; i++) second[i] = Read(data, ref at);
                    for (nuint i = 0; i < count; i++) third[i] = Read(data, ref at);
                    for (nuint i = 0; i < count; i++) fourth[i] = Read(data, ref at);
                    var source = new Span<int>(&input[0], count).AsEnumerable();
                    var root = source.{{(valueOrdering ? "Order(ComparePrimary)" : "OrderBy(Primary)")}};
                    var a = Standard::Queries::ThenByDescending(root, Secondary).ThenBy(Third, Compare);
                    var b = Standard::Queries::ThenBy(root, Secondary, Compare).ThenByDescending(Third);
                    var c = Standard::Queries::ThenByDescending(
                        source.{{(valueOrdering ? "OrderDescending(ComparePrimary)" : "OrderByDescending(Primary)")}}.ThenBy(Secondary, Compare), Third, Compare);
                    var d = Standard::Queries::ThenBy(root, Secondary).OrderBy(Third);
                    for (int pass = 0; pass < 2; pass++)
                    {
                        calls = 0;
                        var ai = a.GetEnumerator();
                        var bi = b.GetEnumerator();
                        var ci = c.GetEnumerator();
                        var di = d.GetEnumerator();
                        if (calls != 0) return 1;
                        for (nuint i = 0; i < count; i++)
                        {
                            if (!ai.MoveNext() || !bi.MoveNext() || !ci.MoveNext() || !di.MoveNext()) return 2;
                            if (ai.Current != first[i] || bi.Current != second[i]
                                || ci.Current != third[i] || di.Current != fourth[i]) return 3;
                            if (ai.Current != ai.Current || calls != (int)count * {{(valueOrdering ? 8 : 12)}}) return 4;
                        }
                        if (ai.MoveNext() || bi.MoveNext() || ci.MoveNext() || di.MoveNext()) return 5;
                        if (calls != (int)count * {{(valueOrdering ? 8 : 12)}}) return 6;
                    }
                }
                if (at != {{hex.Length}}) return 7;
                return 0;
            }
            """, optimization);
    }
}
