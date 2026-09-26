namespace gflat.Tests;

public class ExceptionBoundaryTests
{
    private const string Prelude = """
        extern int printf(readonly(char)* format, ...);
        class First : Exception {}
        class Second : Exception {}
        void Fail() throws { throw new* First(); }
        """;

    [Theory]
    [InlineData("void Boundary() { Fail(); printf(c\"continued\"); }")]
    [InlineData("void Boundary() { throw new* First(); }")]
    [InlineData("void Boundary() { try { Fail(); } catch (Second* e) { printf(c\"wrong catch\"); } }")]
    public void NonThrowingBoundaryTerminatesBeforeOuterCatch(string method)
    {
        var result = CompilerTestHelper.Run(Prelude + method + "int main() { try { Boundary(); } catch { printf(c\"outer catch\"); } printf(c\"continued\"); return 0; }");
        Assert.Equal(1, result.ExitCode);
        Assert.Equal("", result.StandardOutput);
    }

    [Fact]
    public void MatchingLocalCatchContinuesNormally()
    {
        var result = CompilerTestHelper.Run(Prelude + "void Boundary() { try { Fail(); } catch (First* e) { printf(c\"caught;\"); } } int main() { Boundary(); printf(c\"continued\"); return 0; }");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("caught;continued", result.StandardOutput);
    }

    [Fact]
    public void ThrowingBoundaryStillPropagates()
    {
        var result = CompilerTestHelper.Run(Prelude + "void Boundary() throws { Fail(); } int main() { try { Boundary(); } catch (First* e) { printf(c\"caught\"); } return 0; }");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("caught", result.StandardOutput);
    }

    [Fact]
    public void TerminationDoesNotRunLanguageCleanup()
    {
        var result = CompilerTestHelper.Run(Prelude + "void Boundary() { defer { printf(c\"cleanup\"); } Fail(); } int main() { Boundary(); return 0; }");
        Assert.Equal(1, result.ExitCode);
        Assert.Equal("", result.StandardOutput);
    }

    [Theory]
    [InlineData("class C { public void Throwing() throws { Fail(); } public C() { Fail(); } }", "new C();")]
    [InlineData("struct C { public void Throwing() throws { Fail(); } public C() { Fail(); } }", "new C();")]
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
        Assert.Equal(1, result.ExitCode);
        Assert.Equal("", result.StandardOutput);
    }
}
