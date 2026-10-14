using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class NameofTests
{
    [Theory]
    [InlineData("alias Handle = void*;", "Handle", "Handle")]
    [InlineData("alias Number = int;", "Number", "Number")]
    [InlineData("namespace N { alias Number = int; }", "N::Number", "Number")]
    [InlineData("namespace N {}", "N", "N")]
    [InlineData("using N; namespace N { public struct Widget {} }", "Widget", "Widget")]
    [InlineData("namespace N { public struct Widget {} }", "N::Widget", "Widget")]
    [InlineData("int F(int n) => n; int F(char n) => 1;", "F", "F")]
    [InlineData("namespace N { int F(int n) => n; int F(char n) => 1; }", "N::F", "F")]
    public void SourceNamesProduceStaticCString(string declarations, string target, string expected)
    {
        var result = CompilerTestHelper.Run(declarations + $$"""
            extern int printf(readonly(char*) format, ...);
            int main() { readonly(char*) name = nameof({{target}}); printf("%s", name); return 0; }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expected, result.StandardOutput);
    }

    [Fact]
    public void NullableUninitializedAndTypeReceiversDoNotEvaluateMembers()
    {
        var result = CompilerTestHelper.Run("""
            extern int printf(readonly(char*) format, ...);
            struct S {
                public int field;
                public int P { get { throw new Exception(); } }
                public int WriteOnly { set { throw new Exception(); } }
                public int F(int n) => n;
                public int F(char n) => 1;
            }
            readonly(char*) Name() { S*? p; return nameof(p.field); }
            int main() {
                S s; S*? p = null;
                printf("%s %s %s %s %s", Name(), nameof(s.P), nameof(p.WriteOnly), nameof(S::F), nameof(p.F));
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("field P WriteOnly F F", result.StandardOutput);
    }

    [Theory]
    [InlineData("F().field")]
    [InlineData("new S().field")]
    [InlineData("items[0].field")]
    [InlineData("move item")]
    [InlineData("1")]
    public void RuntimeExpressionsAreNotNamedEntities(string expression)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Emit("struct S { public int field; } S F() => new S(); int main() { S item; S[1] items; nameof(" + expression + "); return 0; }"));
        Assert.Contains("named", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GenericParameterAndNestedTypeKeepSourceNames()
    {
        var result = CompilerTestHelper.Run("""
            extern int printf(readonly(char*) format, ...);
            namespace N { public class Outer { public class Inner {} } }
            readonly(char*) Name<T>(T item) => nameof(T);
            int main() { printf("%s %s", Name(1), nameof(N::Outer::Inner)); return 0; }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("T Inner", result.StandardOutput);
    }

    [Theory]
    [InlineData("private int field;", "field")]
    [InlineData("private int P { get => 1; }", "P")]
    [InlineData("private int F() => 1;", "F")]
    public void NamingDoesNotBypassPrivateAccess(string member, string name)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Emit("struct S { " + member + " } int main() { S*? p; nameof(p." + name + "); return 0; }"));
        Assert.Contains("private", error.Message);
    }

    [Fact]
    public void MethodReturnTypesAreNotMemberReceivers()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Emit("struct S { public int field; } S F() => new S(); int main() { nameof(F.field); return 0; }"));
        Assert.Contains("method group", error.Message);
    }

    [Theory]
    [InlineData("private int Hidden() => 1;")]
    [InlineData("private int Hidden { get => 1; }")]
    [InlineData("private const int Hidden = 1;")]
    public void NamespacePrivateNamesRemainFilePrivate(string declaration)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Emit([
            new SourceFile("library.gf", "namespace N; " + declaration),
            new SourceFile("main.gf", "int main() { nameof(N::Hidden); return 0; }")
        ]));
        Assert.Contains("private", error.Message);
    }

    [Fact]
    public void QualifiedAliasUsesItsDeclarationNamespace()
    {
        var result = CompilerTestHelper.Run([
            new SourceFile("library.gf", "namespace N; public struct Target { public int field; } alias A = Target;"),
            new SourceFile("main.gf", "struct Target {} extern int printf(readonly(char*) text, ...); int main() { printf(\"%s\", nameof(N::A::field)); return 0; }")
        ]);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("field", result.StandardOutput);
    }
}
