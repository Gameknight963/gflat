namespace gflat.Tests;

public sealed class IntegerSequenceTests
{
    private static ExecutionResult Run(string program) => CompilerTestHelper.Run([
        new SourceFile("main.gf", program),
        new SourceFile("core/Sequences.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", "core", "Sequences.gf")))
    ]);

    [Fact]
    public void LazyPipelineIsRepeatableAndAllocatesNoMemory()
    {
        var result = Run("""
            using std;
            extern void* malloc(nuint count);
            extern void free(void* storage);
            extern int printf(readonly(char*) text, ...);
            namespace Memory {
                public replace void*? Allocate(nuint count) { printf("allocation"); return malloc(count); }
                public replace void Free(void* storage) { free(storage); }
            }
            int main() {
                FilteredIntRange query = Range(0, 10).Where((int n) => n % 2 == 0).Skip(1).Take(2);
                int total = 0;
                foreach (int n in query) total += n;
                foreach (int n in query) total += n;
                if (total != 12 || query.Count() != 2 || !query.Any() || query.Sum() != 6L) return 1;
                if (Range(0, 10).Count() != 10 || Range(0, 10).Sum() != 45L) return 2;
                return 42;
            }
            """);
        Assert.Equal(42, result.ExitCode);
        Assert.Empty(result.StandardOutput);
    }

    [Fact]
    public void PredicateRunsOnlyWhenAndAsFarAsRequested()
    {
        var result = Run("""
            using std;
            extern int printf(readonly(char*) text, ...);
            bool Odd(int n) { printf("%d", n); return n % 2 == 1; }
            int main() {
                FilteredIntRange query = Range(0, 10).Where(&Odd).Take(1);
                printf("created|");
                if (query.Take(0).Any()) return 1;
                foreach (int n in query) { if (n != 1) return 2; }
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("created|01", result.StandardOutput);
    }

    [Fact]
    public void SkippingAllMatchesStillConsumesTheFilteredInput()
    {
        var result = Run("""
            using std;
            extern int printf(readonly(char*) text, ...);
            bool Seen(int n) { printf("%d", n); return true; }
            int main() {
                if (Range(0, 3).Where(&Seen).Skip(3).Any()) return 1;
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("012", result.StandardOutput);
    }

    [Fact]
    public void OperationOrderAndEmptySequencesArePreserved()
    {
        var result = Run("""
            using std;
            int main() {
                if (Range(0, 10).Take(4).Where((int n) => n % 2 == 0).Sum() != 2L) return 1;
                if (Range(0, 10).Where((int n) => n % 2 == 0).Take(4).Sum() != 12L) return 2;
                if (Range(0, 10).Where((int n) => true).Take(3).Skip(2).Sum() != 2L) return 3;
                if (Range(0, 10).Where((int n) => true).Skip(2).Take(3).Sum() != 9L) return 4;
                if (Range(5, 0).Any() || Range(5, 0).Count() != 0 || Range(5, 0).Sum() != 0L) return 5;
                if (Range(0, 0).Where((int n) => true).Any()) return 6;
                if (Range(0, 10).Where((int n) => false).Count() != 0) return 7;
                if (Range(0, 2147483647).Skip(2147483647).Any()) return 8;
                if (Range(0, 10).Where((int n) => true).Skip(2147483647).Any()) return 9;
                return 42;
            }
            """);
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void BoundsDoNotOverflowAndSumWidensToLong()
    {
        var result = Run("""
            using std;
            int main() {
                if (Range(2147483647, 1).Sum() != 2147483647L) return 1;
                if (Range(2147483646, 2).Sum() != 4294967293L) return 2;
                if (Range(-2147483647 - 1, 2).Sum() != -4294967295L) return 3;
                if (Range(2147483647, 1).Skip(1).Any()) return 4;
                if (Range(1, 2147483647).Sum() != 2305843008139952128L) return 5;
                if (Range(-2147483647 - 1, 2147483647).Sum() != -2305843010287435775L) return 6;
                return 42;
            }
            """);
        Assert.Equal(42, result.ExitCode);
    }

    [Theory]
    [InlineData("Range(0, -1)")]
    [InlineData("Range(2147483647, 2)")]
    [InlineData("Range(0, 10).Take(-1)")]
    [InlineData("Range(0, 10).Skip(-1)")]
    [InlineData("Range(0, 10).Where((int n) => true).Take(-1)")]
    [InlineData("Range(0, 10).Where((int n) => true).Skip(-1)")]
    [InlineData("new IntRangeEnumerator(0, -1)")]
    public void InvalidCountsThrow(string expression)
    {
        Assert.Equal(42, Run($$"""
            using std;
            int main() {
                try { {{expression}}; return 1; }
                catch (ref Exception error) { return 42; }
            }
            """).ExitCode);
    }

    [Fact]
    public void CurrentIsValidOnlyBetweenSuccessfulMoveNextAndExhaustion()
    {
        Assert.Equal(42, Run("""
            using std;
            int main() {
                IntRangeEnumerator iterator = Range(42, 1).GetEnumerator();
                try { iterator.Current; return 1; } catch (ref Exception error) {}
                if (!iterator.MoveNext() || iterator.Current != 42 || iterator.MoveNext()) return 2;
                try { iterator.Current; return 3; } catch (ref Exception error) {}
                if (iterator.MoveNext()) return 4;
                return 42;
            }
            """).ExitCode);
    }

    [Fact]
    public void PredicateExceptionsPropagateThroughForeach()
    {
        Assert.Equal(42, Run("""
            using std;
            int main() {
                int total = 0;
                try {
                    foreach (int n in Range(0, 10).Where((int value) => {
                        if (value == 3) throw new Exception("predicate failed");
                        return true;
                    })) total += n;
                    return 1;
                } catch (ref Exception error) {
                    if (total != 3) return 2;
                    return 42;
                }
            }
            """).ExitCode);
    }
}
