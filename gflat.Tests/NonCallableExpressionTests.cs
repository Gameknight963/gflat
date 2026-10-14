using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class NonCallableExpressionTests
{
    [Theory]
    [InlineData("1(2)")]
    [InlineData("(1 + 2)(3)")]
    [InlineData("null()")]
    public void CallingAValueProducesASourceDiagnostic(string expression)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check("int main() { " + expression + "; return 0; }"));
        Assert.Contains("call", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
