using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class DeferredControlFlowTests
{
    [Theory]
    [InlineData("defer break;")]
    [InlineData("defer continue;")]
    [InlineData("defer { if (true) break; }")]
    [InlineData("defer { if (true) continue; }")]
    public void DeferredCodeCannotReplaceAnEnclosingReturnWithALoopJump(string body)
    {
        var error = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(
            "int main() { while (true) { " + body + " return 42; } return 7; }"));
        Assert.Contains("out of a defer", error.Message);
    }

    [Fact]
    public void DeferredCodeCanControlItsOwnLoops()
    {
        var result = CompilerTestHelper.Run("""
            extern int printf(immutable(char*) text, ...);
            int main() {
                defer {
                    int total = 0;
                    for (int i = 0; i < 4; i++) {
                        if (i == 1) continue;
                        if (i == 3) break;
                        total += i;
                    }
                    printf("%d", total);
                }
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("2", result.StandardOutput);
    }

    [Fact]
    public void RegisteringDeferredCodePreservesTheSurroundingLoopContext()
    {
        var result = CompilerTestHelper.Run("""
            int main() {
                int total = 0;
                for (int i = 0; i < 3; i++) {
                    defer { total++; }
                    if (i == 0) continue;
                    break;
                }
                return total;
            }
            """);
        Assert.Equal(2, result.ExitCode);
    }
}
