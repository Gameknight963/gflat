using gflat.ast;
using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class AccessibilityModifierTests
{
    private const string Message = "A declaration can have only one accessibility modifier";

    [Theory]
    [InlineData("public private class Holder{}", "private")]
    [InlineData("abstract public private class Holder{}", "private")]
    [InlineData("private public struct Holder{}", "public")]
    [InlineData("public public enum Value{A}", "public")]
    [InlineData("public internal interface I{}", "internal")]
    [InlineData("namespace Library{public private alias Value=int;}", "private")]
    [InlineData("public public int Read()=>42;", "public")]
    [InlineData("class Holder{public private int Read()=>42;}", "private")]
    [InlineData("class Holder{protected private int Read()=>42;}", "private")]
    [InlineData("class Holder{public private int Value=>42;}", "private")]
    [InlineData("struct Holder<T>{private private int value;}", "private")]
    [InlineData("class Holder{public private class Nested{}}", "private")]
    [InlineData("private public extern int Foreign(int value);", "public")]
    [InlineData("private extern public int Foreign(int value);", "public")]
    [InlineData("extern private public int Foreign(int value);", "public")]
    [InlineData("extern private private int Foreign(int value);", "private")]
    [InlineData("private extern private int Foreign(int value);", "private")]
    public void TheSecondAccessibilityModifierIsRejectedAtItsToken(string text, string modifier)
    {
        var source = new SourceFile("accessibility-modifier.gf", text);
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(source, options: new() { NoStdLib = true }));
        Assert.Equal(Message, error.Description);
        Assert.Equal(text.LastIndexOf(modifier, StringComparison.Ordinal), error.Span.Start);
        Assert.Equal(modifier, text.Substring(error.Span.Start, error.Span.Length));
        var diagnostic = Assert.Single(Compiler.Analyze([source], new() { NoStdLib = true }));
        Assert.Equal(Message, diagnostic.Message);
        Assert.Equal(error.Span, diagnostic.Span);
    }

    [Theory]
    [InlineData("private extern public", TokenKind.Private)]
    [InlineData("extern private public", TokenKind.Private)]
    [InlineData("public extern private", TokenKind.Public)]
    [InlineData("extern public private", TokenKind.Public)]
    public void RecoveryRetainsTheFirstExplicitExternAccessAndItsPrivacy(string modifiers, TokenKind expected)
    {
        var library = new SourceFile("extern-library.gf", modifiers + " int Foreign(int value);");
        var consumer = new SourceFile("extern-consumer.gf", "int Read()=>Foreign(42);");
        var snapshot = new AnalysisSnapshot([library, consumer], new() { NoStdLib = true });
        Assert.Single(snapshot.Diagnostics, d => d.Message == Message);
        Assert.Equal(expected, Assert.Single(snapshot.Nodes.Select(n => n.Node).OfType<ExternDeclaration>()).Accessibility);
        Assert.Equal(expected == TokenKind.Private, snapshot.Diagnostics.Any(d => d.Message.Contains("private to its source file", StringComparison.Ordinal)));
    }

    [Fact]
    public void ImplicitDefaultsDoNotCountAsExplicitAccessibilityModifiers()
        => Compiler.Check("""
            public class Holder{private int value;public int Read()=>value;}
            abstract public class Base{public abstract int Read();}
            public interface I{int Read();}
            public alias Number=int;
            private extern int First(int value);
            extern private int Second(int value);
            public int Read()=>42;
            """, options: new() { NoStdLib = true });
}
