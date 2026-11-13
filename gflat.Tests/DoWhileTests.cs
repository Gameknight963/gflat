using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class DoWhileTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ContinueTestsTheConditionBreakSkipsItAndEveryIterationCleansUp(int optimization)
        => Run("""
            int checks = 0;
            int destroyed = 0;
            int deferred = 0;
            struct Guard { public Guard() {} ~Guard() { destroyed++; } }
            bool Check(bool value) { checks++; return value; }
            int main()
            {
                int iteration = 0;
                do
                {
                    Guard guard = new();
                    defer { deferred++; }
                    iteration++;
                    if (iteration < 3) continue;
                    if (iteration == 4) break;
                } while (Check(iteration < 10));
                if (iteration != 4 || checks != 3 || destroyed != 4 || deferred != 4) return 1;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BodyAssignmentsReachTheConditionAndTheExitAndConstantGenericLoopsRun(int optimization)
        => Run("""
            const int Count()
            {
                int value = 0;
                do { value++; if (value < 3) continue; } while (value < 4);
                return value;
            }
            const int Folded = Count();
            int Generic<T>()
            {
                int result;
                do { result = 42; } while (result != 42);
                return result;
            }
            int First() { do { return 42; } while (false); }
            int main()
            {
                if (Folded != 4 || Generic<int>() != 42 || First() != 42) return 1;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AConstantFalseLoopCanConsumeAnOwnerExactlyOnce(int optimization)
        => Run("""
            int destroyed = 0;
            struct Owner { public Owner() {} ~Owner() { destroyed++; } }
            void Consume(Owner value) {}
            int main()
            {
                Owner value = new();
                do { Consume(move value); } while (false);
                if (destroyed != 1) return 1;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData("int Read(bool flag) { int value; do { if (flag) break; value = 42; } while (false); return value; }", "unassigned")]
    [InlineData("int Read(bool flag) { int value; do { if (flag) continue; value = 42; } while (value != 42); return value; }", "unassigned")]
    [InlineData("void Read() { do {} while (1); }", "bool")]
    public void InvalidExitAssignmentsAndConditionsAreRejected(string source, string diagnostic)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(source, options: new() { NoStdLib = true }));
        Assert.Contains(diagnostic, error.Description, StringComparison.OrdinalIgnoreCase);
    }

    private static void Run(string source, int optimization)
    {
        var result = CompilerTestHelper.Run(source, optimization);
        Assert.Equal(0, result.ExitCode);
    }
}
