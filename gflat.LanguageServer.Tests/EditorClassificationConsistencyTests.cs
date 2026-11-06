using System.Text.Json;
using gflat.ast;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class EditorClassificationConsistencyTests
{
    [Fact]
    public void CompletionIncludesEveryPrimitiveTypeAndDefaultValues()
    {
        const string text = "void Use() { }";
        var (model, source) = Analyze(text);
        var items = JsonSerializer.SerializeToElement(model.Completion(source.Path, text.IndexOf('}') - 1), Protocol.JsonOptions)
            .GetProperty("items").EnumerateArray().Select(item => item.GetProperty("label").GetString()).ToArray();
        foreach (string name in new[] { "byte", "sbyte", "short", "ushort", "extralong", "default" })
            Assert.Contains(name, items);
    }

    [Fact]
    public void DefaultValuesUseOrdinaryKeywords()
    {
        const string text = "int Create()=>default; int Explicit()=>default(int);";
        var (model, source) = Analyze(text);
        foreach (var token in Lexer.Tokenize(source).Where(t => t.Kind == TokenKind.Default))
            Assert.Equal("modifier", Classification(model, source, token.Start));
        Assert.Equal("keyword", EditorModel.VisualStudioTokenTypes[Array.IndexOf(EditorModel.TokenTypes, "modifier")]);
    }

    [Fact]
    public void FieldsPropertiesAndConstantsUseDistinctThemeClassifications()
    {
        const string text = "struct Item { public int Field; public static const int Limit = 3; public int Property=>Field; } int Use(Item* item){const int local=2;return item.Field+item.Property+Item::Limit+local;}";
        var (model, source) = Analyze(text);
        foreach (var (name, expected) in new[] { ("Field", "field"), ("Property", "property"), ("Limit", "constant"), ("local", "constant") })
        {
            Assert.Equal(expected, Classification(model, source, text.LastIndexOf(name, StringComparison.Ordinal)));
            var hover = Assert.IsType<HoverResult>(model.Hover(source.Path, text.LastIndexOf(name, StringComparison.Ordinal),
                new VisualStudioHover(JsonSerializer.SerializeToElement(new { }))));
            Assert.Contains(expected + " name", JsonSerializer.Serialize(hover.RawContent));
        }
    }

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
        int offset = text.LastIndexOf("Item", StringComparison.Ordinal);
        Assert.Equal(expected, Classification(model, source, offset));
        var icons = JsonSerializer.SerializeToElement(new Dictionary<string, object> {
            ["method.public"] = new { guid = Guid.Empty, id = 7 }
        });
        var hover = JsonSerializer.SerializeToElement(model.Hover(source.Path, offset, new VisualStudioHover(icons)), Protocol.JsonOptions);
        var header = hover.GetProperty("_vs_rawContent").GetProperty("Elements")[0].GetProperty("Elements");
        Assert.Equal("method", header[0].GetProperty("AutomationName").GetString());
        Assert.Contains(header[1].GetProperty("Runs").EnumerateArray(), run =>
            run.GetProperty("Text").GetString() == "Item" && run.GetProperty("ClassificationTypeName").GetString() == expected + " name");
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
        Assert.Equal("field", Classification(model, source, offset));
        Assert.NotNull(model.Hover(source.Path, offset));
        Assert.NotNull(model.Definition(source.Path, offset));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TasksExampleKeepsFieldBindingsThroughImmutableGenericIndexerViews(bool focused)
    {
        var files = StandardTestSources.Combine(StandardTestSources.Collections, StandardTestSources.Console,
            StandardTestSources.Load("core/Text/parsing.gf")).ToList();
        var source = new SourceFile("tasks.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "EditorFixtures", "Tasks.gf")));
        files.Add(source);
        var snapshot = new AnalysisSnapshot(files, new() { NoStdLib = true },
            bodySources: focused ? new HashSet<SourceId> { source.Id } : null);
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, files);
        foreach (var token in Lexer.Tokenize(source).Where(token => token.Text is "Done" or "Title"))
        {
            Assert.Equal("field", Classification(model, source, token.Start));
            Assert.NotNull(model.Hover(source.Path, token.Start));
            Assert.NotNull(model.Definition(source.Path, token.Start));
        }
    }

    [Fact]
    public void ShippedQueryExtensionsKeepGenericNamesAndCreationKeywordsClassified()
    {
        var files = StandardTestSources.Queries.ToList();
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
