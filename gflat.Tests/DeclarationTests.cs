using gflat.CompileExceptions;

namespace gflat.Tests;

public class DeclarationTests
{
    [Fact]
    public void RepeatedForeignSymbolsShareOneNativeDeclaration()
    {
        string ir = Compiler.Emit("namespace A { private extern int abs(int value); } namespace B { extern int abs(int value); } int main() => B::abs(-42);");
        Assert.Equal(1, ir.Split("declare i32 @abs(").Length - 1);
        Assert.Equal(42, CompilerTestHelper.Run("namespace A { extern int abs(int value); } namespace B { extern int abs(int value); } int main() => B::abs(-42);").ExitCode);
    }

    [Fact]
    public void ConflictingForeignSignaturesHaveASourceError()
        => Assert.Contains("incompatible native signatures", Assert.Throws<TypeCheckException>(() => Compiler.Emit(
            "namespace A { extern int abs(int value); } namespace B { extern long abs(long value); } int main() => 0;")).Message);

    [Theory]
    [InlineData("int Missing();")]
    [InlineData("int Missing<T>();")]
    [InlineData("namespace Test { int Missing(); }")]
    [InlineData("struct Test { public int Missing(); }")]
    public void OrdinaryDeclarationsRequireBodies(string source)
        => Assert.Contains("body", Assert.Throws<TypeCheckException>(() => Compiler.Check(source)).Message);

    [Fact]
    public void InterfaceAbstractAndExternDeclarationsRemainValid()
        => Compiler.Check("interface I { int Value(); } abstract class Base { public abstract int Value(); } extern int abs(int value);");

    [Theory]
    [InlineData("private extern")]
    [InlineData("extern private")]
    public void PrivateExternCanBeCalledAndAddressedInsideItsFile(string modifiers)
    {
        var result = CompilerTestHelper.Run($$"""
            namespace detail { {{modifiers}} int abs(int value); }
            int main() { int(int)* f = &detail::abs; return f(-42); }
            """);
        Assert.Equal(42, result.ExitCode);
    }

    [Theory]
    [InlineData("return detail::abs(-42);")]
    [InlineData("int(int)* f = &detail::abs; return f(-42);")]
    [InlineData("return abs(-42);")]
    public void PrivateExternCannotBeUsedFromAnotherFile(string body)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(new SourceFile[] {
            new("library.gf", "namespace detail { private extern int abs(int value); }"),
            new("main.gf", "using detail; int main() { " + body + " }")
        }));
        Assert.Contains("private", error.Message);
    }
}
