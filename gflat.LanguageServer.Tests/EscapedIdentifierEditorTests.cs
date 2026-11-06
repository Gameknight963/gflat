using System.Text.Json;

namespace gflat.LanguageServer.Tests;

public sealed class EscapedIdentifierEditorTests
{
    [Theory]
    [InlineData("namespace Names { public class @int {} } void Use(Names::@int* value) {}", "Names::@int* value")]
    [InlineData("namespace Names { public class @readonly<T> {} } void Use(Names::@readonly<int>* value) {}", "Names::@readonly<int>* value")]
    [InlineData("using Names;\n\nnamespace Names { public class @int {} } void Use(Names::@int* value) {}", "Names::@int* value")]
    [InlineData("using Names;\n\nnamespace Names { public class @readonly<T> {} } void Use(Names::@readonly<int>* value) {}", "@readonly<int>* value")]
    public void QualifiedKeywordTypeNamesAreEscapedInVariableHovers(string text, string expected)
    {
        var source = new SourceFile(Path.GetFullPath("qualified-escaped-hover.gf"), text);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Severity == gflat.diagnostics.DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, [source]);
        var hover = JsonSerializer.SerializeToElement(model.Hover(source.Path, text.LastIndexOf("value", StringComparison.Ordinal)), Protocol.JsonOptions);
        Assert.Contains(expected, hover.GetProperty("contents").GetProperty("value").GetString());
        var rich = JsonSerializer.SerializeToElement(model.Hover(source.Path, text.LastIndexOf("value", StringComparison.Ordinal),
            new VisualStudioHover(JsonSerializer.SerializeToElement(new { }))), Protocol.JsonOptions);
        var runs = rich.GetProperty("_vs_rawContent").GetProperty("Elements")[0].GetProperty("Elements")[0].GetProperty("Runs").EnumerateArray().ToArray();
        string escapedName = expected.Contains("@readonly", StringComparison.Ordinal) ? "@readonly" : "@int";
        Assert.Contains(runs, run => run.GetProperty("Text").GetString() == escapedName && run.GetProperty("ClassificationTypeName").GetString() == "class name");
        if (expected.Contains("<int>", StringComparison.Ordinal))
            Assert.Contains(runs, run => run.GetProperty("Text").GetString() == "int" && run.GetProperty("ClassificationTypeName").GetString() == "keyword");
    }

    [Theory]
    [InlineData("class @class {} @class* value;", "class", "class")]
    [InlineData("int Read(){int @while=0; return @while;}", "while", "variable")]
    public void SemanticClassificationIncludesTheIdentifierEscape(string text, string name, string kind)
    {
        var source = new SourceFile(Path.GetFullPath("escaped-colors.gf"), text);
        var model = new EditorModel(new AnalysisSnapshot([source], new() { NoStdLib = true }), [source]);
        var data = JsonSerializer.SerializeToElement(model.SemanticTokens(source.Path), Protocol.JsonOptions)
            .GetProperty("data").EnumerateArray().Select(item => item.GetInt32()).ToArray();
        var tokens = new Dictionary<int, (int Length, string Kind, int Modifiers)>();
        int column = 0;
        for (int i = 0; i < data.Length; i += 5)
        {
            Assert.Equal(0, data[i]);
            column += data[i + 1];
            tokens[column] = (data[i + 2], EditorModel.TokenTypes[data[i + 3]], data[i + 4]);
        }
        int first = text.IndexOf('@');
        foreach (int at in Enumerable.Range(0, text.Length).Where(index => text[index] == '@'))
        {
            Assert.True(tokens.TryGetValue(at, out var token), $"Missing escaped identifier at {at}");
            Assert.Equal(name.Length + 1, token.Length);
            Assert.Equal(kind, token.Kind);
        }
        Assert.NotEqual(0, tokens[first].Modifiers & 1);
        var range = new TextRange(new(0, first), new(0, first + 1));
        var ranged = JsonSerializer.SerializeToElement(model.SemanticTokens(source.Path, range), Protocol.JsonOptions).GetProperty("data");
        Assert.Equal(5, ranged.GetArrayLength());
        Assert.Equal(first, ranged[1].GetInt32());
        Assert.Equal(name.Length + 1, ranged[2].GetInt32());
    }

    [Theory]
    [InlineData("void Call(int @class) {} void Use() { Call(1); }", "int @class")]
    [InlineData("#define VALUE 7\nclass @VALUE {} void Call(@VALUE* value) {} void Use(@VALUE* value) { Call(value); }", "@VALUE* value")]
    [InlineData("namespace Names { public class @readonly<T> {} } void Call(Names::@readonly<int>* value) {} void Use(Names::@readonly<int>* value) { Call(value); }", "Names::@readonly<int>* value")]
    public void SignatureHelpParameterLabelsMatchEscapedNamesInTheSignature(string text, string expected)
    {
        var source = new SourceFile(Path.GetFullPath("escaped-signature-help.gf"), text);
        var model = new EditorModel(new AnalysisSnapshot([source], new() { NoStdLib = true }), [source]);
        var help = JsonSerializer.SerializeToElement(model.SignatureHelp(source.Path, text.LastIndexOf("Call(", StringComparison.Ordinal) + 5), Protocol.JsonOptions);
        var signature = help.GetProperty("signatures")[0];
        string parameter = signature.GetProperty("parameters")[0].GetProperty("label").GetString()!;
        Assert.Equal(expected, parameter);
        Assert.Contains(parameter, signature.GetProperty("label").GetString());
    }

    [Fact]
    public void SpecializedSignatureHelpRetainsAnEscapedParameterName()
    {
        const string text = "#define VALUE 7\nstruct Box<T> { public T Read(T @VALUE) => @VALUE; } void Use(Box<int>* box) { box.Read(1); }";
        var source = new SourceFile(Path.GetFullPath("specialized-escaped-parameter.gf"), text);
        var model = new EditorModel(new AnalysisSnapshot([source], new() { NoStdLib = true }), [source]);
        var help = JsonSerializer.SerializeToElement(model.SignatureHelp(source.Path, text.LastIndexOf("Read(", StringComparison.Ordinal) + 5), Protocol.JsonOptions);
        var signature = help.GetProperty("signatures")[0];
        Assert.Equal("int @VALUE", signature.GetProperty("parameters")[0].GetProperty("label").GetString());
        Assert.Contains("int @VALUE", signature.GetProperty("label").GetString());
    }

    [Theory]
    [InlineData("int @class;", "int @class")]
    [InlineData("void @while() {}", "void @while()")]
    [InlineData("void @while(int @class) {}", "void @while(int @class)")]
    [InlineData("alias @alias = int;", "alias @alias = int")]
    [InlineData("class @class {}", "class @class")]
    public void HoverPreservesEscapedDeclarationNames(string text, string expected)
    {
        var source = new SourceFile(Path.GetFullPath("escaped-hover.gf"), text);
        var model = new EditorModel(new AnalysisSnapshot([source], new() { NoStdLib = true }), [source]);
        var hover = JsonSerializer.SerializeToElement(model.Hover(source.Path, text.IndexOf('@') + 1), Protocol.JsonOptions);
        Assert.Contains(expected, hover.GetProperty("contents").GetProperty("value").GetString());
    }

    [Fact]
    public void HoverAtEscapedTypeUseRetainsItsSourceSpelling()
    {
        const string text = "class @class {}\n@class* value;";
        var source = new SourceFile(Path.GetFullPath("escaped-type-hover.gf"), text);
        var model = new EditorModel(new AnalysisSnapshot([source], new() { NoStdLib = true }), [source]);
        var hover = JsonSerializer.SerializeToElement(model.Hover(source.Path, text.LastIndexOf('@') + 1), Protocol.JsonOptions);
        Assert.Contains("class @class", hover.GetProperty("contents").GetProperty("value").GetString());
        var variable = JsonSerializer.SerializeToElement(model.Hover(source.Path, text.IndexOf("value", StringComparison.Ordinal)), Protocol.JsonOptions);
        Assert.Contains("@class* value", variable.GetProperty("contents").GetProperty("value").GetString());
    }

    [Theory]
    [InlineData("readonly(int)*", "readonly(int)*")]
    [InlineData("immutable(int)*", "readonly(int)*")]
    [InlineData("readonly(@class)*", "readonly(@class)*")]
    public void EscapedTypeRenderingPreservesOrdinaryTypeModifiers(string type, string expected)
    {
        string text = "class @class {}\n" + type + " value;";
        var source = new SourceFile(Path.GetFullPath("escaped-modifier-hover.gf"), text);
        var model = new EditorModel(new AnalysisSnapshot([source], new() { NoStdLib = true }), [source]);
        var hover = JsonSerializer.SerializeToElement(model.Hover(source.Path, text.IndexOf("value", StringComparison.Ordinal)), Protocol.JsonOptions);
        Assert.Contains(expected + " value", hover.GetProperty("contents").GetProperty("value").GetString());
    }

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
