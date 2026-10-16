namespace gflat.Tests;

public sealed class NestedConstantEvaluationTests
{
    [Theory]
    [InlineData("2 + 2")]
    [InlineData("1 << 2")]
    [InlineData("sizeof(short) + 2")]
    public void ResolvingAnArrayTypeDoesNotEraseTheEnclosingConstantFunctionsLocals(string bound)
        => Assert.Equal(23, CompilerTestHelper.Run(
            "const int Fold() { int value = 7; return (int)sizeof(int[" + bound +
            "]) + value; } const int folded = Fold(); int main() => folded;").ExitCode);

    [Fact]
    public void RepeatedLayoutQueriesPreserveConstantLoopState()
        => Assert.Equal(55, CompilerTestHelper.Run("""
            const int Fold() {
                int value = 7;
                for (int i = 0; i < 3; i++) value += (int)sizeof(int[1 << 2]);
                return value;
            }
            const int folded = Fold();
            int main() => folded;
            """).ExitCode);

    [Fact]
    public void NestedConstantCallsKeepTheirScopesAfterLayoutResolution()
        => Assert.Equal(55, CompilerTestHelper.Run("""
            const int Fold(int depth) {
                if (depth == 0) return 7;
                int value = Fold(depth - 1);
                return (int)sizeof(int[2 + 2]) + value;
            }
            const int folded = Fold(3);
            int main() => folded;
            """).ExitCode);
}
