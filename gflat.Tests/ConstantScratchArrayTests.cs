using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ConstantScratchArrayTests
{
    private const string Types = """
        struct Cell {
            public readonly(char)* Text;
            public const Cell(readonly(char)* text) nothrow { Text = text; }
        }
        struct Group {
            public Cell[2] Items;
            public const Group(Cell[2] items) nothrow { Items = items; }
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ConstructedElementsFillScratchWithoutDefaultConstructingThem(int optimization)
        => Run(Types + """
            const Cell[2] Build() nothrow {
                Cell[2] scratch;
                scratch[0] = new Cell("left");
                scratch[1] = new Cell("right");
                return scratch;
            }
            const Cell[2] Values = Build();
            const Group MakeGroup() nothrow => new Group(Build());
            const Group Result = MakeGroup();
            int main() nothrow {
                if (Values[0].Text[0] != 'l' || Values[1].Text[0] != 'r') return 1;
                if (Result.Items[0].Text[0] != 'l' || Result.Items[1].Text[0] != 'r') return 2;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NestedScratchAndElementUpdatesUseInitializedStorage(int optimization)
        => Run("""
            const int[2][2] Build() nothrow {
                int[2][2] scratch;
                scratch[0][0] = 1; scratch[0][1] = 2;
                scratch[1][0] = 3; scratch[1][1] = 4;
                scratch[1][0] += 10;
                return scratch;
            }
            const int[2][2] Values = Build();
            int main() nothrow {
                if (Values[0][0] != 1 || Values[0][1] != 2 || Values[1][0] != 13 || Values[1][1] != 4) return 3;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData("const int Read(){ int[2] values; values[0]=7; return values[1]; } const int Result=Read();")]
    [InlineData("const int Read(){ int[2] values; values[0]=7; values[1]++; return values[0]; } const int Result=Read();")]
    [InlineData("const int Read(){ int[2] values; values[0]=7; foreach(int value in values){ if(value==0)return 0; } return 1; } const int Result=Read();")]
    public void ReadingAnUninitializedElementIsRejected(string source)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(source + " int main()=>0;"));
        Assert.Contains("uninitialized array element", error.Description);
    }

    [Theory]
    [InlineData("const Cell[2] Build(){ Cell[2] values; values[0]=new Cell(\"filled\"); return values; } const Cell[2] Result=Build();")]
    [InlineData("const Group Build(){ Cell[2] values; values[0]=new Cell(\"filled\"); return new Group(values); } const Group Result=Build();")]
    [InlineData("const readonly(Cell)* Build(){ Cell[2] values; values[0]=new Cell(\"filled\"); return values; } const readonly(Cell)* Result=Build();")]
    public void PartiallyInitializedStorageCannotEscape(string source)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Types + source + " int main()=>0;"));
        Assert.Contains("Uninitialized array storage cannot escape", error.Description);
    }

    private static void Run(string source, int optimization)
        => Assert.Equal(0, CompilerTestHelper.Run(source, optimization).ExitCode);
}
