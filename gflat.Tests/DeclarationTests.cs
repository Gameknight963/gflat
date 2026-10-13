using gflat.CompileExceptions;

namespace gflat.Tests;

public class DeclarationTests
{
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
