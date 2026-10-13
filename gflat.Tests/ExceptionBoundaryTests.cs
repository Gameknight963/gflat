namespace gflat.Tests;

public class ExceptionBoundaryTests
{
    [Theory]
    [InlineData("new Exception()", 1)]
    [InlineData("new Exception(c\"failure\")", 1)]
    [InlineData("new Exception(c\"failure\", 42)", 42)]
    [InlineData("new Exception(c\"failure\", 0)", 0)]
    [InlineData("new Custom()", 23)]
    public void DefaultHookUsesExceptionCode(string value, int status)
    {
        var result = CompilerTestHelper.Run(
            "class Custom : Exception { public readonly override int GetCode() => 23; } " +
            "int main() { throw " + value + "; }");
        Assert.Equal(status, result.ExitCode);
        Assert.Equal("", result.StandardOutput);
        Assert.Equal("", result.StandardError);
    }

    [Fact]
    public void DefaultHookUsesOneForForeignException()
    {
        Assert.Equal(1, CompilerTestHelper.Run("int main() => Exceptions::Unhandled(null);").ExitCode);
    }

    [Fact]
    public void ReplacementUnhandledHookControlsOutputAndExitStatus()
    {
        var result = CompilerTestHelper.Run("""
            extern int printf(readonly(char*) format, ...);
            namespace Exceptions {
                public replace int Unhandled(Exception*? error) {
                    printf(c"custom failure");
                    return 42;
                }
            }
            int main() { throw new Exception(); }
            """);
        Assert.Equal(42, result.ExitCode);
        Assert.Equal("custom failure", result.StandardOutput);
        Assert.Equal("", result.StandardError);
    }

    [Theory]
    [InlineData("return 1;")]
    [InlineData("throw new Exception();")]
    public void ReplacementUnhandledHookCannotResumeOrPropagate(string body)
    {
        var result = CompilerTestHelper.Run(
            "namespace Exceptions { public replace int Unhandled(Exception*? error) { " + body +
            " } } int main() { throw new Exception(); }");
        Assert.Equal(1, result.ExitCode);
        Assert.Equal("", result.StandardOutput);
        Assert.Equal("", result.StandardError);
    }

    private const string Prelude = """
        extern int printf(readonly(char)* format, ...);
        class First : Exception {}
        class Second : Exception {}
        void Fail() { throw new First(); }
        """;

    [Theory]
    [InlineData("void Boundary() nothrow { Fail(); printf(c\"continued\"); }")]
    [InlineData("void Boundary() nothrow { throw new First(); }")]
    [InlineData("void Boundary() nothrow { try { Fail(); } catch (Second* e) { printf(c\"wrong catch\"); } }")]
    public void NonThrowingBoundaryTerminatesBeforeOuterCatch(string method)
    {
        var result = CompilerTestHelper.Run(Prelude + method + "int main() { try { Boundary(); } catch { printf(c\"outer catch\"); } printf(c\"continued\"); return 0; }");
        Assert.Equal(1, result.ExitCode);
        Assert.Equal("", result.StandardOutput);
    }

    [Fact]
    public void MatchingLocalCatchContinuesNormally()
    {
        var result = CompilerTestHelper.Run(Prelude + "void Boundary() nothrow { try { Fail(); } catch (First* e) { printf(c\"caught;\"); } } int main() { Boundary(); printf(c\"continued\"); return 0; }");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("caught;continued", result.StandardOutput);
    }

    [Fact]
    public void ThrowingBoundaryStillPropagates()
    {
        var result = CompilerTestHelper.Run(Prelude + "void Boundary() { Fail(); } int main() { try { Boundary(); } catch (First* e) { printf(c\"caught\"); } return 0; }");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("caught", result.StandardOutput);
    }

    [Fact]
    public void TerminationUnwindsToBoundary()
    {
        var result = CompilerTestHelper.Run(Prelude + "void Boundary() nothrow { defer { printf(c\"cleanup\"); } Fail(); } int main() { Boundary(); return 0; }");
        Assert.Equal(1, result.ExitCode);
        Assert.Equal("cleanup", result.StandardOutput);
    }

    [Theory]
    [InlineData("class C { public void Throwing() { Fail(); } public C() nothrow { Fail(); } }", "new C();")]
    [InlineData("struct C { public void Throwing() { Fail(); } public C() nothrow { Fail(); } }", "new C();")]
    [InlineData("struct C { public ~C() { Fail(); } }", "{ C value = new C(); }")]
    public void ConstructorsAndDestructorsAreTerminationBoundaries(string type, string expression)
    {
        var result = CompilerTestHelper.Run(Prelude + type + "int main() { try { " + expression + " } catch { printf(c\"outer catch\"); } return 0; }");
        Assert.Equal(1, result.ExitCode);
        Assert.Equal("", result.StandardOutput);
    }

    [Fact]
    public void LambdaDoesNotInheritEnclosingExceptionHandlers()
    {
        var result = CompilerTestHelper.Run(Prelude + "int main() { try { void()* action = () => { Fail(); }; action(); } catch { printf(c\"outer catch\"); } return 0; }");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("outer catch", result.StandardOutput);
    }
}
