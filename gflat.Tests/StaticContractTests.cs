using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class StaticContractTests
{
    [Fact]
    public void LibrarySelectsProviderThroughAnOrdinaryTypeArgument()
    {
        var result = CompilerTestHelper.Run([
            new SourceFile("library.gf", """
                namespace Library;
                public interface ITransform { static int Apply(int value); }
                public static class Pipeline<P : ITransform> {
                    public static int Run(int value) => P::Apply(P::Apply(value));
                }
                """),
            new SourceFile("providers.gf", """
                namespace Providers;
                public static class Increment : Library::ITransform { public static int Apply(int value) => value + 1; }
                public struct Double : Library::ITransform { public static int Apply(int value) => value * 2; }
                """),
            new SourceFile("main.gf", """
                alias First = Library::Pipeline<Providers::Increment>;
                alias Second = Library::Pipeline<Providers::Double>;
                int main() { return First::Run(38) + Second::Run(1) - 2; }
                """)
        ]);
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void StaticPropertyContractsWorkInGenericFunctions()
    {
        var result = CompilerTestHelper.Run("""
            interface IValue { static int Value { get; } }
            static class Answer : IValue { public static int Value => 42; }
            int Read<P : IValue>() => P::Value;
            int main() => Read<Answer>();
            """);
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void ConfiguredPropertyAndNestedTypeRetainProvider()
    {
        var result = CompilerTestHelper.Run("""
            interface IValue { static int Get(); }
            static class Answer : IValue { public static int Get() => 42; }
            static class Library<P : IValue> {
                public static int Value => P::Get();
                public struct Reader { public int Read() => P::Get(); }
            }
            alias Selected = Library<Answer>;
            int main() {
                Selected::Reader reader = new Selected::Reader();
                if (Selected::Value != 42) return 1;
                return reader.Read();
            }
            """);
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void StaticContractsHaveNoDispatchTables()
    {
        var ir = Compiler.Emit("interface IValue { static int Get(); } struct Provider : IValue { public static int Get() => 42; }");
        Assert.DoesNotContain("Provider$IValue$vtable", ir);
    }

    [Theory]
    [InlineData("static class Provider : IValue {}", "does not implement")]
    [InlineData("class Provider : IValue { public int Get() => 1; }", "static modifier")]
    [InlineData("static class Provider : IValue { private static int Get() => 1; }", "must be public")]
    [InlineData("static class Provider : IValue { public static bool Get() => true; }", "return type")]
    [InlineData("void F(IValue* value) {}", "not an instance reference")]
    [InlineData("void F(IValue^ value) {}", "not an instance reference")]
    [InlineData("static class Provider : IValue { public static int Get(int x) => x; }", "parameters")]
    public void InvalidImplementationsAndRuntimeViewsAreRejected(string source, string diagnostic)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Emit("interface IValue { static int Get(); } " + source));
        Assert.Contains(diagnostic, error.Message);
    }

    [Fact]
    public void MatchingMethodsDoNotBypassNominalConstraint()
    {
        Assert.Throws<TypeCheckException>(() => Compiler.Emit("""
            interface IValue { static int Get(); }
            static class Other { public static int Get() => 42; }
            int Read<P : IValue>() => P::Get();
            int main() => Read<Other>();
            """));
    }
}
