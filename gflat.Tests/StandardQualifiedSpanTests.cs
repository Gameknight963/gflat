using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class StandardQualifiedSpanTests
{
    private const string Cell = """
        using std; using std::Collections;
        struct Cell {
            public int* value;
            public Cell(int* storage) { value = storage; }
            public readonly void Write(int next) { *value = next; }
            public immutable int Read() => *value;
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ShallowElementsAllowPointeeMutationWhileDeepElementsPreserveReads(int optimization)
    {
        string code = Cell + """
            int main() {
                int value = 1; Vector<Cell> cells = new(); cells.Add(new Cell(&value));
                readonly(Vector<Cell>)* owner = &cells;
                Span<readonly(Cell)> shallow = owner.AsReadOnly();
                shallow[0].Write(42);
                Cell copied = shallow[0]; copied.Write(43);
                immutable(Vector<Cell>)* protectedOwner = &cells;
                Span<immutable(Cell)> deep = protectedOwner.AsImmutable();
                if (deep[0].Read() != 43 || shallow[0].Read() != 43) return 1;
                int[1] numbers = [42]; Span<int> mutable = new(numbers, 1);
                Span<readonly(int)> first = mutable.AsReadOnly();
                Span<immutable(int)> second = mutable.AsImmutable();
                if (first[0] != second[0]) return 2;
                String text = s"abc";
                Span<readonly(char)> shallowText = text.AsReadOnly();
                Span<readonly(char)> deepText = text.AsImmutable();
                if (shallowText.Data != deepText.Data || shallowText[0] != 'a') return 3;
                return 0;
            }
            """;
        Assert.Equal(0, CompilerTestHelper.Run(Sources(code), optimization).ExitCode);
    }

    [Theory]
    [InlineData("Span<immutable(Cell)> view=cells.AsImmutable();view[0].Write(2);")]
    [InlineData("Span<readonly(Cell)> view=cells.AsReadOnly();view[0].value=&value;")]
    [InlineData("Span<immutable(Cell)> view=cells.AsImmutable();Cell copy=view[0];")]
    [InlineData("immutable(Vector<Cell>)* owner=&cells;owner.AsReadOnly();")]
    public void ContainerViewsCannotDiscardTheirDeclaredProtection(string operation)
        => Assert.Throws<TypeCheckException>(() => Compiler.Check(Sources(Cell +
            "void Test(){int value=1;Vector<Cell> cells=new();cells.Add(new Cell(&value));" + operation + "}")));

    private static IEnumerable<SourceFile> Sources(string code)
        => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path))).Append(new SourceFile("main.gf", code));
}
