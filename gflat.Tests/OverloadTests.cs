using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class OverloadTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExactMatchWinsIndependentlyOfDeclarationOrder(bool reverse)
    {
        string first = "public int F(int n) => 20;", second = "public int F(long n) => 22;";
        string methods = reverse ? second + first : first + second;
        Assert.Equal(42, CompilerTestHelper.Run("struct S { " + methods + " } int main() { S s = new S(); return s.F(1) + s.F(1L); }").ExitCode);
        Assert.Equal(42, CompilerTestHelper.Run(methods + " int main() => F(1) + F(1L);").ExitCode);
    }

    [Fact]
    public void StaticAndNamespacedOverloadsExecute()
        => Assert.Equal(42, CompilerTestHelper.Run("namespace N { public int F(int n) => 20; public int F(long n) => 1; } struct S { public static int F(int n) => 1; public static int F(long n) => 22; } int main() => N::F(1) + S::F(1L);").ExitCode);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StaticOverloadsCheckTheSelectedMethodsAccessibility(bool reverse)
    {
        string first = "public static int F(int n) => 42;", second = "private static int F(long n) => 1;";
        string source = "struct S { " + (reverse ? second + first : first + second) + " } ";
        Assert.Equal(42, CompilerTestHelper.Run(source + "int main() => S::F(1);").ExitCode);
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(source + "int main() => S::F(1L);"));
        Assert.Contains("private", error.Message);
    }

    [Fact]
    public void ConstructorsPreferExactTypes()
        => Assert.Equal(42, CompilerTestHelper.Run("struct S { public int value; public S(long x) { value = 1; } public S(int x) { value = 42; } } int main() { S s = new S(1); return s.value; }").ExitCode);

    [Fact]
    public void ConstEvaluationUsesSelectedOverload()
        => Assert.Equal(42, CompilerTestHelper.Run("const int F(int x) => 42; const int F(long x) => 1; int main() { const int value = F(1); return value; }").ExitCode);

    [Fact]
    public void InterfaceSelectsMatchingConcreteOverload()
        => Assert.Equal(42, CompilerTestHelper.Run("interface I { int F(int n); } struct S : I { public int F(int n) => 42; public int F(long n) => 1; } int main() { S s = new S(); I* p = &s; return p.F(1); }").ExitCode);

    [Fact]
    public void GenericOwnerOverloadsExecute()
        => Assert.Equal(42, CompilerTestHelper.Run("struct S<T> { public int F(int n) => 42; public int F(long n) => 1; } int main() { S<int> s = new S<int>(); return s.F(1); }").ExitCode);

    [Theory]
    [InlineData("int F(int x, long y) => 1; int F(long x, int y) => 2; int main() => F(1, 1);")]
    [InlineData("int F(int* x) => 1; int F(readonly(int)* x) => 2; int main() => 0;")]
    [InlineData("struct S { public int F() => 1; public readonly int F() => 2; } int main() => 0;")]
    [InlineData("int F(int x) => 1; int F(long x) => 2; int main() { int(int)* p = &F; return p(1); }")]
    public void AmbiguousAndUnsupportedOverloadFormsAreDiagnosed(string source)
        => Assert.Throws<TypeCheckException>(() => Compiler.Check(source));
}
