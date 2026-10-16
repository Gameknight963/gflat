using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class LambdaContextTests
{
    [Fact]
    public void LambdaInsideDeferCanReturnFromItsOwnBody()
    {
        var result = CompilerTestHelper.Run("""
            extern int printf(readonly(char*) text, ...);
            int main() {
                defer {
                    int()* answer = () => { return 42; };
                    printf("%d", answer());
                }
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("42", result.StandardOutput);
    }

    [Theory]
    [InlineData("defer { int()* answer = () => 42; return 1; }")]
    [InlineData("int()* answer = () => { defer { return 1; } return 42; };")]
    public void LambdaDoesNotAllowReturningFromADeferredBody(string body)
        => Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check("int main() { " + body + " return 0; }"));

    [Theory]
    [InlineData("break;")]
    [InlineData("continue;")]
    public void LambdaCannotControlAnEnclosingLoop(string statement)
        => Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(
            "int main() { while (true) { void()* callback = () => { " + statement + " }; break; } return 0; }"));
}
