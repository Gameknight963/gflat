using gflat.CompileExceptions;
namespace gflat.Tests;

public class NullForgivingTests
{
    [Fact]
    public void NullSuppressionDoesNotCheckOrAssumeEvenAcrossCalls()
    {
        const string source = """
            bool IsNull(int* p) { int*? view = p; return view == null; }
            int main() {
                int*? p = null;
                int* q = p!;
                if (!IsNull(q)) return 1;
                if (p != null || !IsNull(q)) return 2;
                return 0;
            }
            """;
        string ir = CompilerTestHelper.EmitIr(source);
        Assert.DoesNotContain("null_failure", ir);
        Assert.DoesNotContain("llvm.assume", ir);
        Assert.DoesNotContain(" nonnull ", ir);
        Assert.Equal(0, CompilerTestHelper.Run(source).ExitCode);
    }

    [Fact]
    public void SupportsCallsMembersIndexingAndSingleEvaluation()
        => Assert.Equal(0, CompilerTestHelper.Run("""
            struct S { public int n; public int Get() { return n; } }
            S*? Once(S* p, int* count) { (*count)++; return p; }
            int Answer() { return 42; }
            int main() {
                S s = new S(); s.n = 42;
                int count = 0;
                if (Once(&s, &count)!.Get() != 42 || count != 1) return 1;
                int*? p = &s.n;
                p![(nuint)0] = 43;
                if (*p! != 43) return 2;
                int()*? f = &Answer;
                if (f!() != 42) return 3;
                int()*? missing = null;
                int()* uncheckedFunction = missing!;
                int()*? functionView = uncheckedFunction;
                if (functionView != null) return 4;
                return 0;
            }
            """).ExitCode);

    [Fact]
    public void SupportsManagedAndInterfacePointers()
        => Assert.Equal(0, CompilerTestHelper.Run("""
            interface I { int Get(); }
            class S : I { public int Get() { return 42; } }
            int main() {
                S value = new S(); I*? view = &value;
                if (view!.Get() != 42) return 1;
                I*? empty = null; I* nonnull = empty!;
                I*? interfaceView = nonnull;
                if (interfaceView != null) return 2;
                S^? managed = null; S^ uncheckedManaged = managed!;
                S^? managedView = uncheckedManaged;
                if (managedView != null) return 3;
                return 0;
            }
            """).ExitCode);

    [Fact]
    public void GenericSuppressionPreservesInnerNullability()
        => Assert.Equal(0, CompilerTestHelper.Run("""
            T* Unwrap<T>(T*? p) { return p!; }
            int main() {
                int*? inner = null;
                int*?*? outer = &inner;
                int*?* unwrapped = Unwrap<int*?>(outer);
                if (*unwrapped != null) return 1;
                return 0;
            }
            """).ExitCode);

    [Theory]
    [InlineData("int main() { int*? p; int* q = p!; return 0; }")]
    [InlineData("int main() { readonly(int)*? p = null; int* q = p!; return 0; }")]
    [InlineData("int main() { int*? p = null; p!; return *p; }")]
    [InlineData("int main() { bool b = true; b!; return 0; }")]
    [InlineData("int main() { null!; return 0; }")]
    public void PreservesReadonlyDefiniteAssignmentAndOriginalNullability(string source)
        => Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(source));

    [Fact]
    public void ExistingCastStillChecks()
    {
        string ir = CompilerTestHelper.EmitIr("int* Convert(int*? p) { return (int*)p; } int main() { return 0; }");
        Assert.Contains("null_failure", ir);
        Assert.Contains("call void @llvm.trap()", ir);
    }
}
