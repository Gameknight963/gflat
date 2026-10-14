using gflat.LanguageServer;

namespace gflat.LanguageServer.Tests;

public sealed class DocumentHighlightTests
{
    [Theory]
    [InlineData("int F() { int |value = 1; value++; return value; } int G() { int value = 2; return value; }", 3)]
    [InlineData("struct S { public int value; } int F() { int |value = 1; S s = new S(); return value + s.value; }", 2)]
    [InlineData("int Read(int n) => n; int Read(char n) => 1; int F() => |Read(1); int G() => Read('x');", 2)]
    [InlineData("T |Identity<T>(T n) => n; int F() => Identity(1); char G() => Identity('x');", 3)]
    [InlineData("int F() => |missing;", 0)]
    [InlineData("int F() => |42;", 0)]
    public void HighlightsFollowSymbolIdentity(string marked, int count)
    {
        int offset = marked.IndexOf('|');
        var source = new SourceFile(Path.GetFullPath("highlight.gf"), marked.Remove(offset, 1));
        var model = new EditorModel(new AnalysisSnapshot([source]), [source]);
        Assert.Equal(count, model.DocumentHighlights(source.Path, offset).Length);
    }

    [Fact]
    public void OtherDocumentsAreExcluded()
    {
        var source = new SourceFile(Path.GetFullPath("highlight.gf"), "int F() => Answer(); int G() => Answer();");
        var library = new SourceFile(Path.GetFullPath("highlight-library.gf"), "int Answer() => 42; int H() => Answer();");
        var model = new EditorModel(new AnalysisSnapshot([source, library]), [source, library]);
        var actual = model.DocumentHighlights(source.Path, source.Text.IndexOf("Answer", StringComparison.Ordinal));
        Assert.Equal(2, actual.Length);
        Assert.Equal(model.References(source.Path, source.Text.IndexOf("Answer", StringComparison.Ordinal), true)
            .Where(location => location.Uri == Workspace.FileUri(source.Path)).Select(location => location.Range),
            actual.Select(highlight => highlight.Range));
    }
}
