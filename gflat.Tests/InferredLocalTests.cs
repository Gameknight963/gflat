using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class InferredLocalTests
{
    [Fact]
    public void InferencePreservesConcreteTypesAndEvaluatesTheInitializerOnce()
    {
        var result = CompilerTestHelper.Run("""
            extern int putchar(int c);
            struct Box<T> { public T Value; public Box(T value) { Value = value; } }
            Box<int> Make() { putchar(65); return new Box<int>(42); }
            int main() {
                var box = Make();
                var pointer = &box;
                var number = pointer.Value;
                return number;
            }
            """);
        Assert.Equal(42, result.ExitCode);
        Assert.Equal("A", result.StandardOutput);
    }

    [Fact]
    public void InferredOwningValuesObeyCopyMoveAndDestructionRules()
    {
        var result = CompilerTestHelper.Run("""
            extern int putchar(int c);
            struct Owner {
                public Owner() {}
                ~Owner() { putchar(68); }
            }
            int main() { var a = new Owner(); var b = move a; a = new Owner(); return 0; }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("DD", result.StandardOutput);
    }

    [Theory]
    [InlineData("var value;", "initializer")]
    [InlineData("var value = null;", "concrete")]
    [InlineData("var value = Nothing();", "concrete")]
    [InlineData("var value = value;", "unassigned")]
    public void MissingOrUntypedInitializersAreRejected(string body, string message)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Emit("void Nothing() {} int main() { " + body + " return 0; }"));
        Assert.Contains(message, error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
