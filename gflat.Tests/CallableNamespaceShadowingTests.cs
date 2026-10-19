using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class CallableNamespaceShadowingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void LocalOrdinaryFunctionShadowsImportedGenericFunction(int optimization)
        => Assert.Equal(42, CompilerTestHelper.Run("""
            using Library;
            namespace Library { public int Pick<T>(T input, T other) => 99; }
            int Pick(int input) => input;
            int main() => Pick(42);
            """, optimization).ExitCode);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InnerOrdinaryFunctionShadowsOuterGenericFunction(int optimization)
        => Assert.Equal(42, CompilerTestHelper.Run("""
            namespace Outer {
                public int Pick<T>(T input) => 99;
                namespace Inner { int Pick(int input) => input; public int Run() => Pick(42); }
            }
            int main() => Outer::Inner::Run();
            """, optimization).ExitCode);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InnerExternShadowsOuterFunctionAndImportedGenericFunction(int optimization)
        => Assert.Equal(42, CompilerTestHelper.Run("""
            using Library;
            namespace Library { public int abs<T>(T input) => 99; }
            namespace Outer {
                public int abs(int input) => 98;
                namespace Inner { private extern int abs(int input); public int Run() => abs(-42); }
            }
            int main() => Outer::Inner::Run();
            """, optimization).ExitCode);

    [Fact]
    public void IncorrectLocalCallDoesNotFallBackToAnImportedGeneric()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check("""
            using Library;
            namespace Library { public int Pick<T>(T input,T other) => 99; }
            int Pick(int input) => input;
            int main() => Pick(1,2);
            """));
        Assert.Contains("expects 1 arguments but got 2", error.Message);
    }
}
