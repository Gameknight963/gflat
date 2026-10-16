using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class StaticCallbackTests
{
    [Fact]
    public void FunctionPointerFieldsCanBeCalledAndReplaced()
    {
        var result = CompilerTestHelper.Run("""
            int Increment(int value) => value + 1;
            int Double(int value) => value * 2;
            namespace Callbacks { public int(int)* handler = Increment; }
            static class State { public static int(int)* handler = Double; }
            int main() {
                if (Callbacks::handler(3) != 4 || State::handler(3) != 6) return 1;
                Callbacks::handler = Double;
                State::handler = Increment;
                return Callbacks::handler(3) + State::handler(3) - 10;
            }
            """);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void QualifiedConstantsWorkInOrdinaryRuntimeExpressions()
    {
        var result = CompilerTestHelper.Run("""
            namespace Constants { public const int Answer = 42; }
            static class Settings { public const int Offset = 2; }
            int main() { return Constants::Answer + Settings::Offset - 44; }
            """);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void NullableFunctionPointerFieldRequiresAnAssertionBeforeCalling()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check("""
            namespace Callbacks { public int(int)*? handler; }
            int main() { return Callbacks::handler(3); }
            """));
        Assert.Contains("nullable", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IncompatibleNativeShutdownDeclarationHasASourceDiagnostic()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Emit("""
            extern void atexit();
            struct Owner { public Owner() {} ~Owner() {} }
            Owner owner = new Owner();
            int main() => 0;
            """));
        Assert.Contains("atexit signature", error.Message);
        Assert.True(error.Span.Length > 0);
    }

    [Fact]
    public void CompatibleNativeShutdownDeclarationIsSharedWithStaticCleanup()
    {
        var result = CompilerTestHelper.Run("""
            extern int putchar(int value);
            extern int atexit(void()* callback);
            struct Owner { public Owner() {} ~Owner() { putchar(65); } }
            Owner owner = new Owner();
            int main() => 0;
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("A", result.StandardOutput);
    }

    [Theory]
    [InlineData("struct S { static readonly(int) value = 1; public S() { value = 2; } }", "readonly")]
    [InlineData("class S { static readonly(int) value = 1; public S() { value = 2; } }", "readonly")]
    [InlineData("struct V { public int x; } static class S { public static readonly(V) value; } int main() { S::value.x = 2; return 0; }", "readonly")]
    public void ReadonlyStaticStorageCannotBeMutatedByAnInstanceConstructorOrThroughAnAggregate(string source, string message)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(source));
        Assert.Contains(message, error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
