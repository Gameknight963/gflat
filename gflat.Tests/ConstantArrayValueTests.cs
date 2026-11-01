namespace gflat.Tests;

public sealed class ConstantArrayValueTests
{
    private const string Values = """
        struct Cell {
            public int Value;
            public const Cell(int value) nothrow { Value = value; }
        }
        const Cell[2] Build() nothrow {
            Cell[2] values;
            values[0] = new Cell(17);
            values[1] = new Cell(25);
            return values;
        }
        struct Pair {
            public Cell[2] Values;
            public const Pair(Cell[2] values) nothrow { Values = values; }
        }
        int Sum(Cell[2] values) nothrow => values[0].Value + values[1].Value;
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ConstantArrayCallsSupplyAggregateConstructorAndFunctionArguments(int optimization)
        => Run(Values + """
            const Pair Construct() nothrow => new Pair(Build());
            int main() nothrow {
                Pair pair = new Pair(Build());
                Pair constant = Construct();
                if (Sum(Build()) != 42 || Sum(pair.Values) != 42 || Sum(constant.Values) != 42) return 1;
                pair.Values[0].Value = 9;
                if (Sum(Build()) != 42 || Sum(pair.Values) != 34) return 2;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ConstantArrayValuesRemainValuesAcrossReturnsAssignmentAndAddressMaterialization(int optimization)
        => Run(Values + """
            Cell[2] Forward() nothrow => Build();
            int main() nothrow {
                Cell[2] values = Build();
                values = Forward();
                if (Sum(values) != 42 || Build()[1].Value != 25) return 3;
                Cell[2]* materialized = &Build();
                if ((*materialized)[0].Value != 17 || (*materialized)[1].Value != 25) return 4;
                values[1].Value = 10;
                if (Sum(Forward()) != 42 || Sum(values) != 27) return 5;
                return 0;
            }
            """, optimization);

    private static void Run(string source, int optimization)
        => Assert.Equal(0, CompilerTestHelper.Run(source, optimization).ExitCode);
}
