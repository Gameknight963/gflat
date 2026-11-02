using gflat.TestSupport;

namespace gflat.LanguageServer.Tests;

public sealed class PreprocessorEditorTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "gflat-pp-editor-" + Guid.NewGuid().ToString("N"));
    private string Write(string name, string text)
    {
        string path = Path.GetFullPath(Path.Combine(root, name));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [Fact]
    public void HeaderChangesUseUnsavedTextAndInvalidateOnlyChangedAnalyses()
    {
        Write("app.gfproj", ProjectFileFixture.Xml(content: "<ItemGroup><IncludeDirectory Include=\"include\"/><Define Include=\"VALUE=42\"/></ItemGroup>"));
        string main = Write("main.gf", "#include \"value.gfh\"\nint main()=>Answer();");
        string header = Write("include/value.gfh", "int Answer()=>VALUE;");
        var documents = new Dictionary<string, OpenDocument>(Workspace.Paths) { [main] = new(main, File.ReadAllText(main), 1) };
        var projects = new ProjectWorkspace();
        var first = Workspace.Analyze(root, documents, projects);
        Assert.All(first.Diagnostics.Values, Assert.Empty);
        Assert.Contains(header, first.WatchedPaths);
        Assert.Same(first.Models![main], first.Models[header]);
        Assert.Same(first.Models[main], Workspace.Analyze(root, documents, projects).Models![main]);
        documents[header] = new(header, "int Answer()=>missing;", 1);
        var edited = Workspace.Analyze(root, documents, projects);
        Assert.NotSame(first.Models[main], edited.Models![main]);
        Assert.Contains(edited.Diagnostics[header], diagnostic => diagnostic.Message.Contains("missing"));
        documents.Remove(header);
        Assert.All(Workspace.Analyze(root, documents, projects).Diagnostics.Values, Assert.Empty);
        string shadow = Write("value.gfh", "int Other()=>0;");
        var shadowed = Workspace.Analyze(root, documents, projects);
        Assert.Contains(shadowed.Diagnostics[main], diagnostic => diagnostic.Message.Contains("Answer"));
        File.Delete(shadow);
        Assert.All(Workspace.Analyze(root, documents, projects).Diagnostics.Values, Assert.Empty);
    }

    [Fact]
    public void MissingIncludesAreWatchedAndRecoverWhenCreated()
    {
        Write("app.gfproj", ProjectFileFixture.Xml());
        string main = Write("main.gf", "#include \"later.gfh\"\nint main()=>Answer();");
        var documents = new Dictionary<string, OpenDocument>(Workspace.Paths) { [main] = new(main, File.ReadAllText(main), 1) };
        var projects = new ProjectWorkspace();
        var missing = Workspace.Analyze(root, documents, projects);
        Assert.Contains(Path.Combine(root, "later.gfh"), missing.WatchedPaths);
        Assert.Contains(missing.Diagnostics[main], diagnostic => diagnostic.Message.Contains("was not found"));
        Write("later.gfh", "int Answer()=>42;");
        var recovered = Workspace.Analyze(root, documents, projects);
        Assert.NotSame(missing.Models![main], recovered.Models![main]);
        Assert.All(recovered.Diagnostics.Values, Assert.Empty);
    }
    [Fact]
    public void MacroHoverAndDefinitionFollowTheSelectedDefinitionAcrossHeaders()
    {
        Write("app.gfproj", ProjectFileFixture.Xml());
        string header = Write("answer.gfh", "#define ANSWER 42");
        const string code = "#include \"answer.gfh\"\nint main()=>ANSWER;";
        string main = Write("main.gf", code);
        var documents = new Dictionary<string, OpenDocument>(Workspace.Paths) { [main] = new(main, code, 1) };
        var result = Workspace.Analyze(root, documents);
        int offset = code.IndexOf("ANSWER", StringComparison.Ordinal);
        var hover = Assert.IsType<HoverResult>(result.Models![main].Hover(main, offset));
        Assert.Contains("#define ANSWER 42", System.Text.Json.JsonSerializer.Serialize(hover));
        var definition = Assert.IsType<Location>(result.Models[main].Definition(main, offset));
        Assert.Equal(Workspace.FileUri(header), definition.Uri);
        Assert.Equal(new Position(0, 8), definition.Range.Start);
    }

}
