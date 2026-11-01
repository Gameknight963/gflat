using System.Text.Json;
using gflat.ast;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class EditorClassificationConsistencyTests
{
    private static (EditorModel Model, SourceFile Source) Analyze(string text)
    {
        var source = new SourceFile("classification.gf", text);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        return (new(snapshot, [source]), source);
    }

    private static string? Classification(EditorModel model, SourceFile source, int offset)
    {
        var data = JsonSerializer.SerializeToElement(model.SemanticTokens(source.Path), Protocol.JsonOptions)
            .GetProperty("data").EnumerateArray().Select(value => value.GetInt32()).ToArray();
        int line = 0, column = 0;
        for (int index = 0; index < data.Length; index += 5)
        {
            line += data[index];
            column = data[index] == 0 ? column + data[index + 1] : data[index + 1];
            var span = source.Span(offset);
            if (span.Line - 1 == line && span.Column - 1 == column) return EditorModel.TokenTypes[data[index + 3]];
        }
        return null;
    }

    [Fact]
    public void ExtensionMethodGenericParametersKeepTheirOwnDeclarationScope()
    {
        const string text = "struct Flow<T> {} struct Result<T, K> {} extension<T> Flow<T> { public Result<T, K> Select<K>(K(T)* selector) => new Result<T, K>(); }";
        var (model, source) = Analyze(text);
        foreach (var token in Lexer.Tokenize(source).Where(token => token.Text is "T" or "K"))
            Assert.True(Classification(model, source, token.Start) == "typeParameter", $"{token.Text} at {token.Start}: {Classification(model, source, token.Start) ?? "missing"}");
    }

    [Theory]
    [InlineData("struct", "struct")]
    [InlineData("class", "class")]
    public void ConstructorDeclarationsUseTheirContainingTypeColor(string declaration, string expected)
    {
        string text = declaration + " Item { public Item() {} }";
        var (model, source) = Analyze(text);
        Assert.Equal(expected, Classification(model, source, text.LastIndexOf("Item", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("class Outer<T> { public struct Enumerator { public Enumerator() {} } }", "struct")]
    [InlineData("struct Outer<T> { public class Enumerator { public Enumerator() {} } }", "class")]
    public void NestedConstructorsUseTheirImmediateContainingType(string text, string expected)
    {
        var (model, source) = Analyze(text);
        Assert.Equal(expected, Classification(model, source, text.LastIndexOf("Enumerator", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("class", "class")]
    [InlineData("struct", "struct")]
    public void StaticConstructorDeclarationsUseTheirContainingTypeColor(string declaration, string expected)
    {
        string text = declaration + " Item { static Item() {} }";
        var (model, source) = Analyze(text);
        Assert.Equal(expected, Classification(model, source, text.LastIndexOf("Item", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("immutable(Task)*")]
    [InlineData("readonly(Task)*")]
    [InlineData("Task*")]
    public void ProtectedPointerFieldsHaveClassificationHoverAndNavigation(string receiver)
    {
        string text = "struct Task { public bool Done; } void List(" + receiver + " task) { if (task.Done) {} }";
        var (model, source) = Analyze(text);
        int offset = text.LastIndexOf("Done", StringComparison.Ordinal);
        Assert.Equal("property", Classification(model, source, offset));
        Assert.NotNull(model.Hover(source.Path, offset));
        Assert.NotNull(model.Definition(source.Path, offset));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TasksExampleKeepsFieldBindingsThroughImmutableGenericIndexerViews(bool focused)
    {
        string root = Path.Combine(AppContext.BaseDirectory, "StandardLibrary");
        var files = Directory.GetFiles(root, "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path))).ToList();
        var source = new SourceFile("tasks.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "EditorFixtures", "Tasks.gf")));
        files.Add(source);
        var snapshot = new AnalysisSnapshot(files, new() { NoStdLib = true },
            bodySources: focused ? new HashSet<SourceId> { source.Id } : null);
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, files);
        foreach (var token in Lexer.Tokenize(source).Where(token => token.Text is "Done" or "Title"))
        {
            Assert.Equal("property", Classification(model, source, token.Start));
            Assert.NotNull(model.Hover(source.Path, token.Start));
            Assert.NotNull(model.Definition(source.Path, token.Start));
        }
    }

    [Fact]
    public void ShippedQueryExtensionsKeepGenericNamesAndCreationKeywordsClassified()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "StandardLibrary");
        var files = Directory.GetFiles(root, "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path))).ToList();
        var selected = files.Where(file => Path.GetFileName(file.Path) is "enumerable_slice.gf" or "enumerable_distinct_by.gf").ToArray();
        Assert.Equal(2, selected.Length);
        var snapshot = new AnalysisSnapshot(files, new() { NoStdLib = true },
            bodySources: selected.Select(file => file.Id).ToHashSet());
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, files);
        foreach (var source in selected)
            foreach (var token in Lexer.Tokenize(source))
            {
                if (token.Kind == TokenKind.New) Assert.Equal("modifier", Classification(model, source, token.Start));
                if (token.Text is not ("T" or "S" or "E" or "K")) continue;
                Assert.Equal("typeParameter", Classification(model, source, token.Start));
                Assert.NotNull(model.Definition(source.Path, token.Start));
                Assert.Contains("type parameter", JsonSerializer.Serialize(model.Hover(source.Path, token.Start), Protocol.JsonOptions));
            }
    }

    [Fact]
    public void CreationKeywordsHaveExplicitKeywordClassification()
    {
        const string text = "struct Box<T> { public Box(T value) {} } void Make() { Box<Box<int>> item = new Box<Box<int>>(new Box<int>(1)); }";
        var (model, source) = Analyze(text);
        foreach (var token in Lexer.Tokenize(source).Where(token => token.Kind == TokenKind.New))
            Assert.Equal("modifier", Classification(model, source, token.Start));
    }
}
