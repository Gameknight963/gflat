using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ProviderBindingTests
{
    [Fact]
    public void QualifiedNamespaceDefinitionsAndExpectedFunctionsWorkAcrossFiles()
    {
        Assert.Equal(42, CompilerTestHelper.Run([
            new SourceFile("main.gf", "replace int Api::Read() => 42; int main() => Api::Read();"),
            new SourceFile("contract.gf", "namespace Api { public expect int Read(); }"),
            new SourceFile("other.gf", "int Api::Other() => 0;")
        ]).ExitCode);
    }

    [Fact]
    public void QualifiedStaticReplacementImplementsAClosedClassContract()
    {
        Assert.Equal(42, CompilerTestHelper.Run("""
            static class Api { public expect static int Read(); }
            public replace static int Api::Read() => 42;
            int main() => Api::Read();
            """).ExitCode);
    }

    [Fact]
    public void ExpectedAndWeakAliasesSelectProvidersBeforeBodiesAreChecked()
    {
        Assert.Equal(42, CompilerTestHelper.Run("""
            interface IProvider { static int Read(); }
            static class Answer : IProvider { public static int Read() => 42; }
            public expect alias Selected : IProvider;
            replace alias Selected = Answer;
            int main() => Selected::Read();
            """).ExitCode);
        Assert.Equal(42, CompilerTestHelper.Run("""
            interface IProvider { static int Read(); }
            static class Original : IProvider { public static int Read() => 1; }
            static class Answer : IProvider { public static int Read() => 42; }
            weak alias Selected : IProvider = Original;
            replace alias Selected = Answer;
            int main() => Selected::Read();
            """).ExitCode);
    }

    [Theory]
    [InlineData("expect int Missing();", "has no implementation")]
    [InlineData("expect alias Missing;", "has no implementation")]
    [InlineData("expect alias Missing = int;", "cannot have a default")]
    [InlineData("expect int F() => 1;", "cannot have a body")]
    [InlineData("weak expect int F();", "exactly one")]
    [InlineData("replace alias Missing = int;", "exactly one declaration")]
    [InlineData("alias Fixed = int; replace alias Fixed = bool;", "not weak or expect")]
    [InlineData("int Missing::F() => 1;", "Unknown declaration owner")]
    public void InvalidBindingsProduceDiagnostics(string source, string expected)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Emit(source));
        Assert.Contains(expected, error.Message);
    }

    [Fact]
    public void DefaultProviderExtraMembersRemainHiddenThroughAlias()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Emit("""
            interface IProvider { static int Read(); }
            static class Answer : IProvider {
                public static int Read() => 42;
                public static int Extra() => 0;
            }
            weak alias Selected : IProvider = Answer;
            int main() => Selected::Extra();
            """));
        Assert.Contains("not exposed", error.Message);
    }
}
