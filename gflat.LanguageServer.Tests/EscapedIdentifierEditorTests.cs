using System.Text.Json;

namespace gflat.LanguageServer.Tests;

public sealed class EscapedIdentifierEditorTests
{
    [Theory]
    [InlineData("class @class {}", "@class*", "class")]
    [InlineData("alias @alias = int;", "@alias", "alias")]
    public void NavigationSelectsTheEscapedNameRatherThanItsDeclarationKeyword(string declaration, string type, string name)
    {
        var source = new SourceFile(Path.GetFullPath("escaped-navigation.gf"), declaration + "\n" + type + " value;");
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
        var model = new EditorModel(snapshot, [source]);
        int use = source.Text.LastIndexOf("@" + name, StringComparison.Ordinal) + 1;
        var result = JsonSerializer.SerializeToElement(model.Definition(source.Path, use), Protocol.JsonOptions);
        Assert.Equal(0, result.GetProperty("range").GetProperty("start").GetProperty("line").GetInt32());
        Assert.Equal(declaration.IndexOf('@') + 1, result.GetProperty("range").GetProperty("start").GetProperty("character").GetInt32());
    }

    [Theory]
    [InlineData("class @class {}", "@cl", "class")]
    [InlineData("class @class {}", "cl", "class")]
    [InlineData("alias @alias = int;", "@ali", "alias")]
    [InlineData("alias @alias = int;", "ali", "alias")]
    [InlineData("#define VALUE 7\nint @VALUE = 42;", "@VA", "VALUE")]
    [InlineData("#define VALUE 7\nint @VALUE = 42;", "VA", "VALUE")]
    public void CompletionRetainsTheIdentifierEscape(string declaration, string prefix, string name)
    {
        string before = declaration + "\nvoid Use() { ";
        var source = new SourceFile(Path.GetFullPath("escaped-completion.gf"), before + prefix + "; }");
        var model = new EditorModel(new AnalysisSnapshot([source], new() { NoStdLib = true }), [source]);
        var response = JsonSerializer.SerializeToElement(model.Completion(source.Path, before.Length + prefix.Length), Protocol.JsonOptions);
        var item = Assert.Single(response.GetProperty("items").EnumerateArray(), candidate =>
            candidate.GetProperty("label").GetString() == name && candidate.GetProperty("kind").GetInt32() != 14);
        Assert.Equal("@" + name, item.GetProperty("textEdit").GetProperty("newText").GetString());
        Assert.Equal(prefix.StartsWith('@') ? "@" + name : name, item.GetProperty("filterText").GetString());
    }

    [Fact]
    public void InsertedMemberSeparatorStillCompletesAnEscapedField()
    {
        var source = new SourceFile(Path.GetFullPath("escaped-member.gf"),
            "struct Item { public int @class; } Item* Use(Item* value) { return value; }");
        var model = new EditorModel(new AnalysisSnapshot([source], new() { NoStdLib = true }), [source]);
        int separator = source.Text.LastIndexOf("value;", StringComparison.Ordinal) + "value".Length;
        string edited = source.Text.Insert(separator, ".");
        Assert.True(model.TryReuseMemberCompletion(source.Path, edited, separator + 1, out var response));
        var completion = JsonSerializer.SerializeToElement(response, Protocol.JsonOptions);
        var item = Assert.Single(completion.GetProperty("items").EnumerateArray(), candidate => candidate.GetProperty("label").GetString() == "class");
        Assert.Equal("@class", item.GetProperty("textEdit").GetProperty("newText").GetString());
    }

    [Fact]
    public void ReflectionMemberCompletionKeepsAnExplicitEscape()
    {
        const string text = "void Use() { const reflection info = reflect(int); info.@Na; }";
        var source = new SourceFile(Path.GetFullPath("escaped-reflection.gf"), text);
        var model = new EditorModel(new AnalysisSnapshot([source], new() { NoStdLib = true }), [source]);
        var response = JsonSerializer.SerializeToElement(model.Completion(source.Path, text.IndexOf("@Na", StringComparison.Ordinal) + 3), Protocol.JsonOptions);
        var item = Assert.Single(response.GetProperty("items").EnumerateArray(), candidate => candidate.GetProperty("label").GetString() == "Name");
        Assert.Equal("@Name", item.GetProperty("textEdit").GetProperty("newText").GetString());
    }
}
