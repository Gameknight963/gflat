using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class CallContractTests
{
    [Theory]
    [InlineData("int F() throws { return 1; } int main() { int()* p = &F; return p(); }")]
    [InlineData("namespace N { int F() throws { return 1; } } int main() { int()* p = &N::F; return p(); }")]
    [InlineData("int F(const int x) => x; int main() { int(int)* p = &F; return p(1); }")]
    [InlineData("extern int F(const int x); int main() { int(int)* p = &F; return p(1); }")]
    public void FunctionAddressesCannotDiscardCallRequirements(string source)
        => Assert.Throws<TypeCheckException>(() => Compiler.Check(source));

    [Fact]
    public void InterfaceCannotDiscardReadonlyAggregateReturn()
        => Assert.Throws<TypeCheckException>(() => Compiler.Check("struct B { public int* data; } interface I { B Get(); } struct S : I { public B value; public readonly(B) Get() => value; }"));

    [Fact]
    public void InterfaceConversionThroughBaseKeepsDerivedOverride()
        => Assert.Equal(42, CompilerTestHelper.Run("""
            interface I { int Get(); }
            class Base : I { public virtual int Get() => 1; }
            class Derived : Base { public override int Get() => 42; }
            int Read(Base* p) { I* i = p; return i.Get(); }
            int main() { Derived d = new Derived(); return Read(&d); }
            """).ExitCode);

    [Fact]
    public void ThrowingInterfaceDispatchUsesExceptionAbi()
        => Assert.Equal(42, CompilerTestHelper.Run("""
            interface I { int Get() throws; }
            class Base : I { public virtual int Get() throws { return 1; } }
            class Derived : Base { public override int Get() throws { throw new* Exception(); } }
            int main() { Derived d = new Derived(); Base* b = &d; I* i = b;
                try { return i.Get(); } catch (Exception* e) { return 42; }
            }
            """).ExitCode);
}
