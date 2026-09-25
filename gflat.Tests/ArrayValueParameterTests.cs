using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ArrayValueParameterTests
{
    [Fact]
    public void ValueParameterCopiesAndPointerParameterSharesStorage()
        => Assert.Equal(42, CompilerTestHelper.Run("""
            int Change(int[2] values) { values[0] = 40; return values[0]; }
            void Mutate(int[2]* values) { (*values)[0] = 2; }
            int main() { int[2] a = [1, 2]; int n = Change(a); if (a[0] != 1) return 3; Mutate(&a); return n + a[0]; }
            """).ExitCode);

    [Fact]
    public void ReadonlyArrayCanBePassedAsIndependentMutableCopy()
        => Assert.Equal(42, CompilerTestHelper.Run("int Change(int[2] a) { a[0] = 41; return a[0]; } int main() { readonly(int[2]) a = [1, 2]; return Change(a) + a[0]; }").ExitCode);

    [Fact]
    public void ArrayArgumentsWorkThroughMethodsConstructorsAndFunctionPointers()
        => Assert.Equal(42, CompilerTestHelper.Run("""
            int Sum(int[2] a) => a[0] + a[1];
            struct S { public int value; public S(int[2] a) { value = Sum(a); } public int Add(int[2] a) => value + Sum(a); }
            int main() { S s = new S([10, 10]); int(int[2])* f = &Sum; return s.Add([10, 10]) + f([1, 1]); }
            """).ExitCode);

    [Fact]
    public void ArrayArgumentsWorkThroughVirtualInterfaceDispatch()
        => Assert.Equal(42, CompilerTestHelper.Run("""
            interface I { int Get(int[2] a); }
            class Base : I { public virtual int Get(int[2] a) => 0; }
            class Derived : Base { public override int Get(int[2] a) => a[0] + a[1]; }
            int main() { Derived d = new Derived(); Base* b = &d; I* i = b; return i.Get([20, 22]); }
            """).ExitCode);

    [Fact]
    public void NestedAndReturnedArraysAreValues()
        => Assert.Equal(42, CompilerTestHelper.Run("int[2] Make() => [20, 22]; int Sum(int[2][1] a) => a[0][0] + a[0][1]; int main() => Sum([Make()]);").ExitCode);

    [Theory]
    [InlineData("void F(int[] a) {} int main() => 0;")]
    [InlineData("int main() { long[2] a = [1, 2]; return 0; }")]
    [InlineData("void F(int*[1] a) {} int main() { int n = 1; readonly(int*[1]) a = [&n]; F(a); return 0; }")]
    public void UnsupportedOrUnsafeArrayConversionsAreRejected(string source)
        => Assert.Throws<TypeCheckException>(() => Compiler.Check(source));
}
