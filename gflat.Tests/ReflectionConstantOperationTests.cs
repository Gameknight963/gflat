namespace gflat.Tests;

public sealed class ReflectionConstantOperationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ReflectedCallsExecuteDuringConstantEvaluation(int optimization)
    {
        Run("""
            const int Twice(int number) => number * 2;
            const reflection method = reflect(Twice);
            const int Answer = [:method:](21);
            int main() => Answer - 42;
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FieldSplicesReadAndMutateTheirOriginalCompileTimeStorage(int optimization)
    {
        Run("""
            struct Pair { public int First; public int Second; }
            const int Build() {
                Pair value = new();
                static foreach (reflection field in reflect(Pair).Fields) {
                    value.[:field:] = 20;
                    value.[:field:]++;
                }
                return value.First + value.Second;
            }
            const int Answer = Build();
            int main() => Answer - 42;
            """, optimization);
    }

    private static void Run(string source, int optimization)
    {
        string ir = Compiler.Emit(source, options: new() { NoStdLib = true });
        CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }
}
