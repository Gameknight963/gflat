using gflat.ast;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class SourceSyntaxCacheTests
{
    [Fact]
    public void BodyNodeAttributesRemainIndependentAcrossCachedSnapshots()
    {
        var source = new SourceFile(Path.GetFullPath("cached-body-attributes.gf"), "int Read() => 42;");
        var cache = new SourceSyntaxCache();
        var first = new AnalysisSnapshot([source], Bare, checkTypes: false, syntaxCache: cache);
        var literal = Assert.Single(first.Nodes.Select(syntax => syntax.Node).OfType<LiteralExpression>());
        literal.Attributes.Add(new("Marker", [], 1));
        for (int pass = 0; pass < 2; pass++)
        {
            var next = new AnalysisSnapshot([source], Bare, checkTypes: false, syntaxCache: cache);
            var nextLiteral = Assert.Single(next.Nodes.Select(syntax => syntax.Node).OfType<LiteralExpression>());
            Assert.Empty(nextLiteral.Attributes);
            Assert.NotSame(literal.Attributes, nextLiteral.Attributes);
        }
    }

    private static readonly CompilationOptions Bare = new() { NoStdLib = true };

    [Fact]
    public void ReusedSyntaxRemainsIsolatedFromCheckingAndLaterMutations()
    {
        var source = new SourceFile(Path.GetFullPath("cached-generic.gf"), """
            abstract class Attribute { public Attribute() {} }
            class Tag : Attribute { public int Value; public Tag(int value) { Value = value; } }
            [Tag(7)] struct Box<T>
            {
                public T Value;
                public int Count { get; set; }
                public Box(T value) { Value = move value; }
                public U Echo<U>(U value) => value;
            }
            alias Item = Box<int>;
            int main() { Item item = new(7); item.Count = 35; return item.Echo<int>(item.Value) + item.Count; }
            """);
        var cache = new SourceSyntaxCache();
        var first = new AnalysisSnapshot([source], Bare, syntaxCache: cache);
        Assert.DoesNotContain(first.Diagnostics, issue => issue.Severity == DiagnosticSeverity.Error);
        var firstNodes = first.Nodes.Select(syntax => syntax.Node).ToHashSet<AstNode>(ReferenceEqualityComparer.Instance);
        int expectedCount = first.Nodes.Count;
        var body = first.Nodes.Select(syntax => syntax.Node).OfType<MethodDeclaration>().First(method => method.Name == "main").Body!;
        body.Statements.Clear();
        first.Nodes.Select(syntax => syntax.Node).OfType<FieldDeclaration>().First().Attributes.Add(new("MissingAttribute", [], 1));
        var second = new AnalysisSnapshot([new SourceFile(source.Path, source.Text)], Bare, syntaxCache: cache);
        Assert.True(cache.ReusedTrees > 0);
        Assert.Equal(expectedCount, second.Nodes.Count);
        Assert.DoesNotContain(second.Diagnostics, issue => issue.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(second.Nodes, syntax => firstNodes.Contains(syntax.Node));
    }

    [Theory]
    [InlineData("int F( { return missing; }")]
    [InlineData("int F() { return 1 } int G() => 2;")]
    public void RecoveryDiagnosticsAreReplayedWithoutAccumulating(string text)
    {
        var source = new SourceFile(Path.GetFullPath("cached-recovery.gf"), text);
        var expected = new AnalysisSnapshot([source], Bare);
        var cache = new SourceSyntaxCache();
        for (int pass = 0; pass < 3; pass++)
        {
            var actual = new AnalysisSnapshot([source], Bare, syntaxCache: cache);
            Assert.Equal(expected.Diagnostics.Select(Shape), actual.Diagnostics.Select(Shape));
        }
        Assert.True(cache.ReusedTrees >= 2);
    }

    [Fact]
    public void ChangedSourceTextReplacesItsTemplate()
    {
        string path = Path.GetFullPath("cached-edit.gf");
        var cache = new SourceSyntaxCache();
        _ = new AnalysisSnapshot([new(path, "int F() => Missing();")], Bare, syntaxCache: cache);
        var repaired = new AnalysisSnapshot([new(path, "int F() => 42;")], Bare, syntaxCache: cache);
        Assert.DoesNotContain(repaired.Diagnostics, issue => issue.Severity == DiagnosticSeverity.Error);
        Assert.Contains(repaired.Nodes, syntax => syntax.Node is LiteralExpression { Token.Text: "42" });
    }

    [Fact]
    public void MacroOptionsAreNeverHiddenByThePlainSyntaxCache()
    {
        var source = new SourceFile(Path.GetFullPath("cached-macro.gf"), "int F() => VALUE;");
        var cache = new SourceSyntaxCache();
        foreach (string number in new[] { "1", "2", "3" })
        {
            var options = Bare with { Defines = new Dictionary<string, string> { ["VALUE"] = number } };
            var snapshot = new AnalysisSnapshot([source], options, syntaxCache: cache);
            Assert.DoesNotContain(snapshot.Diagnostics, issue => issue.Severity == DiagnosticSeverity.Error);
            Assert.Contains(snapshot.Nodes, syntax => syntax.Node is LiteralExpression literal && literal.Token.Text == number);
        }
        Assert.Equal(0, cache.ReusedTrees);
    }

    [Fact]
    public async Task ConcurrentAnalysesReceiveSeparateTrees()
    {
        var source = new SourceFile(Path.GetFullPath("cached-concurrent.gf"), "struct Item { public int Value { get; set; } } int F() { Item item = new(); item.Value = 42; return item.Value; }");
        var cache = new SourceSyntaxCache();
        var snapshots = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            new AnalysisSnapshot([source], Bare, syntaxCache: cache))));
        var previous = new HashSet<AstNode>(ReferenceEqualityComparer.Instance);
        foreach (var snapshot in snapshots)
        {
            Assert.DoesNotContain(snapshot.Diagnostics, issue => issue.Severity == DiagnosticSeverity.Error);
            var nodes = snapshot.Nodes.Select(syntax => syntax.Node).ToHashSet<AstNode>(ReferenceEqualityComparer.Instance);
            Assert.False(previous.Overlaps(nodes));
            previous.UnionWith(nodes);
        }
        Assert.True(cache.ReusedTrees >= 7);
    }

    private static object Shape(Diagnostic issue) => new { issue.Descriptor.Id, issue.Message, issue.Span.Start, issue.Span.Length };
}
