using gflat.ast;
using gflat.CompileExceptions;
using gflat.diagnostics;

namespace gflat.Tests;

public sealed class ReflectionUnevaluatedTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CallsAndRefArgumentsAreNotExecuted(int optimization)
        => Run("""
            int calls = 0;
            int Increment(ref int value) { calls++; value++; return value; }
            int main()
            {
                int value = 7;
                const reflection type = typeof(Increment(ref value));
                static if (type != reflect(int)) { invalidType(); }
                if (calls != 0 || value != 7) return 1;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MoveArgumentsRemainOwnedAndUsable(int optimization)
        => Run("""
            int destroyed = 0;
            struct Owner
            {
                public int Value;
                ~Owner() { destroyed++; }
            }
            void Consume(Owner value) { }
            int main()
            {
                {
                    Owner value = new();
                    value.Value = 42;
                    const reflection type = typeof(Consume(move value));
                    static if (type != reflect(void)) { invalidType(); }
                    if (destroyed != 0 || value.Value != 42) return 1;
                    value.Value = 43;
                }
                if (destroyed != 1) return 2;
                return 0;
            }
            """, optimization);

    [Fact]
    public void UnevaluatedCallsDoNotContributeOutgoingExceptions()
    {
        var snapshot = new AnalysisSnapshot([new SourceFile(Path.GetFullPath("typeof-exceptions.gf"), """
            int Fail() { throw new Exception("failure"); }
            void Test() { const reflection type = typeof(Fail()); }
            """)]);
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var method = snapshot.Nodes.Select(node => node.Node).OfType<MethodDeclaration>()
            .Single(method => method.Name == "Test");
        Assert.Empty(snapshot.Checker!.GetPossibleExceptions(method));
    }

    [Fact]
    public void UnevaluatedSpliceDoesNotInitializeARequiredField()
    {
        Assert.Throws<TypeCheckException>(() => Compiler.Check("""
            struct Holder
            {
                public int* Value;
                public Holder(int* input)
                {
                    const reflection field = reflect(Holder::Value);
                    const reflection type = typeof(this.[:field:] = input);
                }
            }
            """, options: new() { NoStdLib = true }));
    }

    [Fact]
    public void EvaluatedSpliceInitializesARequiredField()
        => Compiler.Check("""
            struct Holder
            {
                public int* Value;
                public Holder(int* input)
                {
                    const reflection field = reflect(Holder::Value);
                    this.[:field:] = input;
                }
            }
            """, options: new() { NoStdLib = true });

    private static void Run(string source, int optimization)
    {
        string ir = Compiler.Emit(source, options: new() { NoStdLib = true, InternalizeFunctions = true });
        CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }
}
