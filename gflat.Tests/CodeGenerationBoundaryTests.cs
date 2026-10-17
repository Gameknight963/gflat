using System.Text.RegularExpressions;

namespace gflat.Tests;

public sealed class CodeGenerationBoundaryTests
{
    [Fact]
    public void OrdinaryFunctionNamedDtorPropagatesExceptions()
        => Assert.Equal(42, CompilerTestHelper.Run("""
            void dtor() { throw new Exception(); }
            int main() {
                try { dtor(); }
                catch (ref Exception error) { return 42; }
                return 1;
            }
            """).ExitCode);

    [Fact]
    public void PlainFunctionNeedsNoPersonalityWhenAnotherFunctionThrows()
    {
        string ir = CompilerTestHelper.EmitIr("int Plain()=>42; void Fail(){throw new Exception();}");
        string header = Regex.Match(ir, @"(?m)^define i32 @gflat\$Plain\(\).*$").Value;
        Assert.NotEmpty(header);
        Assert.DoesNotContain("personality", header);
    }

    [Fact]
    public void LambdaDefinitionDuringCleanupPreservesDoubleExceptionTermination()
    {
        var result = CompilerTestHelper.Run("""
            extern int putchar(int value);
            extern void exit(int status);
            namespace Exceptions {
                public replace void Unhandled(Exception*? error) { putchar(84); exit(0); }
            }
            void Fail() { throw new Exception(); }
            void Work() {
                defer {
                    int()* callback = static () => 0;
                    callback();
                    Fail();
                }
                Fail();
            }
            int main() { try { Work(); } catch { putchar(67); } return 1; }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("T", result.StandardOutput);
    }

    [Fact]
    public void NestedLambdaDefinitionsAreEmittedOutsideTheirEnclosingFunction()
        => Assert.Equal(42, CompilerTestHelper.Run("""
            int main() {
                int(int)* outer = static (int input) => {
                    int(int)* inner = static (int value) => value + 1;
                    return inner(input);
                };
                return outer(41);
            }
            """).ExitCode);
}
