using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class LazyReferenceQueryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReferencesIncludeUnqueriedFilesAndKeepOverloadsSeparate(bool colorAnotherFile)
    {
        var (model, library, main, other) = Calls();
        if (colorAnotherFile) model.SemanticTokens(other.Path);
        int use = main.Text.IndexOf("Answer(1)", StringComparison.Ordinal);
        Location[] expected = [At(library, "Answer(int"), At(library, "Answer(3)"),
            At(main, "Answer(1)"), At(main, "Answer(2)"), At(other, "Answer(4)")];
        var actual = model.References(main.Path, use, includeDeclaration: true);
        Assert.Equal(expected.OrderBy(location => location.Uri, StringComparer.Ordinal)
            .ThenBy(location => location.Range.Start.Line).ThenBy(location => location.Range.Start.Character), actual);
        Assert.Equal(actual, model.References(library.Path,
            library.Text.IndexOf("Answer(int", StringComparison.Ordinal), includeDeclaration: true));
        Assert.Equal(4, model.References(main.Path, use, includeDeclaration: false).Length);
    }

    [Fact]
    public void HighlightsFindAllOccurrencesAfterOnlyAnotherDocumentWasColored()
    {
        var (model, library, main, _) = Calls();
        model.SemanticTokens(library.Path);
        int use = main.Text.IndexOf("Answer(1)", StringComparison.Ordinal);
        var highlights = model.DocumentHighlights(main.Path, use);
        Assert.Null(typeof(EditorModel).GetField("referenceIndex",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(model));
        Assert.Equal(new[] { At(main, "Answer(1)").Range, At(main, "Answer(2)").Range },
            highlights.Select(highlight => highlight.Range));
        Assert.Equal(5, model.References(main.Path, use, includeDeclaration: true).Length);
    }

    [Fact]
    public void ColdRenameCollectsEveryParameterUseIncludingNameof()
    {
        const string text = "int Probe(int value) { nameof(value); int other = value; return value + other; }";
        var source = new SourceFile(Path.GetFullPath("cold-rename.gf"), text);
        var model = Analyze(source);
        int declaration = text.IndexOf("value", StringComparison.Ordinal);
        var edits = model.Rename(source.Path, declaration, "renamed");
        Assert.Equal(4, edits.Length);
        Assert.All(edits, edit => {
            Assert.Equal(source.Path, edit.Path);
            Assert.Equal("renamed", edit.NewText);
        });
        Assert.Equal(4, model.References(source.Path, declaration, includeDeclaration: true).Length);
    }

    [Fact]
    public void ColdRenameRejectsCaptureOfAnUnqueriedGlobalUse()
    {
        var library = new SourceFile(Path.GetFullPath("cold-capture-library.gf"), "const int Global = 2;");
        var source = new SourceFile(Path.GetFullPath("cold-capture-main.gf"),
            "int Probe(int value) => value + Global;");
        var model = Analyze(library, source);
        int declaration = source.Text.IndexOf("value", StringComparison.Ordinal);
        var error = Assert.Throws<InvalidOperationException>(() => model.Rename(source.Path, declaration, "Global"));
        Assert.Contains("declaration", error.Message);
    }

    private static (EditorModel Model, SourceFile Library, SourceFile Main, SourceFile Other) Calls()
    {
        var library = new SourceFile(Path.GetFullPath("cold-reference-library.gf"),
            "int Answer(int value) => value; int Answer(bool value) => 0; int Library() => Answer(3);");
        var main = new SourceFile(Path.GetFullPath("cold-reference-main.gf"),
            "int Main() => Answer(1) + Answer(2);");
        var other = new SourceFile(Path.GetFullPath("cold-reference-other.gf"),
            "int Other() => Answer(4); int Flag() => Answer(true);");
        return (Analyze(library, main, other), library, main, other);
    }

    private static EditorModel Analyze(params SourceFile[] files)
    {
        var snapshot = new AnalysisSnapshot(files, new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        return new EditorModel(snapshot, files);
    }

    private static Location At(SourceFile source, string occurrence)
        => new(Workspace.FileUri(source.Path), Workspace.ToRange(source.Span(
            source.Text.IndexOf(occurrence, StringComparison.Ordinal), "Answer".Length)));
}
