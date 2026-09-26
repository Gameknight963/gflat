using System.Text.Json;
using System.Text.RegularExpressions;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;
using TextMateSharp.Themes;
using TextMateSharp.Internal.Types;

namespace gflat.LanguageServer.Tests;

public class EditorGrammarTests
{
    [Fact]
    public void StringIsNotHighlightedAsABuiltInType()
    {
        const string line = "int string = 0;";
        var token = Assert.Single(Grammar().TokenizeLine(line).Tokens,
            t => t.StartIndex <= 4 && t.EndIndex > 4);
        Assert.DoesNotContain(token.Scopes, s => s.StartsWith("storage.type", StringComparison.Ordinal));
    }

    [Fact]
    public void ReturnAfterAConditionDoesNotTurnItsOperandIntoAType()
    {
        const string line = "if (requested <= capacity) return;";
        var token = Assert.Single(Grammar().TokenizeLine(line).Tokens, t => t.StartIndex <= line.IndexOf("capacity", StringComparison.Ordinal) && t.EndIndex > line.IndexOf("capacity", StringComparison.Ordinal));
        Assert.DoesNotContain(token.Scopes, s => s.StartsWith("entity.name.type", StringComparison.Ordinal));
    }

    [Fact]
    public void InterpolationExpressionsDoNotInheritStringColor()
    {
        const string line = "s$\"hello {value + 1}\"";
        var token = Assert.Single(Grammar().TokenizeLine(line).Tokens, t => t.StartIndex <= line.IndexOf("value", StringComparison.Ordinal) && t.EndIndex > line.IndexOf("value", StringComparison.Ordinal));
        Assert.DoesNotContain(token.Scopes, s => s.StartsWith("string.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("public void cool(", "cool", "entity.name.function")]
    [InlineData("public void cool() throws", "throws", "storage.modifier")]
    [InlineData("int value = alignof(Widget);", "alignof", "keyword.control")]
    [InlineData("int value = alignof(Widget);", "Widget", "entity.name.type")]
    public void TypingAndLayoutQueriesHaveImmediateSyntaxClassifications(string line, string text, string scope)
        => AssertScope(Grammar().TokenizeLine(line), line.IndexOf(text, StringComparison.Ordinal), scope);

    [Fact]
    public void ThemeUsesTheSameMethodAndKeywordClassificationsAsSemanticTokens()
    {
        var theme = System.Xml.Linq.XDocument.Load(Asset("gflat.tmTheme"));
        string Mapping(string scope) => theme.Descendants("dict").Single(d => d.Elements("key")
            .Any(k => k.Value == "scope" && ((System.Xml.Linq.XElement)k.NextNode!).Value.Split(',').Select(s => s.Trim()).Contains(scope)))
            .Descendants("key").Single(k => k.Value == "vsclassificationtype").ElementsAfterSelf("string").First().Value;
        Assert.Equal("method name", Mapping("entity.name.function.gflat"));
        Assert.Equal("keyword", Mapping("storage.modifier.gflat"));
        Assert.Equal("string - escape character", Mapping("constant.character.escape.gflat"));
        Assert.Equal("operator", Mapping("punctuation.section.interpolation.begin.gflat"));
        Assert.DoesNotContain(theme.Descendants("key"), k => k.Value is "foreground" or "background");
    }

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
    [InlineData("public static String From(readonly(char)* text) throws", "String", "entity.name.type")]
    [InlineData("public static String", "String", "entity.name.type")]
    [InlineData("struct String : IInterpolatedString, IStringConvertible", "IInterpolatedString", "entity.name.type")]
    [InlineData("struct String : IInterpolatedString, IStringConvertible", "IStringConvertible", "entity.name.type")]
    [InlineData("sizeof(String)", "String", "entity.name.type")]
    [InlineData("public String Make<T>()", "String", "entity.name.type")]
    [InlineData("public readonly(List<readonly(String)*>)* Get()", "String", "entity.name.type")]
    [InlineData("public readonly String Clone() throws", "String", "entity.name.type")]
    [InlineData("String message = s\"hello\";", "String", "entity.name.type")]
    [InlineData("private String name;", "String", "entity.name.type")]
    [InlineData("void Use(readonly(String)* text, String* output)", "String", "entity.name.type")]
    [InlineData("public List<String> Make()", "String", "entity.name.type")]
    [InlineData("public std::String Make()", "String", "entity.name.type")]
    [InlineData("public readonly(String)* Data {", "String", "entity.name.type")]
    [InlineData("return new String();", "String", "entity.name.type")]
    [InlineData("public String Name { readonly get => name; }", "String", "entity.name.type")]
    [InlineData("public static String From(readonly(char)* text) throws", "From", "entity.name.function")]
    [InlineData("public static int Count()", "int", "storage.type")]
    [InlineData("value.Set(1);", "Set", "entity.name.function")]
    public void ColorsLanguageConstructs(string line, string text, string scope)
    {
        var result = Grammar().TokenizeLine(line, null, TimeSpan.FromSeconds(2));
        AssertScope(result, line.IndexOf(text, StringComparison.Ordinal), scope);
    }

    [Fact]
    public void StandardStringDeclarationsAreHighlightedInWholeFileContext()
    {
        var grammar = Grammar();
        ITokenizeLineResult? previous = null;
        int checkedReferences = 0;
        foreach (string line in File.ReadLines(Path.Combine(AppContext.BaseDirectory, "EditorFixtures", "String.gf")))
        {
            var result = grammar.TokenizeLine(line, previous?.RuleStack, TimeSpan.FromSeconds(2));
            previous = result;
            // Follow the actual library declarations, including new overloads, instead
            // of maintaining a separate hand-copied miniature String implementation.
            foreach (Match match in Regex.Matches(line, @"^\s*(?:(?:public|private|protected|internal|static|readonly)\s+)*(String)(?=\s+[A-Za-z_][A-Za-z0-9_]*\b)"))
            {
                AssertScope(result, match.Groups[1].Index, "entity.name.type");
                checkedReferences++;
            }
        }
        Assert.True(checkedReferences >= 4, "The fixture must exercise real custom-type declarations.");
    }

    [Theory]
    [InlineData("value = left + right;", "left")]
    [InlineData("Consume(left, right);", "right")]
    [InlineData("if (left < right && other > value) { }", "left")]
    [InlineData("public static String From()", "From")]
    [InlineData("// public static String From()", "String")]
    [InlineData("c\"public static String From()\"", "String")]
    public void TypeRulesDoNotReclassifyExpressionsCommentsOrStrings(string line, string text)
    {
        var result = Grammar().TokenizeLine(line, null, TimeSpan.FromSeconds(2));
        int position = line.IndexOf(text, StringComparison.Ordinal);
        var token = Assert.Single(result.Tokens, t => t.StartIndex <= position && t.EndIndex > position);
        Assert.DoesNotContain(token.Scopes, scope => scope.StartsWith("entity.name.type."));
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
