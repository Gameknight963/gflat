using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class FunctionPointerInferenceTests
{
    [Theory]
    [InlineData("&Project")]
    [InlineData("(int value) => (long)value + 1L")]
    public void ReturnTypeIsInferredFromTypedCallback(string callback)
    {
        var result = CompilerTestHelper.Run("""
            long Project(int value) => (long)value + 1L;
            U Apply<U>(U(int)* callback) => callback(41);
            int main() { long result = Apply(CALLBACK); if (result != 42L) return 1; return 0; }
            """.Replace("CALLBACK", callback));
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void ExtensionInfersResultFromTypedSpanCallback()
    {
        var result = CompilerTestHelper.Run([
            new SourceFile("core/Collections/IEnumerable.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core/Collections/IEnumerable.gf"))),
            new SourceFile("core/Collections/Enumerable.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core/Collections/Enumerable.gf"))),
            new SourceFile("Span.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core/Span.gf"))),
            new SourceFile("main.gf", """
                using std; using std::Collections;
                U Select<T, U>(this Span<T> source, U(T)* callback) => callback(source[(nuint)0]);
                int main() {
                    int[1] buffer = [42]; Span<int> source = new Span<int>(buffer, 1);
                    long result = source.Select((int value) => (long)value);
                    if (result != 42L) return 1; return 0;
                }
                """)
        ]);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void NestedCallbackChildrenParticipateInInference()
    {
        var result = CompilerTestHelper.Run("""
            int Value(int item) => item;
            int(int)* Factory(long value) => &Value;
            T Infer<T, U>(T(T)*(U)* callback, U value) { T(T)* produced = callback(value); return produced((T)42); }
            int main() => Infer(&Factory, 1L);
            """);
        Assert.Equal(42, result.ExitCode);
    }

    [Theory]
    [InlineData("int Both(char value) => 1; T Infer<T>(T(T)* callback) => default(T); int main() => Infer(&Both);")]
    [InlineData("int Zero() => 1; T Infer<T>(T(int)* callback) => default(T); int main() => Infer(&Zero);")]
    [InlineData("int Value(int item) => item; T Infer<T>(T(int)* callback) => default(T); int main() { int(int)*? maybe = null; return Infer(maybe); }")]
    [InlineData("int Value(int item) => item; T Infer<T>(T value, T(T)* callback) => value; int main() => Infer('x', &Value);")]
    public void InferenceDoesNotRelaxCallbackApplicability(string source)
        => Assert.Throws<TypeCheckException>(() => Compiler.Emit(source));

    [Fact]
    public void VoidReturnAndReferenceParametersAreInferred()
    {
        var result = CompilerTestHelper.Run("""
            void Set(ref int value) { value = 42; }
            int Apply<T>(void(ref T)* callback) { T value = default(T); callback(ref value); return (int)value; }
            int main() => Apply(&Set);
            """);
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void NullableReadonlyResultMetadataIsPreserved()
    {
        var result = CompilerTestHelper.Run("""
            immutable(char*?) Text(int value) => "hello";
            int Inspect<T>(T(int)* callback) {
                if (!typeof(T).IsNullable || !typeof(T).IsReadOnly) return 1;
                T text = callback(1);
                if (text == null) return 2;
                if (text[0] != 'h') return 3;
                return 0;
            }
            int main() => Inspect(&Text);
            """);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void ConcreteAliasChildrenUseNormalTypeMatching()
    {
        var result = CompilerTestHelper.Run("""
            alias Number = int;
            long Project(int n) => (long)n;
            U Infer<U>(U(Number)* callback) => callback(42);
            int main() { if (Infer(&Project) != 42L) return 1; return 0; }
            """);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void NullableCallbackContractStillRequiresSuppressionBeforeInvocation()
    {
        var result = CompilerTestHelper.Run("""
            int Value(int item) => item;
            U Apply<U>(U(int)*? callback) => callback!(42);
            int Forward(int(int)*? callback) => Apply(callback);
            int main() => Forward(&Value);
            """);
        Assert.Equal(42, result.ExitCode);
    }
}
