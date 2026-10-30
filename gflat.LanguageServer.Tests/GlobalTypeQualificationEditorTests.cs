using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class GlobalTypeQualificationEditorTests
{
    [Theory]
    [InlineData("struct", "class")]
    [InlineData("class", "struct")]
    public void UninstantiatedRootedNestedTypesKeepRootColorHoverAndDefinition(string rootKind, string shadowKind)
    {
        string libraryText = "namespace A {public struct Outer<T> {public " + rootKind + " Leaf {}}}";
        string callerText = "namespace Client {namespace A {public struct Outer<T> {public " + shadowKind + " Leaf {}}}" +
            "public struct Box<T> {public global::A::Outer<T>::Leaf Read()=>new global::A::Outer<T>::Leaf();}}";
        var library = new SourceFile(Path.GetFullPath("rooted-editor-library.gf"), libraryText);
        var caller = new SourceFile(Path.GetFullPath("rooted-editor-main.gf"), callerText);
        SourceFile[] files = [library, caller];
        var snapshot = new AnalysisSnapshot(files, new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, files);
        int declaration = libraryText.IndexOf("Leaf", StringComparison.Ordinal);
        object? expected = model.Definition(library.Path, declaration);
        Assert.NotNull(expected);
        var classifications = Classifications(model, caller);
        var uses = Lexer.Tokenize(caller, new DiagnosticBag()).Where(token => token.Text == "Leaf" &&
            token.Start > callerText.IndexOf("public struct Box", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, uses.Length);
        foreach (var use in uses)
        {
            Assert.Equal(rootKind, classifications.GetValueOrDefault(use.Start));
            Assert.Equal(Json(expected), Json(model.Definition(caller.Path, use.Start)));
            var hover = JsonSerializer.SerializeToElement(model.Hover(caller.Path, use.Start), Protocol.JsonOptions);
            Assert.NotEqual(JsonValueKind.Null, hover.ValueKind);
            Assert.Contains(rootKind, hover.GetProperty("contents").GetProperty("value").GetString());
        }
        int namespaceDeclaration = libraryText.IndexOf("A {", StringComparison.Ordinal);
        object? expectedNamespace = model.Definition(library.Path, namespaceDeclaration);
        Assert.NotNull(expectedNamespace);
        foreach (var token in Lexer.Tokenize(caller, new DiagnosticBag()).Where(token => token.Text == "A" &&
            token.Start > callerText.IndexOf("public struct Box", StringComparison.Ordinal)))
        {
            Assert.Equal("namespace", classifications.GetValueOrDefault(token.Start));
            Assert.Equal(Json(expectedNamespace), Json(model.Definition(caller.Path, token.Start)));
        }
    }

    [Fact]
    public void RootedSimpleTypeDoesNotNavigateToSameNameAliasOrGenericParameter()
    {
        var library = new SourceFile(Path.GetFullPath("rooted-simple-library.gf"), "public struct Token {public int Value;}");
        var caller = new SourceFile(Path.GetFullPath("rooted-simple-main.gf"), """
            namespace Client {
                alias Token=long;
                struct Holder {public global::Token Value;}
                struct Generic<Token> {public global::Token Value;}
            }
            """);
        SourceFile[] files = [library, caller];
        var snapshot = new AnalysisSnapshot(files, new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, files);
        object? expected = model.Definition(library.Path, library.Text.IndexOf("Token", StringComparison.Ordinal));
        Assert.NotNull(expected);
        var classifications = Classifications(model, caller);
        int cursor = 0, count = 0;
        while ((cursor = caller.Text.IndexOf("global::Token", cursor, StringComparison.Ordinal)) >= 0)
        {
            int use = cursor + "global::".Length;
            Assert.Equal("struct", classifications.GetValueOrDefault(use));
            Assert.Equal(Json(expected), Json(model.Definition(caller.Path, use)));
            var hover = JsonSerializer.SerializeToElement(model.Hover(caller.Path, use), Protocol.JsonOptions);
            Assert.Contains("struct Token", hover.GetProperty("contents").GetProperty("value").GetString());
            cursor = use + "Token".Length;
            count++;
        }
        Assert.Equal(2, count);
    }

    private static string Json(object? value) => JsonSerializer.Serialize(value, Protocol.JsonOptions);

    private static Dictionary<int, string> Classifications(EditorModel model, SourceFile source)
    {
        int[] data = JsonSerializer.SerializeToElement(model.SemanticTokens(source.Path), Protocol.JsonOptions)
            .GetProperty("data").EnumerateArray().Select(value => value.GetInt32()).ToArray();
        var starts = new List<int> { 0 };
        for (int at = 0; at < source.Text.Length; at++) if (source.Text[at] == '\n') starts.Add(at + 1);
        var result = new Dictionary<int, string>();
        int line = 0, column = 0;
        for (int at = 0; at < data.Length; at += 5)
        {
            line += data[at];
            column = data[at] == 0 ? column + data[at + 1] : data[at + 1];
            result[starts[line] + column] = EditorModel.TokenTypes[data[at + 3]];
        }
        return result;
    }
}
