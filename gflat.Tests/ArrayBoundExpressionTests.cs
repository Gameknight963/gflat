using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ArrayBoundExpressionTests
{
    [Theory]
    [InlineData("1 << 2", 4)]
    [InlineData("((extralong)1 << 96) >> 95", 2)]
    [InlineData("sizeof(short) + 1", 3)]
    public void ArrayBoundsUseCheckedExpressionTypes(string bound, int expected)
        => Assert.Equal(expected * 4, CompilerTestHelper.Run(
            "int main() { int[" + bound + "] values; return (int)sizeof(values); }").ExitCode);

    [Theory]
    [InlineData("1 << 32")]
    [InlineData("(extralong)1 << 128")]
    [InlineData("true + 1")]
    [InlineData("\"text\" + false")]
    public void InvalidArrayBoundOperationsHaveSourceDiagnostics(string bound)
        => Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(
            "int main() { int[" + bound + "] values; return 0; }"));
}
