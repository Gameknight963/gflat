using gflat.CompileExceptions;

namespace gflat.Tests;

public class InitializationContractTests
{
    [Theory]
    [InlineData("struct S { int* p; public S() {} }")]
    [InlineData("struct S { int* p; public S(int* q, bool b) { if (b) p = q; } }")]
    [InlineData("struct S { int* p; public S(int* q, bool b) { if (b) return; p = q; } }")]
    [InlineData("struct S { int* p; public S(int* q, bool b) { while (b) p = q; } }")]
    [InlineData("struct S { int* p; public S(int* p) { p = p; } }")]
    [InlineData("struct S { int* p; public S(int* q) { int n = *p; p = q; } }")]
    [InlineData("struct S { int* p; public S(int* q) { Use(); p = q; } void Use() {} }")]
    [InlineData("struct S { int* p; public S(int* q) { S* escaped = &this; p = q; } }")]
    [InlineData("struct S { int* p; } int main() { S s = new S(); return 0; }")]
    [InlineData("struct S { int* p; } int main() { S s = default(S); return 0; }")]
    [InlineData("struct S { public S() {} } int main() { S s = default; return 0; }")]
    [InlineData("struct S { public S(int n) {} } struct T { S s; } int main() { T t = new T(); return 0; }")]
    [InlineData("struct S { int* p; } struct T { S s; } int main() { T t = default(T); return 0; }")]
    [InlineData("class B { int* p; } class D : B { public D() {} }")]
    [InlineData("struct S { readonly(char)* first = second; readonly(char)* second = \"x\"; }")]
    [InlineData("class Outer { public struct Inner { int* p; public Inner() {} } }")]
    public void InvalidInitializationIsRejected(string source)
        => Assert.Throws<TypeCheckException>(() => Compiler.Check(source));

    [Theory]
    [InlineData("if (b) p = q; else this.p = q;")]
    [InlineData("defer p = q;")]
    [InlineData("while (true) { p = q; break; }")]
    [InlineData("if (b) { p = q; return; } p = q;")]
    public void EveryCompletingPathInitializesFields(string body)
        => Assert.Equal(42, CompilerTestHelper.Run(
            "struct S { public int* p; public S(int* q, bool b) { " + body +
            " } } int main() { int n = 42; S s = new S(&n, true); return *s.p; }").ExitCode);

    [Fact]
    public void CaughtThrowInitializesFieldsOnTheCompletingPath()
        => Compiler.Check("struct S { int* p; public S(int* q, bool b) { try { if (b) throw new* Exception(); p = q; } catch (Exception* e) { p = q; } } }");

    [Fact]
    public void NestedFieldAssignmentsAndAutomaticPropertyAssignmentsCount()
        => Assert.Equal(42, CompilerTestHelper.Run("""
            struct Inner { public int* p; }
            struct S { public Inner inner; public readonly(int)* P { get; }
                public S(int* q) { inner.p = q; P = q; } }
            int main() { int n = 21; S s = new S(&n); return *s.inner.p + *s.P; }
            """).ExitCode);

    [Fact]
    public void FieldInitializersCountAndNewCallsConstructor()
        => Assert.Equal(42, CompilerTestHelper.Run("""
            struct S { public readonly(char)* p = "*"; public int n; public S() { n = (int)p[0]; } }
            int main() { S s = new S(); return s.n; }
            """).ExitCode);

    [Fact]
    public void DefaultClassMetadataIsValidInsideArraysAndStructs()
        => Assert.Equal(42, CompilerTestHelper.Run("""
            class C { public virtual int Get() => 21; }
            struct S { public C[2] values; }
            int main() { S s = default(S); C* a = &s.values[0]; C* b = &s.values[1]; return a.Get() + b.Get(); }
            """).ExitCode);

    [Fact]
    public void DefaultNullableInterfaceIsNull()
        => Assert.Equal(42, CompilerTestHelper.Run("interface I { int Get(); } int main() { I*? p = default; if (p == null) return 42; return 1; }").ExitCode);

    [Fact]
    public void FreeHelperMaySupplyAFieldInitializer()
        => Assert.Equal(42, CompilerTestHelper.Run("int* Identity(int* p) => p; struct S { public int* p; public S(int* q) { p = Identity(q); } } int main() { int n = 42; S s = new S(&n); return *s.p; }").ExitCode);

    [Fact]
    public void ConcreteDefaultMayHaveAnAbstractBaseWithoutUserConstructors()
        => Assert.Equal(42, CompilerTestHelper.Run("abstract class B { public abstract int Get(); } class D : B { public override int Get() => 42; } int main() { D d = default(D); B* p = &d; return p.Get(); }").ExitCode);
}
