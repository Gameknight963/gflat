using gflat.CompileExceptions;

namespace gflat.Tests;

public class WeakReplacementTests
{
    [Theory]
    [InlineData("weak int value() => 7;", 7)]
    [InlineData("weak int value() => 7; replace int value() => 42;", 42)]
    [InlineData("replace int value() => 42; weak int value() => 7;", 42)]
    [InlineData("weak int value() => missingDefaultDependency(); replace int value() => 42;", 42)]
    public void SelectedFunctionRuns(string declarations, int expected)
        => Assert.Equal(expected, CompilerTestHelper.Run(declarations + " int main() => value();").ExitCode);

    [Fact]
    public void ReopenedNamespacesAndInternalCallsSelectReplacement()
        => Assert.Equal(42, CompilerTestHelper.Run("namespace N { weak int value() => 7; int call() => value(); } namespace N { replace int value() => 42; } int main() => N::call();").ExitCode);

    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    public void InstanceMethodsCanBeReplaced(string kind)
        => Assert.Equal(42, CompilerTestHelper.Run(kind + " C { public int x; public weak int value() => 7; public replace int value() => x; } int main() { C c = new C(); c.x = 42; return c.value(); }").ExitCode);

    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    public void StaticMethodsCanBeReplaced(string kind)
        => Assert.Equal(42, CompilerTestHelper.Run("namespace N { " + kind + " C { public static weak int value(int x) => 7; public static replace int value(int x) => helper(x); private static int helper(int x) => x; } } int main() => N::C::value(42);").ExitCode);

    [Fact]
    public void VirtualReplacementAndDerivedOverrideRemainSeparate()
        => Assert.Equal(42, CompilerTestHelper.Run("class C { public virtual weak int value() => 7; public virtual replace int value() => 20; } class D : C { public override int value() => 22; } int main() { C c = new C(); D d = new D(); C* p = &d; return c.value() + p.value(); }").ExitCode);

    [Fact]
    public void GenericReplacementMatchesParametersByPosition()
        => Assert.Equal(42, CompilerTestHelper.Run("weak T identity<T>(T x) => x; replace U identity<U>(U x) => x; int main() => identity<int>(42);").ExitCode);

    [Theory]
    [InlineData("weak int f(int x) => 7; replace int f(int x) => x;", "f")]
    [InlineData("namespace N { weak int f(int x) => 7; replace int f(int x) => x; }", "N::f")]
    [InlineData("class C { public static weak int f(int x) => 7; public static replace int f(int x) => x; }", "C::f")]
    public void FunctionAddressesUseReplacement(string source, string target)
        => Assert.Equal(42, CompilerTestHelper.Run(source + " int main() { int(int)* fn = &" + target + "; return fn(42); }").ExitCode);

    [Fact]
    public void ConstEvaluationUsesReplacement()
        => Assert.Equal(42, CompilerTestHelper.Run("const weak int f() => 7; const replace int f() => 42; int main() { const int result = f(); return result; }").ExitCode);

    [Fact]
    public void AliasesMatchUnderlyingSignature()
        => Assert.Equal(42, CompilerTestHelper.Run("alias Number = int; weak Number f(Number x) => 7; replace int f(int x) => x; int main() => f(42);").ExitCode);

    [Fact]
    public void NestedTypeIsNotQualifiedTwiceDuringSelection()
        => Assert.Equal(42, CompilerTestHelper.Run("class Outer { public class Inner { public weak int f() => 7; public replace int f() => 42; } } int main() { Outer::Inner x = new Outer::Inner(); return x.f(); }").ExitCode);

    [Fact]
    public void GenericPointerShapesMatchWithoutEarlyInstantiation()
        => Assert.Equal(42, CompilerTestHelper.Run("class Box<T> { public T value; } weak T f<T>(Box<T>* x) => x.value; replace U f<U>(Box<U>* x) => x.value; int main() { Box<int> x = new Box<int>(); x.value = 42; return f<int>(&x); }").ExitCode);

    [Fact]
    public void SelectionEmitsOneOrdinaryDefinition()
    {
        string ir = CompilerTestHelper.EmitIr("weak int value() => 7; replace int value() => 42; int main() => value();");
        Assert.Equal(1, ir.Split("define i32 @gflat$value(").Length - 1);
        Assert.DoesNotContain("define weak", ir);
        Assert.DoesNotContain("ret i32 7", ir);
    }

    [Theory]
    [InlineData("replace int f() => 1;", "matching weak")]
    [InlineData("int f() => 1; int f() => 2;", "Duplicate definition")]
    [InlineData("weak int f() => 1; int f() => 2;", "requires replace")]
    [InlineData("weak int f() => 1; weak int f() => 2;", "Multiple weak")]
    [InlineData("weak int f() => 1; replace int f() => 2; replace int f() => 3;", "Multiple replacements")]
    [InlineData("weak int f() => 1; replace long f() => 2L;", "must match")]
    [InlineData("weak int f(int x) => 1; replace int f(long x) => 2;", "matching weak")]
    [InlineData("public weak int f() => 1; private replace int f() => 2;", "must match")]
    [InlineData("weak int f() => 1; replace int f() throws => 2;", "must match")]
    [InlineData("const weak int f() => 1; replace int f() => 2;", "must match")]
    [InlineData("weak int f(readonly char* p) => 1; replace int f(char* p) => 2;", "matching weak")]
    [InlineData("weak int f(void*? p) => 1; replace int f(void* p) => 2;", "matching weak")]
    [InlineData("class C { public weak int f() => 1; public static replace int f() => 2; }", "must match")]
    [InlineData("class C { public weak int f() => 1; } class D : C { public replace int f() => 2; }", "matching weak")]
    [InlineData("namespace N { weak int f() => 1; } namespace M { replace int f() => 2; }", "matching weak")]
    [InlineData("weak int x;", "ordinary functions")]
    [InlineData("weak class C {}", "ordinary functions")]
    [InlineData("class C { weak C() {} }", "ordinary functions")]
    [InlineData("class C { weak ~C() {} }", "ordinary functions")]
    [InlineData("abstract class C { public abstract weak int f(); }", "concrete function body")]
    [InlineData("weak replace int f() => 1;", "exactly one")]
    [InlineData("weak int f(int[2] x) => 1; replace int f(int[3] x) => 2;", "matching weak")]
    [InlineData("weak int f(const int x) => 1; replace int f(int x) => 2;", "must match")]
    [InlineData("class C { public readonly weak int f() => 1; public replace int f() => 2; }", "must match")]
    [InlineData("extern int f(); weak int f() => 1;", "extern declaration")]
    [InlineData("weak int f() => 1; int f(int x) => x;", "Overloading weak")]
    [InlineData("class C { public static virtual weak int f() => 1; }", "Static methods cannot")]
    [InlineData("interface I { weak int f(); }", "concrete function body")]
    public void InvalidReplacementsAreRejected(string source, string message)
        => Assert.Contains(message, Assert.Throws<TypeCheckException>(() => Compiler.Check(source + " int main() => 0;")).Message);

    [Theory]
    [InlineData("class C { int x; public static int f() => x; } int main() => C::f();")]
    [InlineData("class C { int f() => 1; public static int g() => f(); } int main() => C::g();")]
    [InlineData("class C { private static int f() => 1; } int main() => C::f();")]
    [InlineData("class C { public static int f(int x) => x; } int main() => C::f();")]
    [InlineData("class C { public static int f(int x) => x; } int main() => C::f(null);")]
    public void StaticMethodsEnforceReceiverAccessAndArguments(string source)
        => Assert.Throws<TypeCheckException>(() => Compiler.Check(source));
}
