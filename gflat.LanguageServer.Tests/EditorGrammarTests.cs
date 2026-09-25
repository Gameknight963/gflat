using System.Text.Json;
using System.Text.RegularExpressions;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;
using TextMateSharp.Themes;
using TextMateSharp.Internal.Types;

namespace gflat.LanguageServer.Tests;

public class EditorGrammarTests
{
    private static string Asset(string name) => Path.Combine(
        Environment.GetEnvironmentVariable("GFLAT_EDITOR_ASSETS") ??
        Path.Combine(AppContext.BaseDirectory, "EditorAssets"), name);

    private static IGrammar Grammar() => new Registry(new EmptyThemeOptions()).LoadGrammarFromPathSync(
        Asset("gflat.tmLanguage.json"), 0, new Dictionary<string, int>());

    private sealed class EmptyThemeOptions : IRegistryOptions, IRawTheme
    {
        public IRawTheme GetDefaultTheme() => this;
        public IRawTheme GetTheme(string scopeName) => this;
        public IRawGrammar GetGrammar(string scopeName) => null!;
        public ICollection<string> GetInjections(string scopeName) => [];
        public string GetName() => "test";
        public string GetInclude() => "";
        public ICollection<IRawThemeSetting> GetSettings() => [];
        public ICollection<IRawThemeSetting> GetTokenColors() => [];
        public ICollection<KeyValuePair<string, object>> GetGuiColors() => [];
    }

    [Theory]
    [InlineData("public class Player {", "public", "storage.modifier")]
    [InlineData("public class Player {", "Player", "entity.name.type")]
    [InlineData("readonly(int)^ value;", "int", "storage.type")]
    [InlineData("replace int Allocate() {", "replace", "storage.modifier")]
    [InlineData("return 0xFFul;", "0xFFul", "constant.numeric")]
    [InlineData("float n = 1_000.25f;", "1_000.25f", "constant.numeric")]
    [InlineData("my_2string\"hello\";", "my_2string", "entity.name.function.literal")]
    [InlineData("my_2string\"hello\";", "hello", "string.quoted.double")]
    [InlineData("\"a\\\" // b\"; return;", "// b", "string.quoted.double")]
    [InlineData("'\\n'", "\\n", "constant.character.escape")]
    [InlineData("// return \"text\"", "return", "comment.line")]
    [InlineData("get => value;", "get", "keyword.other.accessor")]
    [InlineData("namespace std::Collections;", "namespace", "keyword.control")]
    [InlineData("namespace std::Collections;", "::", "keyword.operator")]
    [InlineData("public int this[int i] { readonly get => data[i]; }", "this", "constant.language")]
    [InlineData("public int this[int i] { readonly get => data[i]; }", "get", "keyword.other.accessor")]
    [InlineData("public static String operator+() {", "String", "entity.name.type")]
    [InlineData("public static String operator s\"\"(readonly(char)* data, nuint length) {", "String", "entity.name.type")]
    [InlineData("String operator s\"\"(", "s\"\"", "entity.name.function.literal")]
    [InlineData("public static String operator+(", "operator", "keyword.control")]
    [InlineData("public static readonly(std::String)* operator+(", "String", "entity.name.type")]
    [InlineData("public static List<Pair<int, String>> operator+(", "String", "entity.name.type")]
    [InlineData("public static int operator+(", "int", "storage.type")]
    [InlineData("// String operator+(", "String", "comment.line")]
    [InlineData("\"String operator+\"", "String", "string.quoted.double")]
    [InlineData("s$\"hello {42}\"", "s", "entity.name.function.literal")]
    [InlineData("s$\"hello {42}\"", "$", "keyword.operator.interpolation")]
    [InlineData("s$\"hello {42}\"", "42", "constant.numeric")]
    [InlineData("s$\"hello {42}\"", "hello", "string.quoted.double")]
    [InlineData("s$\"{{literal}} {s$\"{true}\"}\"", "true", "constant.language")]
    [InlineData("s$\"{Read(c\"}\")}\"; return 1;", "return", "keyword.control")]
    [InlineData("value.Set(1);", "Set", "entity.name.function")]
    public void ColorsLanguageConstructs(string line, string text, string scope)
    {
        var result = Grammar().TokenizeLine(line, null, TimeSpan.FromSeconds(2));
        AssertScope(result, line.IndexOf(text, StringComparison.Ordinal), scope);
    }

    [Fact]
    public void MultilineCommentsResumeCodeAfterClosingDelimiter()
    {
        var grammar = Grammar();
        var first = grammar.TokenizeLine("/* comment", null, TimeSpan.FromSeconds(2));
        var second = grammar.TokenizeLine("return */ int x;", first.RuleStack, TimeSpan.FromSeconds(2));
        AssertScope(second, 0, "comment.block");
        AssertScope(second, 10, "storage.type");
    }

    [Theory]
    [InlineData("\"unfinished")]
    [InlineData("'unfinished")]
    [InlineData("\"unfinished\\")]
    public void UnfinishedLiteralDoesNotColorNextLine(string line)
    {
        var grammar = Grammar();
        var first = grammar.TokenizeLine(line, null, TimeSpan.FromSeconds(2));
        var next = grammar.TokenizeLine("return 1;", first.RuleStack, TimeSpan.FromSeconds(2));
        AssertScope(next, 0, "keyword.control");
    }

    [Theory]
    [InlineData("integer")]
    [InlineData("returnValue")]
    [InlineData("get = 1;")]
    public void IdentifiersDoNotBecomeKeywords(string line)
    {
        var result = Grammar().TokenizeLine(line, null, TimeSpan.FromSeconds(2));
        Assert.DoesNotContain(result.Tokens.First().Scopes,
            scope => scope.StartsWith("keyword.") || scope.StartsWith("storage."));
    }

    [Theory]
    [InlineData("if (ready) {", true)]
    [InlineData("if (name == \"x\") { // block", true)]
    [InlineData("// {", false)]
    [InlineData("/* {", false)]
    [InlineData("/* comment { */", false)]
    [InlineData("if (ready) /* comment */ {", true)]
    [InlineData("string text = \"{\";", false)]
    [InlineData("Run(); // {", false)]
    [InlineData("class Player {}", false)]
    public void IndentationRecognizesCodeBraces(string line, bool expected)
    {
        using var config = JsonDocument.Parse(File.ReadAllText(Asset("gflat-language-configuration.json")));
        var pattern = config.RootElement.GetProperty("indentationRules").GetProperty("increaseIndentPattern").GetString()!;
        Assert.Equal(expected, Regex.IsMatch(line, pattern));
    }

    [Fact]
    public void PairsExcludeCommentsAndStringsAndDoNotCloseComparisonOperators()
    {
        using var config = JsonDocument.Parse(File.ReadAllText(Asset("gflat-language-configuration.json")));
        var pairs = config.RootElement.GetProperty("autoClosingPairs").EnumerateArray().ToArray();
        Assert.Equal(new[] { "{", "[", "(", "\"", "'" }, pairs.Select(p => p.GetProperty("open").GetString()));
        foreach (var pair in pairs)
            Assert.Equal(new[] { "string", "comment" }, pair.GetProperty("notIn").EnumerateArray().Select(v => v.GetString()));
    }

    private static void AssertScope(ITokenizeLineResult result, int position, string scope)
    {
        var token = Assert.Single(result.Tokens, t => t.StartIndex <= position && t.EndIndex > position);
        Assert.Contains(token.Scopes, value => value.StartsWith(scope + ".", StringComparison.Ordinal));
    }
}
