using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ProviderBindingTests
{
    [Theory]
    [InlineData("alias Another = Selected; int main() => Another::Extra();")]
    [InlineData("int main() { alias Another = Selected; return Another::Extra(); }")]
    [InlineData("int Invoke<P>() => P::Extra(); int main() => Invoke<Selected>();")]
    [InlineData("static class Invoke<P> { public static int Run() => P::Extra(); } alias Invocation = Invoke<Selected>; int main() => Invocation::Run();")]
    [InlineData("int Invoke<P>() => P::Extra(); int main() { int first = Invoke<Answer>(); return Invoke<Selected>(); }")]
    [InlineData("int Forward<P>() => Invoke<P>(); int Invoke<P>() => P::Extra(); int main() { int first = Forward<Answer>(); return Forward<Selected>(); }")]
    [InlineData("int Invoke<P>() { alias Local = P; return Local::Extra(); } int main() { int first = Invoke<Answer>(); return Invoke<Selected>(); }")]
    [InlineData("int Forward<P>() => Api::Invoke<P>(); namespace Api { public int Invoke<P>() => P::Extra(); } int main() { int first = Forward<Answer>(); return Forward<Selected>(); }")]
    public void ContractCannotBeBypassedThroughAnAliasOrGenericParameter(string use)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Emit("""
            interface IProvider { static int Read(); }
            static class Answer : IProvider {
                public static int Read() => 42;
                public static int Extra() => 0;
            }
            weak alias Selected : IProvider = Answer;
            """ + use));
        Assert.Contains("not exposed", error.Message);
    }

    [Fact]
    public void GenericConstraintCannotWidenProviderAliasContract()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Emit("""
            interface IProvider { static int Read(); }
            interface IExtra { static int Extra(); }
            static class Answer : IProvider, IExtra {
                public static int Read() => 42;
                public static int Extra() => 0;
            }
            weak alias Selected : IProvider = Answer;
            int Invoke<P : IExtra>() => 1;
            int main() => Invoke<Selected>();
            """));
        Assert.Contains("does not satisfy", error.Message);
    }

    [Fact]
    public void BaseClassContractsExposeTheirPublicStaticMembers()
    {
        Assert.Equal(42, CompilerTestHelper.Run("""
            class Base { public static int Read() => 42; }
            class Answer : Base { public static int Extra() => 0; }
            alias Selected : Base = Answer;
            int main() => Selected::Read();
            """).ExitCode);
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Emit("""
            class Base { public static int Read() => 42; }
            class Answer : Base { public static int Extra() => 0; }
            alias Selected : Base = Answer;
            int main() => Selected::Extra();
            """));
        Assert.Contains("not exposed", error.Message);
    }

    [Fact]
    public void ProviderOnlyOverloadCannotBeAccessedThroughContract()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Emit("""
            interface IProvider { static int Read(); }
            static class Answer : IProvider {
                public static int Read() => 42;
                public static int Read(int ignored) => 0;
            }
            weak alias Selected : IProvider = Answer;
            int main() => Selected::Read(1);
            """));
        Assert.Contains("No matching overload for 'Read'", error.Message);
    }

    [Fact]
    public void ProviderOnlyNestedTypesCannotBeAccessedThroughContract()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Emit("""
            interface IProvider { static int Read(); }
            static class Answer : IProvider {
                public static int Read() => 42;
                public struct Hidden { public int value; }
            }
            weak alias Selected : IProvider = Answer;
            int main() { Selected::Hidden value = new Selected::Hidden(); return 0; }
            """));
        Assert.Contains("not exposed", error.Message);
    }

    [Fact]
    public void QualifiedReplacementTargetResolvesInItsSourceNamespace()
    {
        Assert.Equal(42, CompilerTestHelper.Run([
            new SourceFile("binding.gf", "namespace Api { public interface IProvider { static int Read(); } public expect alias Selected : IProvider; }"),
            new SourceFile("replacement.gf", "namespace App { static class Provider : Api::IProvider { public static int Read() => 42; } replace alias Api::Selected = Provider; }"),
            new SourceFile("main.gf", "int main() => Api::Selected::Read();")
        ]).ExitCode);
    }

    [Fact]
    public void ReplacementTargetAliasesResolveBeforeExpectedFunctionSignatureMatching()
    {
        Assert.Equal(42, CompilerTestHelper.Run("""
            namespace Api {
                public expect alias Number;
                public expect Number Read(Number value);
            }
            namespace App {
                alias Number = int;
                replace alias Api::Number = Number;
                replace int Api::Read(int value) => value;
            }
            int main() => Api::Read(42);
            """).ExitCode);
    }

    [Theory]
    [InlineData("alias A = B; alias B = A;", "Cyclic")]
    [InlineData("interface I { static int F(); } static class X {} weak alias A : I = X;", "does not satisfy")]
    [InlineData("weak alias A = int; replace alias A = bool; replace alias A = char;", "at most one")]
    [InlineData("static class X { public expect alias A; }", "must be declared in a namespace")]
    public void InvalidUnusedAliasBindingsAreChecked(string source, string expected)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Emit(source));
        Assert.Contains(expected, error.Message);
    }

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

    [Fact]
    public void UnselectedAliasDefaultDoesNotInstantiateUnavailableDependencies()
    {
        Assert.Equal(42, CompilerTestHelper.Run("""
            weak alias Selected = Missing<int>;
            replace alias Selected = int;
            int main() { Selected value = 42; return value; }
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
