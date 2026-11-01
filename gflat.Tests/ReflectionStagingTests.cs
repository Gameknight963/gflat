using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ReflectionStagingTests
{
    [Fact]
    public void StaticBranchesAreCheckedForEachConstInput()
    {
        Check("""
            const reflection Choose(const bool useLong)
            {
                static if (useLong) { return reflect(long); }
                else { return reflect(int); }
            }
            int main()
            {
                const reflection first = Choose(false);
                const reflection second = Choose(true);
                static if (first != reflect(int) || second != reflect(long)) { staleBranch(); }
                return 0;
            }
            """);
    }

    [Fact]
    public void TypeSplicesAreBoundForEachReflectionInput()
    {
        Check("""
            const int Width(const reflection type) => sizeof([:type:]);
            int main()
            {
                const int first = Width(reflect(int));
                const int second = Width(reflect(long));
                static if (first != 4 || second != 8) { staleType(); }
                return 0;
            }
            """);
    }

    [Fact]
    public void UnsizedReflectionSequenceInputsExpandEachInvocation()
    {
        Check("""
            struct Pair { public int first; public long second; }
            struct Single { public int value; }
            const nuint Count(const reflection[] fields)
            {
                nuint count = 0;
                static foreach (reflection field in fields) { count++; }
                return count;
            }
            int main()
            {
                const nuint first = Count(reflect(Pair).Fields);
                const nuint second = Count(reflect(Single).Fields);
                static if (first != 2 || second != 1) { staleExpansion(); }
                return 0;
            }
            """);
    }

    [Fact]
    public void MutableParametersCannotUseAStaleEntrySnapshotForStaticExpansion()
    {
        Assert.Throws<TypeCheckException>(() => Check("""
            const int Width(reflection type)
            {
                type = reflect(long);
                static if (type.Size == 8) { return 8; }
                else { return 4; }
            }
            int main() => Width(reflect(int));
            """));
    }

    [Fact]
    public void MutableParametersWithOrdinaryControlFlowKeepInterpreterSemantics()
    {
        Check("""
            const reflection Choose(bool useLong)
            {
                useLong = !useLong;
                if (useLong) return reflect(long);
                return reflect(int);
            }
            int main()
            {
                const reflection first = Choose(false);
                const reflection second = Choose(true);
                static if (first != reflect(long) || second != reflect(int)) { wrongInterpreterState(); }
                return 0;
            }
            """);
    }

    [Fact]
    public void AnInvalidSelectedExpansionIsDiagnosedAtItsInvocation()
    {
        var error = Assert.Throws<TypeCheckException>(() => Check("""
            const int Select(const bool broken)
            {
                static if (broken) { missingSelectedFunction(); return 1; }
                else { return 0; }
            }
            int main()
            {
                const int first = Select(false);
                return Select(true);
            }
            """));
        Assert.Contains("missingSelectedFunction", error.Message);
    }

    [Fact]
    public void ParameterIndependentStaticSyntaxKeepsRuntimeAvailability()
    {
        Check("""
            const int Identity(int value)
            {
                static if (sizeof(int) == 4) { return value; }
                else { return 0; }
            }
            int Run(int value) => Identity(value);
            int main() => Run(42);
            """);
    }

    private static void Check(string source) => Compiler.Check(source, options: new() { NoStdLib = true });
}
