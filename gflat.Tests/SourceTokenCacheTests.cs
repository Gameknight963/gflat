using gflat.ast;
using gflat.diagnostics;

namespace gflat.Tests;

public sealed class SourceTokenCacheTests
{
    [Fact]
    public void ReusesLexicalTokensWhileCreatingFreshCheckedTrees()
    {
        var cache = new SourceTokenCache();
        var source = new SourceFile("main.gf", "int Answer() => 42;");
        var first = new AnalysisSnapshot([source], new() { NoStdLib = true }, tokenCache: cache);
        var second = new AnalysisSnapshot([new SourceFile(source.Path, source.Text)], new() { NoStdLib = true }, tokenCache: cache);
        Assert.Same(first.SourceTokens[source.Id][0], second.SourceTokens[source.Id][0]);
        Assert.NotSame(first.Nodes.First(node => node.Node is MethodDeclaration).Node,
            second.Nodes.First(node => node.Node is MethodDeclaration).Node);
        var changed = new AnalysisSnapshot([new SourceFile(source.Path, "int Answer() => 7;")], new() { NoStdLib = true }, tokenCache: cache);
        Assert.NotSame(first.SourceTokens[source.Id][0], changed.SourceTokens[source.Id][0]);
        Assert.Contains(changed.SourceTokens[source.Id], token => token.Text == "7");
    }

    [Fact]
    public void NestedGenericParsingDoesNotSplitCachedShiftTokens()
    {
        var cache = new SourceTokenCache();
        var source = new SourceFile("nested.gf", "struct Box<T> { public T Value; } Box<Box<int>> item;");
        for (int pass = 0; pass < 2; pass++)
        {
            var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true }, tokenCache: cache);
            Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            Assert.Contains(snapshot.SourceTokens[source.Id], token => token.Kind == TokenKind.GreaterGreater);
        }
    }

    [Fact]
    public void ReflectedGenericCallFallbackPreservesCachedLexicalTokens()
    {
        var cache = new SourceTokenCache();
        var source = new SourceFile("reflected-call.gf", """
            struct Box<T> { public T Value; }
            const int Identity<T>(int value) => value;
            const reflection info = reflect(Identity<Box<int>>(7));
            int main() => 0;
            """);
        var lexical = Lexer.Tokenize(source)
            .Select(token => (token.Kind, token.Text, token.Start, token.End)).ToArray();
        Assert.Single(lexical, token => token.Kind == TokenKind.GreaterGreater);
        for (int pass = 0; pass < 2; pass++)
        {
            var snapshot = new AnalysisSnapshot([new SourceFile(source.Path, source.Text)],
                new() { NoStdLib = true }, tokenCache: cache);
            Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            var reflected = Assert.Single(snapshot.Nodes.Select(syntax => syntax.Node).OfType<ReflectExpression>());
            Assert.IsType<CallExpression>(reflected.Target);
            Assert.Equal(lexical, snapshot.SourceTokens[source.Id]
                .Select(token => (token.Kind, token.Text, token.Start, token.End)));
        }
    }

    [Fact]
    public void LexicalErrorsAreReplayedAndClearAfterAnEdit()
    {
        var cache = new SourceTokenCache();
        var source = new SourceFile("broken.gf", "int main() { return `; }");
        var first = new AnalysisSnapshot([source], new() { NoStdLib = true }, tokenCache: cache);
        var second = new AnalysisSnapshot([source], new() { NoStdLib = true }, tokenCache: cache);
        Assert.NotEmpty(first.Diagnostics);
        Assert.Equal(first.Diagnostics.Select(error => (error.Descriptor.Id, error.Message, error.Span)),
            second.Diagnostics.Select(error => (error.Descriptor.Id, error.Message, error.Span)));
        var fixedSource = new SourceFile(source.Path, "int main() { return 0; }");
        Assert.Empty(new AnalysisSnapshot([fixedSource], new() { NoStdLib = true }, tokenCache: cache).Diagnostics);
    }

    [Fact]
    public void CallerCannotChangeFocusedSourcesAfterSnapshotConstruction()
    {
        var source = new SourceFile("main.gf", "void Main() {}");
        var focus = new HashSet<SourceId> { source.Id };
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true }, bodySources: focus);
        focus.Clear();
        Assert.Contains(source.Id, snapshot.BodySources!);
    }
}
