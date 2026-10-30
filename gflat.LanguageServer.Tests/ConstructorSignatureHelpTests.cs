using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class ConstructorSignatureHelpTests
{
    [Fact]
    public void ActualNestedEnumeratorCreationFormsShareConcreteSignaturesAndKeepTypeTokens()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "StandardLibrary");
        var sources = Directory.GetFiles(directory, "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path))).ToList();
        const string text = """
            using Standard; using Standard::Memory;
            alias IntIterator = Standard::Memory::Span<int>::Enumerator;
            int main() {
                int[1] buffer = [42];
                Span<int>::Enumerator inferred = new(buffer, 1);
                Span<int>::Enumerator explicitType = new Span<int>::Enumerator(buffer, 1);
                IntIterator aliasType = new IntIterator(buffer, 1);
                return 0;
            }
            """;
        var source = new SourceFile(Path.GetFullPath("std-editor/constructor-signatures.gf"), text);
        sources.Add(source);
        var snapshot = new AnalysisSnapshot(sources, new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, sources);
        foreach (string marker in new[] { "new(buffer", "new Span<int>::Enumerator(buffer", "new IntIterator(buffer" })
        {
            int start = text.IndexOf(marker, StringComparison.Ordinal);
            int argument = text.IndexOf('(', start) + 1;
            var help = JsonSerializer.SerializeToElement(model.SignatureHelp(source.Path, argument, visualStudio: true), Protocol.JsonOptions);
            var signature = Assert.Single(help.GetProperty("signatures").EnumerateArray());
            Assert.Contains("int*? storage", signature.GetProperty("label").GetString());
            Assert.Contains("Span<int>::Enumerator", signature.GetProperty("label").GetString());
            Assert.DoesNotContain('$', signature.GetProperty("label").GetString()!);
            Assert.True(signature.TryGetProperty("_vs_colorizedLabel", out _));
            Assert.NotNull(model.Hover(source.Path, start));
            Assert.NotNull(model.Definition(source.Path, start));
        }
        int explicitName = text.IndexOf("new Span<int>::Enumerator", StringComparison.Ordinal) + "new Span<int>::".Length;
        var hover = JsonSerializer.SerializeToElement(model.Hover(source.Path, explicitName), Protocol.JsonOptions);
        Assert.Contains("struct", hover.GetProperty("contents").GetProperty("value").GetString());
        Assert.Equal("struct", Classification(model, source, explicitName));
        int aliasName = text.IndexOf("new IntIterator", StringComparison.Ordinal) + "new ".Length;
        hover = JsonSerializer.SerializeToElement(model.Hover(source.Path, aliasName), Protocol.JsonOptions);
        Assert.Contains("alias IntIterator", hover.GetProperty("contents").GetProperty("value").GetString());
        Assert.Equal("type", Classification(model, source, aliasName));
    }

    [Theory]
    [InlineData("new Box<int>(42l)")]
    [InlineData("new Alias(42l)")]
    [InlineData("new(42l)")]
    public void ExplicitGenericAndAliasCreationsSelectTheActualConstructorOverload(string expression)
    {
        string text = "class Box<T>{public Box(int n){}public Box(long n){}} alias Alias=Box<int>; int main(){Box<int> value=" + expression + ";return 0;}";
        var source = new SourceFile(Path.GetFullPath("constructor-overload.gf"), text);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, [source]);
        int argument = text.LastIndexOf("42l", StringComparison.Ordinal);
        var help = JsonSerializer.SerializeToElement(model.SignatureHelp(source.Path, argument), Protocol.JsonOptions);
        var selected = help.GetProperty("signatures")[help.GetProperty("activeSignature").GetInt32()];
        Assert.Equal(2, help.GetProperty("signatures").GetArrayLength());
        Assert.Contains("long n", selected.GetProperty("label").GetString());
        Assert.Contains("Box<int>", selected.GetProperty("label").GetString());
    }

    [Theory]
    [InlineData("new Box<int>()")]
    [InlineData("new Alias()")]
    public void ExplicitCandidatesRemainAvailableWhileArgumentsAreMissing(string expression)
    {
        string text = "struct Box<T>{public Box(T value){}} alias Alias=Box<int>; int main(){Box<int> box=" + expression + ";return 0;}";
        var source = new SourceFile(Path.GetFullPath("constructor-missing-arguments.gf"), text);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
        Assert.Contains(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, [source]);
        int argument = text.IndexOf('(', text.LastIndexOf("new ", StringComparison.Ordinal)) + 1;
        var help = JsonSerializer.SerializeToElement(model.SignatureHelp(source.Path, argument), Protocol.JsonOptions);
        var selected = Assert.Single(help.GetProperty("signatures").EnumerateArray());
        Assert.Contains("Box<int>", selected.GetProperty("label").GetString());
        Assert.Contains("int value", selected.GetProperty("label").GetString());
    }

    private static string? Classification(EditorModel model, SourceFile source, int offset)
    {
        var token = Assert.Single(Lexer.Tokenize(source), token => token.Start == offset);
        var data = JsonSerializer.SerializeToElement(model.SemanticTokens(source.Path)).GetProperty("data")
            .EnumerateArray().Select(value => value.GetInt32()).ToArray();
        int line = 0, column = 0;
        for (int i = 0; i < data.Length; i += 5)
        {
            line += data[i]; column = data[i] == 0 ? column + data[i + 1] : data[i + 1];
            if (line == token.Line - 1 && column == token.Column - 1) return EditorModel.TokenTypes[data[i + 3]];
        }
        return null;
    }
}
