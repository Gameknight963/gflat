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

    [Fact]
    public void NativeMacroHoverShowsExpandedTokensAndDirectiveHelp()
    {
        const string code = "#define VALUE 7\n#define DOUBLE(x) (x + VALUE)\nint main()=>DOUBLE(VALUE);";
        var source = new SourceFile(Path.Combine(root, "main.gf"), code);
        var model = new EditorModel(new AnalysisSnapshot([source]), [source]);
        var presentation = new VisualStudioHover(System.Text.Json.JsonSerializer.SerializeToElement(new { }));
        var hover = Assert.IsType<HoverResult>(model.Hover(source.Path, code.LastIndexOf("DOUBLE", StringComparison.Ordinal), presentation));
        Assert.Contains("**Expands to:** ( 7 + 7 )", hover.Contents.Value);
        string rich = System.Text.Json.JsonSerializer.Serialize(hover.RawContent);
        Assert.Contains("cppMacro", rich);
        Assert.Contains("preprocessor keyword", rich);
        Assert.Contains("\"ClassificationTypeName\":\"cppMacro\",\"Text\":\"VALUE\"", rich);
        Assert.NotNull(hover.RawContent);
        if (Environment.GetEnvironmentVariable("GFLAT_HOVER_CAPTURE") is { Length: > 0 } capture)
            File.WriteAllText(capture + ".macro", System.Text.Json.JsonSerializer.Serialize(hover, Protocol.JsonOptions));
        var directive = Assert.IsType<HoverResult>(model.Hover(source.Path, 2, presentation));
        Assert.Contains("Defines an object-like", directive.Contents.Value);
        Assert.NotNull(directive.RawContent);
    }

    [Fact]
    public void MacroSemanticTokensCoverDefinitionsBodiesAndCalls()
    {
        const string code = "#define VALUE 7\n#define DOUBLE(x) (VALUE + x)\nint main()=>DOUBLE(VALUE);";
        var source = new SourceFile(Path.Combine(root, "main.gf"), code);
        var model = new EditorModel(new AnalysisSnapshot([source]), [source]);
        var json = System.Text.Json.JsonSerializer.SerializeToElement(model.SemanticTokens(source.Path));
        int[] data = json.GetProperty("data").EnumerateArray().Select(e => e.GetInt32()).ToArray();
        var actual = new List<(int Line, int Column, string Kind)>();
        int line = 0, column = 0;
        for (int i = 0; i < data.Length; i += 5)
        {
            column = data[i] == 0 ? column + data[i + 1] : data[i + 1];
            line += data[i];
            actual.Add((line, column, EditorModel.TokenTypes[data[i + 3]]));
        }
        Assert.Contains((0, 0, "preprocessor"), actual);
        Assert.Contains((0, 8, "macro"), actual);
        Assert.Contains((1, 19, "macro"), actual);
        Assert.Contains((2, 12, "macro"), actual);
        Assert.Contains((2, 19, "macro"), actual);
        Assert.Equal("cppMacro", EditorModel.VisualStudioTokenTypes[Array.IndexOf(EditorModel.TokenTypes, "macro")]);
    }

}
