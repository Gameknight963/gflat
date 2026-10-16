namespace gflat.Tests;

public sealed class EntryPointNamingTests
{
    [Theory]
    [InlineData("void main() {}")]
    [InlineData("long main() => 0l;")]
    [InlineData("int main() => 0; int main(int value) => value;")]
    public void UnsupportedEntryPointContractsProduceSourceDiagnostics(string source)
        => Assert.Contains("entry point", Assert.Throws<gflat.CompileExceptions.TypeCheckException>(() => Compiler.Check(source)).Message);

    [Fact]
    public void NamespacedMainIsAnOrdinaryMangledFunction()
    {
        string ir = Compiler.Emit("namespace Helpers { int main() => 42; }");
        Assert.Contains("@gflat$Helpers$main(", ir);
        Assert.DoesNotContain("define i32 @main(", ir);
    }

    [Fact]
    public void NamespacedMainCanBeCalledDirectlyAndThroughAFunctionPointer()
    {
        Assert.Equal(42, CompilerTestHelper.Run("""
            namespace Helpers { public int main() => 21; }
            int main() {
                int()* callback = Helpers::main;
                return Helpers::main() + callback();
            }
            """).ExitCode);
    }

    [Fact]
    public void NamespacedMainDoesNotIntroduceAnUnhandledExceptionBoundary()
    {
        Assert.Equal(42, CompilerTestHelper.Run("""
            namespace Helpers { public void main() { throw new Exception(); } }
            int main() {
                try { Helpers::main(); }
                catch (Exception* error) { return 42; }
                return 1;
            }
            """).ExitCode);
    }
}
