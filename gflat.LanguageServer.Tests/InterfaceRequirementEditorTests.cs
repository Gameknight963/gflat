using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class InterfaceRequirementEditorTests
{
    [Fact]
    public void RequirementsParticipateInTypeHierarchyAndMemberImplementationNavigation()
    {
        const string code = """
            interface Root { int Read(); }
            interface View : Root { int Read(); int Extra(); }
            struct Reader : View { public int Read() => 42; public int Extra() => 1; }
            """;
        var source = new SourceFile(Path.GetFullPath("interface-requirement-navigation.gf"), code);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, [source]);
        int offset = code.IndexOf("View :", StringComparison.Ordinal);
        var view = Assert.Single(model.PrepareTypeHierarchy(source.Path, offset));
        Assert.Equal("Root", Assert.Single(model.TypeHierarchy(view.Data.Path, view.Data.Offset, false)).Name);
        Assert.Equal("Reader", Assert.Single(model.TypeHierarchy(view.Data.Path, view.Data.Offset, true)).Name);
    }

    [Fact]
    public void MissingRequirementFixDeclaresSignaturesWithoutBodies()
    {
        const string code = "interface Root { int Read(); int Value { immutable get; } } interface View : Root { }";
        var source = new SourceFile(Path.GetFullPath("interface-requirement-action.gf"), code);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
        Assert.Contains(snapshot.Diagnostics, diagnostic => diagnostic.Message.Contains("must explicitly declare", StringComparison.Ordinal));
        var model = new EditorModel(snapshot, [source]);
        int offset = code.IndexOf("View :", StringComparison.Ordinal);
        var action = Assert.Single(model.CodeActions(source.Path, Workspace.ToRange(source.Span(offset, 0))),
            action => action.Title == "Implement Root");
        string text = string.Join("\n", action.Edit!.Changes.Values.SelectMany(edits => edits).Select(edit => edit.NewText));
        Assert.Contains("int Read();", text);
        Assert.Contains("immutable get;", text);
        Assert.DoesNotContain("throw", text);
        var edit = Assert.Single(action.Edit.Changes.Values.SelectMany(edits => edits));
        int insertion = code.LastIndexOf('}');
        string updated = code.Insert(insertion, edit.NewText);
        var fixedSnapshot = new AnalysisSnapshot([new SourceFile(source.Path, updated)], new() { NoStdLib = true });
        Assert.DoesNotContain(fixedSnapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }
}
