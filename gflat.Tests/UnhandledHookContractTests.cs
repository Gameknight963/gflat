using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class UnhandledHookContractTests
{
    [Theory]
    [InlineData("public void Unhandled(int code) {}")]
    [InlineData("public int Unhandled(Exception*? error) => 1;")]
    [InlineData("private void Unhandled(Exception*? error) {}")]
    [InlineData("public void Unhandled(Exception* error) {}")]
    [InlineData("public void Unhandled(ref Exception error) {}")]
    [InlineData("public void Unhandled(readonly(Exception*?) error) {}")]
    [InlineData("public void Unhandled<T>(Exception*? error) {}")]
    public void InvalidUnhandledContractsHaveASourceDiagnostic(string method)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(
            "class Exception {} namespace Exceptions { " + method + " } int main() => 0;",
            options: new() { NoStdLib = true }));
        Assert.Contains("void Unhandled(Exception*? error)", error.Message);
        Assert.True(error.Span.Length > 0);
    }

    [Fact]
    public void AdditionalOverloadsDoNotChangeTheRuntimeEntryPoint()
    {
        var result = CompilerTestHelper.Run("""
            extern int putchar(int value);
            extern void exit(int status);
            namespace Exceptions {
                public void Unhandled(int code) { putchar(88); exit(2); }
                public replace void Unhandled(Exception*? error) { putchar(65); exit(42); }
            }
            int main() { throw new Exception(); }
            """);
        Assert.Equal(42, result.ExitCode);
        Assert.Equal("A", result.StandardOutput);
    }

    [Fact]
    public void DefaultHookAlsoWorksWithUnrelatedOverloads()
    {
        var result = CompilerTestHelper.Run("""
            namespace Exceptions { public void Unhandled(int code) {} }
            int main() { throw new Exception("failed", 42); }
            """);
        Assert.Equal(42, result.ExitCode);
    }
}
