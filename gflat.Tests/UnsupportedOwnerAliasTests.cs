using gflat.CompileExceptions;
using gflat.diagnostics;

namespace gflat.Tests;

public sealed class UnsupportedOwnerAliasTests
{
    private const string Message = "Type aliases inside classes or structs are not supported; declare the alias in a namespace or local scope";

    [Theory]
    [InlineData("class Holder { public alias Slot=int; }")]
    [InlineData("struct Holder { public alias Slot=int; }")]
    [InlineData("static class Holder { public alias Slot=int; }")]
    [InlineData("class Holder<T> { public alias Slot=T; }")]
    [InlineData("struct Holder<T> { public alias Slot=T; }")]
    [InlineData("static class Holder<T> { public alias Slot=T; }")]
    [InlineData("namespace Library { class Outer { public struct Holder { public alias Slot=int; } } }")]
    [InlineData("struct Holder { public alias Slot=Missing; }")]
    [InlineData("alias Slot=int; struct Holder { public alias Slot=int*; public Slot value; }")]
    [InlineData("struct Holder { public alias Slot=int; } int main(){Holder::Slot value=1;return value;}")]
    public void OwnerAliasesAreRejectedAtTheirDeclaration(string text)
    {
        var source = new SourceFile("owner-alias.gf", text);
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check([source], options: new() { NoStdLib = true }));
        Assert.Equal(Message, error.Description);
        Assert.Equal(source.Path, error.Span.Source?.Path);
        Assert.Contains("alias Slot", source.Text.Substring(error.Span.Start, error.Span.Length));

        var diagnostic = Assert.Single(Compiler.Analyze([source], new() { NoStdLib = true }), d => d.Message == Message);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(error.Span, diagnostic.Span);
    }

    [Fact]
    public void NamespaceAndLocalAliasesRemainAvailable()
        => Assert.Equal(42, CompilerTestHelper.Run("""
            namespace Library { public alias Number=int; }
            int main() { alias Local=Library::Number; Local value=42; return value; }
            """).ExitCode);
}
