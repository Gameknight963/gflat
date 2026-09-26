using System.Text.Json;
using gflat.LanguageServer;

namespace gflat.LanguageServer.Tests;

public class EditorTests
{
    [Fact]
    public void CompletionDetailsUseReadableGenericAndNestedTypes()
    {
        var (model, path, offset, _) = Analyze("class Outer { public class Inner {} } struct Box<T> { public T value; } Box<int> Read(Outer::Inner* value) => new Box<int>(); void Use() { Re| }");
        var item = Json(model.Completion(path, offset)).GetProperty("items").EnumerateArray().Single(i => i.GetProperty("label").GetString() == "Read");
        string detail = item.GetProperty("detail").GetString()!;
        Assert.Contains("Box<int>", detail);
        Assert.Contains("Outer::Inner*", detail);
        Assert.DoesNotContain("$", detail);
        Assert.DoesNotContain("Outer.Inner", detail);
    }

    [Fact]
    public void SignatureHelpUsesReadableTypesInBothSignatureAndParameters()
    {
        var (model, path, offset, _) = Analyze("class Outer { public class Inner {} } struct Box<T> { public T value; } Box<int> Read(Outer::Inner* value) => new Box<int>(); void Use() { Read(|new* Outer::Inner()); }");
        var signature = Json(model.SignatureHelp(path, offset)).GetProperty("signatures")[0];
        Assert.Contains("Box<int>", signature.GetProperty("label").GetString());
        Assert.Equal("Outer::Inner* value", signature.GetProperty("parameters")[0].GetProperty("label").GetString());
    }

    [Theory]
    [InlineData("class Child : | {}", true)]
    [InlineData("class Child : |", true)]
    [InlineData("struct Child : | {}", false)]
    [InlineData("class Child : Base, | {}", false)]
    [InlineData("class Child<T> : | {}", true)]
    public void InheritanceCompletionOnlyOffersValidKinds(string declaration, bool allowsBase)
    {
        var (model, path, offset, _) = Analyze("class Base {} interface I {} struct Value {} int Helper() => 1; " + declaration);
        var labels = Labels(model.Completion(path, offset));
        Assert.Contains("I", labels);
        Assert.Equal(allowsBase, labels.Contains("Base"));
        foreach (string invalid in new[] { "Child", "Value", "Helper", "int", "void", "class", "return", "readonly" })
            Assert.DoesNotContain(invalid, labels);
    }

    [Fact]
    public void QualifiedInheritanceFiltersNamespaceMembers()
    {
        var (model, path, offset, _) = Analyze("namespace Lib { public interface I {} public struct Value {} public int Helper() => 1; } struct Child : Lib::| {}");
        Assert.Equal(new[] { "I" }, Labels(model.Completion(path, offset)));
    }

    [Fact]
    public void InheritanceDoesNotSuggestAlreadyListedInterface()
    {
        var (model, path, offset, _) = Analyze("interface I {} interface J {} struct Child : I, | {}");
        var labels = Labels(model.Completion(path, offset));
        Assert.Contains("J", labels);
        Assert.DoesNotContain("I", labels);
    }

    [Fact]
    public void UsingCompletionOnlyOffersNamespaces()
    {
        var (model, path, offset, _) = Analyze("namespace Lib {} struct Value {} using |;");
        var items = Json(model.Completion(path, offset)).GetProperty("items").EnumerateArray().ToArray();
        Assert.Contains(items, item => item.GetProperty("label").GetString() == "Lib");
        Assert.All(items, item => Assert.Equal(9, item.GetProperty("kind").GetInt32()));
    }

    [Fact]
    public void CompletionReplacesTheWholeIdentifierWhenEditingItsMiddle()
    {
        var (model, path, offset, source) = Analyze("int helper() => 1; int main() => he|lper();");
        var item = Json(model.Completion(path, offset)).GetProperty("items").EnumerateArray().Single(i => i.GetProperty("label").GetString() == "helper");
        var range = item.GetProperty("textEdit").GetProperty("range");
        int start = source.LastIndexOf("helper", StringComparison.Ordinal);
        Assert.Equal(start, range.GetProperty("start").GetProperty("character").GetInt32());
        Assert.Equal(start + 6, range.GetProperty("end").GetProperty("character").GetInt32());
    }

    [Fact]
    public void UnclosedFunctionKeepsLocalsVisibleAtEndOfFile()
    {
        var (model, path, offset, _) = Analyze("int main(int parameter) { int local = 0; \n    |");
        var labels = Labels(model.Completion(path, offset));
        Assert.Contains("local", labels);
        Assert.Contains("parameter", labels);
        Assert.Contains("return", labels);
    }

    [Fact]
    public void SemanticRangeTokensUseAbsoluteUtf16Positions()
    {
        var (model, path, _, _) = Analyze("// 😀\r\nint first() => 1;\r\nint second() => first();|");
        var data = Json(model.SemanticTokens(path, new(new(2, 0), new(2, 24)))).GetProperty("data").EnumerateArray().Select(n => n.GetInt32()).ToArray();
        Assert.Equal(2, data[0]);
        Assert.Equal(4, data[1]);
        Assert.Equal(6, data[2]);
        for (int i = 5; i < data.Length; i += 5) Assert.Equal(0, data[i]);
    }

    [Fact]
    public void DefinitionUsesTheSelectedOverload()
    {
        var (model, path, offset, source) = Analyze("int Read(int x) => x; int Read(bool x) => 2; int main() => Re|ad(true);");
        var location = Json(model.Definition(path, offset));
        Assert.Equal(source.IndexOf("Read(bool", StringComparison.Ordinal), location.GetProperty("range").GetProperty("start").GetProperty("character").GetInt32());
    }

    [Theory]
    [InlineData("Make().|")]
    [InlineData("new Value().|")]
    public void IncompleteAccessRecoversUnambiguousReturnTypes(string expression)
    {
        var (model, path, offset, _) = Analyze("struct Value { public int Field; } Value Make() => new Value(); int main() { " + expression + "; return 0; }");
        Assert.Contains("Field", Labels(model.Completion(path, offset)));
    }

    [Fact]
    public void QualifiedNamespaceDeclarationsExposeEachSegment()
    {
        var (model, path, offset, _) = Analyze("namespace Lib::Tools { public int Run() => 1; } int main() { Lib::|; return 0; }");
        Assert.Contains("Tools", Labels(model.Completion(path, offset)));
        Assert.DoesNotContain("Run", Labels(model.Completion(path, offset)));
    }

    [Theory]
    [InlineData("int main() { // co|mment\n return 0; }")]
    [InlineData("int main() { /* co|mment */ return 0; }")]
    [InlineData("int main() { c\"he|llo\"; return 0; }")]
    public void CompletionDoesNotOfferCodeInsideCommentsOrStrings(string text)
    {
        var (model, path, offset, _) = Analyze(text);
        Assert.Empty(Labels(model.Completion(path, offset)));
    }

    [Fact]
    public void PropertyHoverAndDefinitionUseTheSourceProperty()
    {
        var (model, path, offset, source) = Analyze("struct S { public int Count => 2; } int main() { S s = new S(); return s.Co|unt; }");
        Assert.Contains("int S.Count", Json(model.Hover(path, offset)).ToString());
        var location = Json(model.Definition(path, offset));
        Assert.Equal(source.IndexOf("Count", StringComparison.Ordinal), location.GetProperty("range").GetProperty("start").GetProperty("character").GetInt32());
    }

    [Fact]
    public void InheritedPublicMembersAreIncluded()
    {
        var (model, path, offset, _) = Analyze("class Base { public int Field; private int secret; } class Derived : Base {} int main() { Derived* d = new* Derived(); d.|; return 0; }");
        var labels = Labels(model.Completion(path, offset));
        Assert.Contains("Field", labels);
        Assert.DoesNotContain("secret", labels);
    }

    private static (EditorModel Model, string Path, int Offset, string Source) Analyze(string marked, string? library = null)
    {
        int offset = marked.IndexOf('|');
        string text = marked.Remove(offset, 1);
        string path = Path.GetFullPath("editor.gf");
        var files = new List<SourceFile> { new(path, text) };
        if (library != null) files.Add(new(Path.GetFullPath("library.gf"), library));
        return (new EditorModel(new AnalysisSnapshot(files), files), path, offset, text);
    }
    private static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value, Protocol.JsonOptions);
    private static string[] Labels(object completion) => Json(completion).GetProperty("items").EnumerateArray().Select(i => i.GetProperty("label").GetString()!).ToArray();

    [Fact]
    public void OrdinaryCompletionIncludesScopeAndExcludesOtherLocals()
    {
        var (model, path, offset, _) = Analyze("struct Item {} int helper(int arg) => arg; int main(int parameter) { int local = 0; { int hidden = 1; } | return 0; int later = 2; }");
        var labels = Labels(model.Completion(path, offset));
        Assert.Contains("local", labels);
        Assert.Contains("parameter", labels);
        Assert.Contains("helper", labels);
        Assert.Contains("Item", labels);
        Assert.Contains("return", labels);
        Assert.DoesNotContain("hidden", labels);
        Assert.DoesNotContain("later", labels);
        Assert.DoesNotContain("arg", labels);
    }

    [Theory]
    [InlineData("v.|")]
    [InlineData("v.Va|")]
    public void IncompleteMemberAccessOnlyOffersAccessibleInstanceMembers(string expression)
    {
        var (model, path, offset, _) = Analyze("struct Value { public int ValueField; private int secret; public static int Static() => 0; public int Read() => ValueField; } int main() { Value v = new Value(); " + expression + "; return 0; }");
        var labels = Labels(model.Completion(path, offset));
        Assert.Contains("ValueField", labels);
        Assert.Contains("Read", labels);
        Assert.DoesNotContain("secret", labels);
        Assert.DoesNotContain("Static", labels);
        Assert.DoesNotContain("main", labels);
        Assert.DoesNotContain("return", labels);
    }

    [Fact]
    public void NamespaceCompletionUsesImportsAndUnsavedSiblings()
    {
        var (model, path, offset, _) = Analyze("using Lib; int main() { Tools::|; return 0; }", "namespace Lib { namespace Tools { public int Run() => 1; public struct Item {} } } namespace Other { public int Hidden() => 0; }");
        var labels = Labels(model.Completion(path, offset));
        Assert.Contains("Run", labels);
        Assert.Contains("Item", labels);
        Assert.DoesNotContain("Hidden", labels);
    }

    [Fact]
    public void DefinitionRespectsLocalShadowing()
    {
        var (model, path, offset, source) = Analyze("int f(int value) { { int value = 2; return val|ue; } }");
        var location = Json(model.Definition(path, offset));
        Assert.Equal(source.IndexOf("value ="), location.GetProperty("range").GetProperty("start").GetProperty("character").GetInt32());
        Assert.Contains("int value", Json(model.Hover(path, offset)).ToString());
    }

    [Fact]
    public void CrossFileDefinitionTargetsDeclarationName()
    {
        var (model, path, offset, _) = Analyze("using Lib; int main() => R|un(2);", "namespace Lib; public int Run(int value) => value;");
        var location = Json(model.Definition(path, offset));
        Assert.EndsWith("library.gf", location.GetProperty("uri").GetString());
        Assert.Equal(26, location.GetProperty("range").GetProperty("start").GetProperty("character").GetInt32());
    }

    [Fact]
    public void SignatureHelpCountsOnlyDirectArguments()
    {
        var (model, path, offset, _) = Analyze("int add(int left, int right) => left + right; int main() => add(add(1, 2), |3);");
        var signature = Json(model.SignatureHelp(path, offset));
        Assert.Equal(1, signature.GetProperty("activeParameter").GetInt32());
        Assert.Contains("int left, int right", signature.GetProperty("signatures")[0].GetProperty("label").GetString());
    }

    [Fact]
    public void SemanticTokensDistinguishTypeMethodParameterAndLocal()
    {
        var (model, path, _, source) = Analyze("struct String { public static String From(int count) { String result = new String(); return result; } } |");
        var data = Json(model.SemanticTokens(path)).GetProperty("data").EnumerateArray().Select(x => x.GetInt32()).ToArray();
        var classified = new List<(string Text, string Kind)>();
        int line = 0, column = 0;
        for (int i = 0; i < data.Length; i += 5)
        {
            line += data[i]; column = data[i] == 0 ? column + data[i + 1] : data[i + 1];
            Assert.Equal(0, line);
            classified.Add((source.Substring(column, data[i + 2]), EditorModel.TokenTypes[data[i + 3]]));
        }
        Assert.Equal(4, classified.Count(x => x == ("String", "struct")));
        Assert.Contains(("From", "method"), classified);
        Assert.Contains(("count", "parameter"), classified);
        Assert.Equal(2, classified.Count(x => x == ("result", "variable")));
    }

    [Fact]
    public void CapacityInAnEarlyReturnConditionIsAField()
    {
        var (model, path, _, source) = Analyze("struct String { nuint capacity; public void Reserve(nuint requested) { if (requested <= capacity) return; } } |");
        var data = Json(model.SemanticTokens(path)).GetProperty("data").EnumerateArray().Select(x => x.GetInt32()).ToArray();
        int column = 0;
        var fields = new List<int>();
        for (int i = 0; i < data.Length; i += 5)
        {
            column += data[i + 1];
            if (source.Substring(column, data[i + 2]) == "capacity")
            {
                Assert.Equal("property", EditorModel.TokenTypes[data[i + 3]]);
                fields.Add(column);
            }
        }
        Assert.Equal(2, fields.Count);
    }

    [Fact]
    public void StaticContextDoesNotOfferInstanceFields()
    {
        var (model, path, offset, _) = Analyze("struct S { public int instance; public static int shared; public static int F() { | return 0; } }");
        var labels = Labels(model.Completion(path, offset));
        Assert.Contains("shared", labels);
        Assert.DoesNotContain("instance", labels);
    }
}
