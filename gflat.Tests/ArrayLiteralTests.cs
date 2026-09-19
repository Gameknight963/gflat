using gflat.CompileExceptions;

namespace gflat.Tests;

public class ArrayLiteralTests
{
    [Theory]
    [InlineData("int[3] a = [1, 2, 3]; return a[0] + a[1] + a[2];", 6)]
    [InlineData("int[] a = [1, 2, 3,]; a[1] = 7; return a[1];", 7)]
    [InlineData("int[2][2] a = [[1, 2], [3, 4]]; return a[1][0];", 3)]
    [InlineData("return [4, 8][1];", 8)]
    [InlineData("int sum = 0; foreach (int n in [2, 4, 6]) { sum += n; } return sum;", 12)]
    [InlineData("const int[3] a = [1, 2, 3]; return a[2];", 3)]
    [InlineData("int[2] a = [1, 2]; a = [3, 4]; return a[1];", 4)]
    [InlineData("int* p = [2, 9]; return p[1];", 9)]
    [InlineData("int[2] a = [0, 0]; int* p = a; p = [4, 8]; return p[0];", 4)]
    [InlineData("int* p = (int*)([4, 8]); return p[1];", 8)]
    [InlineData("const int[2] a = [1, 2]; int[2][2] b = [a, a]; return b[1][1];", 2)]
    [InlineData("char[3][2] words = [\"hi\", \"ho\"]; return (int)words[1][1];", 111)]
    [InlineData("const char[3][2] words = [\"hi\", \"ho\"]; return (int)words[0][1];", 105)]
    public void LiteralsExecute(string body, int expected)
        => Assert.Equal(expected, CompilerTestHelper.Run("int main() { " + body + " }").ExitCode);

    [Fact]
    public void LiteralsCanBeReturnedAndPassedToPointerParameters()
    {
        var result = CompilerTestHelper.Run("int[2] make() { return [3, 7]; } int read(int* a) { return a[1]; } int main() { int[2] a = make(); return read([a[0], a[1]]); }");
        Assert.Equal(7, result.ExitCode);
    }

    [Fact]
    public void ElementsEvaluateFromLeftToRight()
        => Assert.Equal(123, CompilerTestHelper.Run("int next(int* n) { *n += 1; return *n; } int main() { int n = 0; int[] a = [next(&n), next(&n), next(&n)]; return a[0]*100 + a[1]*10 + a[2]; }").ExitCode);

    [Theory]
    [InlineData("int[] a = [];", "at least one")]
    [InlineData("int[] a = [1, true];", "matching types")]
    [InlineData("int[3] a = [1, 2];", "Cannot assign")]
    [InlineData("int[2][2] a = [[1], [2, 3]];", "dimensions")]
    [InlineData("int x; int[] a = [x, 2];", "assigned")]
    public void InvalidLiteralsAreDiagnosed(string body, string message)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check("int main() { " + body + " return 0; }"));
        Assert.Contains(message, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private const string Resource = """
        extern int printf(readonly char* format, ...);
        struct Item {
            public int id;
            public Item(int value) { id = value; printf("N%d", id); }
            public ~Item() { printf("D%d", id); }
        }
        Item fail() throws { throw new* Exception(); }
        """;

    [Theory]
    [InlineData("Item[] a = [new Item(1), new Item(2)];", "N1N2D2D1")]
    [InlineData("[new Item(1), new Item(2)];", "N1N2D2D1")]
    [InlineData("try { Item[] a = [new Item(1), fail()]; } catch { printf(\"C\"); }", "N1D1C")]
    [InlineData("try { Item[] a = [fail(), new Item(2)]; } catch { printf(\"C\"); }", "C")]
    [InlineData("try { Item[2][2] a = [[new Item(1), new Item(2)], [new Item(3), fail()]]; } catch { printf(\"C\"); }", "N1N2N3D3D2D1C")]
    public void ResourceElementsCleanUpOnce(string body, string expected)
    {
        var result = CompilerTestHelper.Run(Resource + "int main() { " + body + " return 0; }");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expected, result.StandardOutput);
    }

    [Fact]
    public void ResourceElementsCannotBeCopied()
        => Assert.Throws<TypeCheckException>(() => Compiler.Check(Resource + "int main() { Item a = new Item(1); Item[] b = [a]; return 0; }"));

    [Fact]
    public void ConstantResourceArraysAreRejected()
        => Assert.Throws<TypeCheckException>(() => Compiler.Check("struct S { ~S() {} } int main() { const S[1] a = [new S()]; return 0; }"));

    [Fact]
    public void ResourceArrayReturnTransfersCleanup()
    {
        var result = CompilerTestHelper.Run(Resource + "Item[2] make() { return [new Item(1), new Item(2)]; } int main() { Item[] a = make(); printf(\"R\"); return 0; }");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("N1N2RD2D1", result.StandardOutput);
    }

    [Fact]
    public void FieldLiteralCleansUpWithContainingObject()
    {
        var result = CompilerTestHelper.Run(Resource + "struct Box { Item[2] items = [new Item(1), new Item(2)]; } int main() { Box b = new Box(); return 0; }");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("N1N2D2D1", result.StandardOutput);
    }

    [Fact]
    public void GenericBodiesCloneArrayLiterals()
        => Assert.Equal(7, CompilerTestHelper.Run("struct Box<T> { public T Get(T value) { T[] a = [value, value]; return a[1]; } } int main() { Box<int> b = new Box<int>(); return b.Get(7); }").ExitCode);

    [Fact]
    public void ArrayLiteralsWorkDuringConstantEvaluation()
        => Assert.Equal(9, CompilerTestHelper.Run("const int sum() { int n = 0; foreach (int v in [2, 3, 4]) { n += v; } return n; } int main() { const int value = sum(); return value; }").ExitCode);
}
