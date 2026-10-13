using gflat.CompileExceptions;
using Xunit;

namespace gflat.Tests;

public class OptionalParameterTests
{
    [Theory]
    [InlineData("int F(int x = 40, int y = 2) => x + y;", "return F();")]
    [InlineData("int F(int x, int y = 2) => x + y;", "return F(40);")]
    [InlineData("int F(int x = 1) => x;", "return F(42);")]
    [InlineData("int F(int x = 6 * 7) => x;", "return F();")]
    [InlineData("const int N = 40; int F(int x = N + 2) => x;", "return F();")]
    [InlineData("enum E { Value = 42 } int F(E x = E::Value) => (int)x;", "return F();")]
    [InlineData("int F(int x = default) => x + 42;", "return F();")]
    [InlineData("int F(char[2] x = \"*\") => (int)x[0];", "return F();")]
    [InlineData("int F(int[2] x = [40, 2]) => x[0] + x[1];", "return F();")]
    [InlineData("int F(float[2] x = [40.0f, 2.0f]) => (int)(x[0] + x[1]);", "return F();")]
    [InlineData("const int C() => 42; int F(int x = C()) => x;", "return F();")]
    [InlineData("int F(double x = 42.0) => (int)x;", "return F();")]
    [InlineData("int F(nuint x = 42) => (int)x;", "return F();")]
    [InlineData("int F(bool x = true, char y = '*') { if (x) return (int)y; return 0; }", "return F();")]
    [InlineData("int F(int*? p = null) { if (p == null) return 42; return 0; }", "return F();")]
    [InlineData("int F(readonly(char)* p = \"*\") => (int)p[0];", "return F();")]
    [InlineData("int F(int x = 42) => x;", "int(int)* p = &F; return p(42);")]
    [InlineData("struct S { public int F(int x = 42) => x; }", "S s = new S(); return s.F();")]
    [InlineData("class S { public static int F(int x = 42) => x; }", "return S::F();")]
    [InlineData("struct S { public int x; public S(int x = 42) { this.x = x; } }", "S s = new S(); return s.x;")]
    [InlineData("class S { public int x; public S(int x = 42) { this.x = x; } }", "S s = new S(); return s.x;")]
    [InlineData("class B { public int x; public B(int x = 42) { this.x = x; } } class D : B { public D() : base() {} }", "D d = new D(); return d.x;")]
    [InlineData("class B { public int x; public B(int x = 42) { this.x = x; } } class D : B { public D() {} }", "D d = new D(); return d.x;")]
    [InlineData("class B { public int x; public B(int x = 42) { this.x = x; } } class D : B {}", "D d = new D(); return d.x;")]
    [InlineData("interface I { int F(int x = 42); } struct S : I { public int F(int x) => x; }", "S s = new S(); I* p = &s; return p.F();")]
    [InlineData("class B { public virtual int F(int x = 42) => x; } class D : B { public override int F(int x = 1) => x; }", "D d = new D(); B* p = &d; return p.F();")]
    [InlineData("T F<T>(T value, int count = 2) => value;", "return F(42);")]
    [InlineData("int F(int x) => x; int F(int x, int y = 9) => y;", "return F(42);")]
    [InlineData("int F(int x = 42) => x; int F(int x = 1, int y = 2) => y;", "return F(42);")]
    public void DefaultsAreInsertedForTheSelectedDeclaration(string declarations, string body)
        => Assert.Equal(42, CompilerTestHelper.Run(declarations + " int main() { " + body + " }").ExitCode);

    [Fact]
    public void ForwardDeclarationDefaultDoesNotUseCallerParameters()
        => Assert.Equal(42, CompilerTestHelper.Run("int main() { int value = 1; return F(); } int F(int value = 42) => value;").ExitCode);

    [Fact]
    public void DefaultConstantComesFromDeclarationNotCallerScope()
        => Assert.Equal(42, CompilerTestHelper.Run("const int N = 42; int main() { const int N = 1; return F(); } int F(int value = N) => value;").ExitCode);

    [Fact]
    public void DefaultConstantCanBeDeclaredLaterAndInAnotherNamespace()
        => Assert.Equal(42, CompilerTestHelper.Run("int main() => API::F(); namespace API { int F(int value = Constants::N) => value; } namespace Constants { const int N = 42; }").ExitCode);

    [Fact]
    public void GenericClassKeepsConstructorDefaults()
        => Assert.Equal(42, CompilerTestHelper.Run("struct S<T> { public T x; public S(T value, int count = 2) { x = value; } } int main() { S<int> s = new S<int>(42); return s.x; }").ExitCode);

    [Fact]
    public void DefaultTypeNamesDoNotUseCallerAliases()
        => Assert.Equal(42, CompilerTestHelper.Run("alias Size = int; int main() { alias Size = long; return F(); } int F(int value = sizeof(Size) + 38) => value;").ExitCode);

    [Fact]
    public void DefaultsRemainAvailableDuringConstantEvaluation()
    {
        var (ast, checker) = CompilerTestHelper.Check("const int F(int x = 42) => x; const int Answer = F();");
        Assert.True(checker.TryGetConstValueByName("Answer", out var value));
        Assert.Equal(new gflat.comptime.ConstValue.Integer(42), value);
    }

    [Fact]
    public void ExternDefaultPreservesNativeSignature()
    {
        var result = CompilerTestHelper.Run("extern int putchar(int c = 42); int main() { putchar(); return 0; }");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("*", result.StandardOutput);
    }

    [Theory]
    [InlineData("int F(int x = 1, int y) => x;", "Required parameters")]
    [InlineData("int F(int x = true) => x;", "Default for parameter")]
    [InlineData("int G() => 1; int F(int x = G()) => x;", "compile-time constant")]
    [InlineData("int F(int x, int y = x) => y;", "Default for parameter")]
    [InlineData("void F(ref int x = 1) {}", "Default arguments")]
    [InlineData("int F(int* p = null) => 0;", "Default for parameter")]
    [InlineData("int F(int* p = default) => 0;", "cannot be default initialized")]
    [InlineData("interface I { int F(int x = true); }", "Default for parameter")]
    [InlineData("struct S { public int this[int i = 0] { get => i; } }", "Default arguments")]
    [InlineData("int main() { int(int)* f = (int x = 1) => x; return 0; }", "Lambda defaults")]
    [InlineData("int F(int x = 1) => x; int main() { int(int)* p = &F; return p(); }", "expects")]
    [InlineData("int F(int x = 1) => x; int F(double x = 2.0) => 0; int main() => F();", "ambiguous")]
    public void InvalidDefaultsAndCallsAreRejected(string source, string diagnostic)
        => Assert.Contains(diagnostic, Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(source)).Message);
}
