using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class FunctionShadowingTests
{
    [Theory]
    [InlineData("int Target() => 0;", "int Target = 42;", "")]
    [InlineData("extern int Target();", "int Target = 42;", "")]
    [InlineData("int Target() => 0;", "", "int Target")]
    public void TakingAnAddressUsesTheVisibleVariable(string declaration, string local, string parameter)
    {
        string call = parameter.Length == 0 ? "Read()" : "Read(42)";
        var result = CompilerTestHelper.Run($$"""
            {{declaration}}
            int Read({{parameter}}) { {{local}} int* address = &Target; return *address; }
            int main() { return {{call}}; }
            """);
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void TakingAFieldAddressDoesNotSelectAnUnrelatedFunction()
    {
        var result = CompilerTestHelper.Run("""
            int Target() => 0;
            struct Value {
                int Target;
                public Value(int value) { Target = value; }
                public int Read() { int* address = &Target; return *address; }
            }
            int main() { Value value = new Value(42); return value.Read(); }
            """);
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void NonCallableLocalCannotFallBackToAnUnrelatedFunction()
    {
        var error = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check("""
            int Target() => 42;
            int main() { int Target = 0; return Target(); }
            """));
        Assert.Contains("not callable", error.Description);
    }

    [Fact]
    public void FunctionPointerLocalStillShadowsFunctionAndHasAddressableStorage()
    {
        var result = CompilerTestHelper.Run("""
            int Target() => 0;
            int Actual() => 42;
            int main() {
                int()* Target = Actual;
                int()** address = &Target;
                return (*address)();
            }
            """);
        Assert.Equal(42, result.ExitCode);
    }
}
