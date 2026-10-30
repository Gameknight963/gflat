using System.Text.Json;
using gflat.LanguageServer;

namespace gflat.LanguageServer.Tests;

public class HoverTests
{
    [Fact]
    public void ForeignNothrowAppearsInTheSignature()
        => Assert.Contains("nothrow",Hover("extern int ab|s(int value) nothrow;").GetProperty("contents").GetProperty("value").GetString());

    private static readonly Guid Catalog = Guid.Parse("ae27a6b0-e345-4288-96df-5eaf394ee369");
    private static JsonElement Hover(string marked, bool rich = false, HoverOptions? options = null)
    {
        int offset = marked.IndexOf('|');
        string path = Path.GetFullPath("hover.gf");
        var source = new SourceFile(path, marked.Remove(offset, 1));
        var model = new EditorModel(new AnalysisSnapshot([source]), [source]);
        var icons = JsonSerializer.SerializeToElement(new Dictionary<string, object> { ["method.public"] = new { guid = Catalog, id = 1 }, ["struct.public"] = new { guid = Catalog, id = 2 } });
        return JsonSerializer.SerializeToElement(model.Hover(path, offset, rich ? new VisualStudioHover(icons) : null, options), Protocol.JsonOptions);
    }

    [Fact]
    public void CustomStringLiteralTypeUsesStructClassification()
    {
        var hover = Hover("using N; namespace N { public struct Text { public static Text operator s\"\"(readonly(char)* data, nuint length) => new Text(); } } void F() { Text text = s\"he|llo\"; }", true);
        var header = hover.GetProperty("_vs_rawContent").GetProperty("Elements")[0].GetProperty("Elements");
        var runs = header.EnumerateArray().Single(e => e.GetProperty("_vs_type").GetString() == "ClassifiedTextElement").GetProperty("Runs").EnumerateArray();
        Assert.Contains(runs, r => r.GetProperty("Text").GetString() == "Text" && r.GetProperty("ClassificationTypeName").GetString() == "struct name");
    }

    [Fact]
    public void HoverPreferencesHideExceptionsByDefaultAndCanHideAllTypeLayout()
    {
        const string method = "void F|ail() { throw new Exception(); }";
        Assert.DoesNotContain("exceptions:", Hover(method).GetProperty("contents").GetProperty("value").GetString());
        Assert.Contains("Known exceptions:", Hover(method, options: new(true)).GetProperty("contents").GetProperty("value").GetString());
        foreach (string source in new[] { "struct S|ize { int n; }", "void F(in|t n) {}", "void F(int|* n) {}" })
            Assert.DoesNotContain("Size:", Hover(source, options: new(ShowTypeLayout: false)).GetProperty("contents").GetProperty("value").GetString());
    }

    [Fact]
    public void UnknownExceptionsDoNotCreateAnEmptySection()
    {
        string text = Hover("void F|ail(void()* callback) { callback(); }", options: new(true)).GetProperty("contents").GetProperty("value").GetString()!;
        Assert.DoesNotContain("exceptions", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CStringLiteralTypesHaveIndividualKeywordAndPunctuationRuns()
    {
        var header = Hover("readonly(char)* F() => \"he|llo\";", true).GetProperty("_vs_rawContent").GetProperty("Elements")[0].GetProperty("Elements");
        var runs = header.EnumerateArray().Single(e => e.GetProperty("_vs_type").GetString() == "ClassifiedTextElement").GetProperty("Runs").EnumerateArray().ToArray();
        foreach (string word in new[] { "readonly", "char" })
            Assert.Contains(runs, r => r.GetProperty("Text").GetString() == word && r.GetProperty("ClassificationTypeName").GetString() == "keyword");
        Assert.Contains(runs, r => r.GetProperty("Text").GetString() == "*" && r.GetProperty("ClassificationTypeName").GetString() == "punctuation");
    }

    [Fact]
    public void NumericLiteralPreviewIncludesItsValue()
        => Assert.Contains("(int)123", Hover("int F() => 1|23;").GetProperty("contents").GetProperty("value").GetString());

    [Fact]
    public void ReferenceParameterHoverShowsTheBorrowingContract()
    {
        var hover = Hover("void Re|ad(ref readonly(int) value) {}", true);
        Assert.Contains("ref readonly(int) value", hover.GetProperty("contents").GetProperty("value").GetString());
        var runs = hover.GetProperty("_vs_rawContent").GetProperty("Elements")[0].GetProperty("Elements")[1].GetProperty("Runs").EnumerateArray();
        Assert.Contains(runs, r => r.GetProperty("Text").GetString() == "ref" && r.GetProperty("ClassificationTypeName").GetString() == "keyword");
    }

    [Theory]
    [InlineData("immutable(char*)")]
    [InlineData("immutable(char**)")]
    [InlineData("immutable(char*?)")]
    [InlineData("immutable(char[4])")]
    [InlineData("readonly(char)*")]
    [InlineData("immutable(char*)*")]
    [InlineData("immutable(char^)")]
    public void ReadonlyTypesAvoidRepeatingInheritedQualifications(string type)
    {
        var hover = Hover("void Read(" + type + " mes|sage) {}", true);
        string text = hover.GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains(type + " message", text);
        Assert.DoesNotContain("immutable(immutable", text);
    }

    [Fact]
    public void ReferencePropertyHoverShowsItsBorrowingContract()
        => Assert.Contains("ref int S.Value", Hover("struct S { int n; public ref int Va|lue => n; }").GetProperty("contents").GetProperty("value").GetString());

    [Fact]
    public void HoveringTheUnderlyingNameStillShowsTheUnderlyingLayout()
    {
        var text = Hover("struct S { int x; } void F(immutable(|S)*? p) {}").GetProperty("contents").GetProperty("value").GetString();
        Assert.Contains("struct S", text);
        Assert.Contains("**Size:** 4 bytes", text);
    }

    [Fact]
    public void TypeNamesAreNotShortenedToShadowingGenericParameters()
        => Assert.Contains("N::S", Hover("using N; namespace N { public struct S {} } N::S Ma|ke<S>() => new N::S();").GetProperty("contents").GetProperty("value").GetString());

    [Theory]
    [InlineData("struct S {} void F(read|only(S)*? p) {}", "readonly(S)*?", 8)]
    [InlineData("struct S {} void F(immu|table(S)*? p) {}", "immutable(S)*?", 8)]
    [InlineData("struct S {} void F(immutable(S)|*? p) {}", "immutable(S)*?", 8)]
    [InlineData("struct S {} void F(immutable(S)*|? p) {}", "immutable(S)*?", 8)]
    [InlineData("struct S {} void F(S|^? p) {}", "S^?", 8)]
    public void CompoundAnnotationsHaveTheirOwnHover(string source, string type, int size)
    {
        string text = Hover(source).GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("```gflat\n" + type + "\n```", text);
        Assert.Contains($"**Size:** {size} bytes", text);
    }

    [Theory]
    [InlineData("int Ma|ke() => 1;", "Make")]
    [InlineData("struct S { public int fi|eld; }", "field")]
    [InlineData("void F() { int lo|cal = 1; }", "local")]
    [InlineData("void F(int va|lue) {}", "value")]
    public void SymbolNamesNavigateToTheirDeclarations(string source, string name)
    {
        var hover = Hover(source, true);
        var header = hover.GetProperty("_vs_rawContent").GetProperty("Elements")[0].GetProperty("Elements");
        var text = header.EnumerateArray().Single(e => e.GetProperty("_vs_type").GetString() == "ClassifiedTextElement");
        var run = text.GetProperty("Runs").EnumerateArray().Single(r => r.GetProperty("Text").GetString() == name);
        Assert.Equal(source.Replace("|", "").IndexOf(name, StringComparison.Ordinal), run.GetProperty("_gflat_target").GetProperty("range").GetProperty("start").GetProperty("character").GetInt32());
    }

    [Theory]
    [InlineData("using N; namespace N { public struct S {} public S Make() => new S(); } void F() { Ma|ke(); }", "S N::Make()")]
    [InlineData("namespace N { public struct S {} public S Make() => new S(); } void F() { N::Ma|ke(); }", "N::S N::Make()")]
    [InlineData("using N; using Other; namespace N { public struct S {} public S Make() => new S(); } namespace Other { public struct S {} } void F() { N::Ma|ke(); }", "N::S N::Make()")]
    public void ReturnTypesUseTheHoverLocationsImports(string source, string expected)
        => Assert.Contains(expected, Hover(source).GetProperty("contents").GetProperty("value").GetString());

    [Fact]
    public void ActualStandardStringFactoryUsesSourceTypeName()
    {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "EditorFixtures", "String.gf"));
        var result = Hover(source.Insert(source.IndexOf("From(", StringComparison.Ordinal) + 1, "|"));
        string text = result.GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("String String::From(", text);
        Assert.DoesNotContain("$", text);
    }

    [Fact]
    public void NamespacedReturnTypesAreReadableAndNavigable()
    {
        const string source = "namespace Standard { struct String { public static String Fr|om() => new String(); } }";
        var result = Hover(source, true);
        Assert.Contains("String String::From()", result.GetProperty("contents").GetProperty("value").GetString());
        Assert.DoesNotContain("$", result.GetProperty("contents").GetProperty("value").GetString());
        var runs = result.GetProperty("_vs_rawContent").GetProperty("Elements")[0].GetProperty("Elements")[1].GetProperty("Runs").EnumerateArray();
        var type = runs.First(r => r.GetProperty("Text").GetString() == "String" && r.GetProperty("_gflat_target").ValueKind == JsonValueKind.Object);
        Assert.Equal(source.IndexOf("String", StringComparison.Ordinal), type.GetProperty("_gflat_target").GetProperty("range").GetProperty("start").GetProperty("character").GetInt32());
    }

    [Theory]
    [InlineData("struct", "struct name")]
    [InlineData("class", "class name")]
    public void ReturnTypeAndContainingTypeUseTheSameClassification(string kind, string classification)
    {
        string source = $"namespace Standard {{ {kind} String {{ public static String Fr|om() => new String(); }} }}";
        var result = Hover(source, true);
        var header = result.GetProperty("_vs_rawContent").GetProperty("Elements")[0].GetProperty("Elements");
        var text = header.EnumerateArray().Single(e => e.GetProperty("_vs_type").GetString() == "ClassifiedTextElement");
        var names = text.GetProperty("Runs").EnumerateArray().Where(r => r.GetProperty("Text").GetString() == "String").ToArray();
        Assert.Equal(2, names.Length);
        Assert.All(names, run =>
        {
            Assert.Equal(classification, run.GetProperty("ClassificationTypeName").GetString());
            Assert.Equal(source.IndexOf("String", StringComparison.Ordinal), run.GetProperty("_gflat_target").GetProperty("range").GetProperty("start").GetProperty("character").GetInt32());
        });
    }

    [Theory]
    [InlineData("int F() => 1|23;", "int")]
    [InlineData("float F() => 1|.5f;", "float")]
    [InlineData("long F() => 1|23l;", "long")]
    [InlineData("bool F() => tr|ue;", "bool")]
    [InlineData("char F() => '|a';", "char")]
    [InlineData("readonly(char)* F() => \"he|llo\";", "readonly(char)*")]
    public void LiteralsShowTheirCompilerTypeWithoutLayout(string source, string expected)
    {
        var hover = Hover(source);
        string text = hover.GetProperty("contents").GetProperty("value").GetString()!;
        Assert.StartsWith("```gflat\n(" + expected + ")", text);
        Assert.DoesNotContain("Size:", text);
    }

    [Fact]
    public void NestedExceptionAndReturnTypesUseSourceSpelling()
    {
        string text = Hover("class Outer { public class Failure : Exception {} } Outer::Failure* Re|ad() { throw new Outer::Failure(); }", options: new(ShowKnownExceptions: true)).GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("Outer::Failure* Read();", text);
        Assert.Contains("Known exceptions:  \n  Outer::Failure", text);
        Assert.DoesNotContain("Outer.Failure", text);
        Assert.DoesNotContain("derived types", text);
    }

    [Fact]
    public void GenericReturnTypesUseSourceSpelling()
    {
        string text = Hover("struct Box<T> { public T value; } Box<int> Re|ad() => new Box<int>();").GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("Box<int> Read()", text);
        Assert.DoesNotContain("$", text);
    }

    [Fact]
    public void TerminatingMethodDoesNotAdvertiseOutgoingExceptions()
        => Assert.DoesNotContain("Exceptions:", Hover("void Ru|n() nothrow { throw new Exception(); }").GetProperty("contents").GetProperty("value").GetString());

    [Fact]
    public void HoverShowsEscapingExceptionsWithClassifiedTypes()
    {
        var result = Hover("class Failure : Exception {} void Fail() { throw new Failure(); } void Ru|n() { Fail(); }", true, new(ShowKnownExceptions: true));
        Assert.Contains("Known exceptions:  \n  Failure", result.GetProperty("contents").GetProperty("value").GetString());
        var runs = result.GetProperty("_vs_rawContent").GetProperty("Elements")[2].GetProperty("Runs").EnumerateArray().ToArray();
        Assert.Equal(0, result.GetProperty("_vs_rawContent").GetProperty("Elements")[1].GetProperty("Runs")[0].GetProperty("Style").GetInt32());
        Assert.Contains(runs, r => r.GetProperty("Text").GetString() == "Failure" && r.GetProperty("ClassificationTypeName").GetString() == "class name");
    }

    [Theory]
    [InlineData("void Ru|n() {}")]
    [InlineData("void Ru|n() { try { throw new Exception(); } catch {} }")]
    [InlineData("void Ru|n() { Missing(); throw new Exception(); }")]
    public void EmptyCaughtOrIncompleteAnalysisDoesNotInventExceptionDetails(string code)
        => Assert.DoesNotContain("Exceptions:", Hover(code).GetProperty("contents").GetProperty("value").GetString());

    [Fact]
    public void LayoutRowsUseBoldLabelsInNativeHover()
    {
        var rows = Hover("struct Pa|ir { public int a; }", true).GetProperty("_vs_rawContent").GetProperty("Elements");
        Assert.Equal(3, rows.GetArrayLength());
        Assert.Equal("Size:", rows[1].GetProperty("Runs")[0].GetProperty("Text").GetString());
        Assert.Equal(1, rows[1].GetProperty("Runs")[0].GetProperty("Style").GetInt32());
        Assert.Equal("Alignment:", rows[2].GetProperty("Runs")[0].GetProperty("Text").GetString());
        Assert.Equal(1, rows[2].GetProperty("Runs")[0].GetProperty("Style").GetInt32());
    }

    [Fact]
    public void HoverShowsOtherOverloadsAndSelectedSignature()
    {
        var result = Hover("int Read(int x) => x; int Read(bool x) => 2; int Read(char x) => 3; int main() => Re|ad(true);", true);
        string text = result.GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("Read(bool x)", text);
        Assert.Contains("+2 overloads", text);
        Assert.Equal("+2 overloads", result.GetProperty("_vs_rawContent").GetProperty("Elements")[1].GetProperty("Runs")[0].GetProperty("Text").GetString());
    }

    [Fact]
    public void HoverDoesNotCountPrivateOrUnrelatedOverloads()
    {
        string text = Hover("class A { public static int F(int x) => x; public static int F(bool x) => 2; private static int F(char x) => 3; } class B { public static int F(int x) => x; } int main() => A::|F(1);").GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("+1 overload", text);
        Assert.DoesNotContain("+2", text);
    }

    [Fact]
    public void SingleMethodHasNoOverloadFooter()
        => Assert.DoesNotContain("overload", Hover("int Re|ad(int x) => x;").GetProperty("contents").GetProperty("value").GetString());

    [Fact]
    public void RichHoverUsesClassifiedRunsAndCatalogIcons()
    {
        var result = Hover("struct Result {} class Factory { public static Result Cr|eate(int count) nothrow => new Result(); }", true);
        var rows = result.GetProperty("_vs_rawContent").GetProperty("Elements");
        var header = rows[0].GetProperty("Elements");
        Assert.Equal("ImageElement", header[0].GetProperty("_vs_type").GetString());
        Assert.Equal(Catalog, header[0].GetProperty("ImageId").GetProperty("Guid").GetGuid());
        Assert.Equal(1, header[0].GetProperty("ImageId").GetProperty("Id").GetInt32());
        var runs = header[1].GetProperty("Runs").EnumerateArray().ToArray();
        Assert.Contains(runs, r => r.GetProperty("Text").GetString() == "Create" && r.GetProperty("ClassificationTypeName").GetString() == "method name");
        Assert.Contains(runs, r => r.GetProperty("Text").GetString() == "Result" && r.GetProperty("ClassificationTypeName").GetString() == "struct name");
        Assert.Contains(runs, r => r.GetProperty("Text").GetString() == "count" && r.GetProperty("ClassificationTypeName").GetString() == "parameter name");
        Assert.Contains(runs, r => r.GetProperty("Text").GetString() == "nothrow" && r.GetProperty("ClassificationTypeName").GetString() == "keyword");
        Assert.Contains("public static Result Factory::Create(int count) nothrow", result.GetProperty("contents").GetProperty("value").GetString());
    }

    [Fact]
    public void PortableHoverDoesNotSendVisualStudioExtensions()
    {
        var result = Hover("struct Pa|ir { public int left; public double right; }");
        Assert.False(result.TryGetProperty("_vs_rawContent", out _));
        Assert.Contains("**Size:** 16 bytes  \n**Alignment:** 8 bytes", result.GetProperty("contents").GetProperty("value").GetString());
    }

    [Theory]
    [InlineData("class Wi|de { public extralong field; }", "**Object size:** 32 bytes  \n**Alignment:** 16 bytes")]
    [InlineData("class Wide { public extralong field; } void F(Wide* po|inter) {}", null)]
    [InlineData("interface I {} void F(I* po|inter) {}", null)]
    [InlineData("int main() { do|uble value = 0; return 0; }", "**Size:** 8 bytes  \n**Alignment:** 8 bytes")]
    public void LayoutIsShownForTypesButNotVariables(string source, string? expected)
    {
        string text = Hover(source).GetProperty("contents").GetProperty("value").GetString()!;
        if (expected != null) Assert.Contains(expected, text);
        else Assert.DoesNotContain("bytes", text);
    }

    [Theory]
    [InlineData("struct Bo|x<T> { public T value; }")]
    [InlineData("struct Bo|x { public Missing value; }")]
    public void OpenOrInvalidLayoutsAreNotGuessed(string source)
        => Assert.DoesNotContain("bytes", Hover(source).GetProperty("contents").GetProperty("value").GetString());

    [Fact]
    public void PropertiesShowAccessorRestrictions()
        => Assert.Contains("{ get; private set; }", Hover("struct S { public int Co|unt { get; private set; } }").GetProperty("contents").GetProperty("value").GetString());

    [Fact]
    public void UndeclaredStringHasNoInventedHover()
        => Assert.Equal(JsonValueKind.Null, Hover("str|ing value;").ValueKind);

    [Fact]
    public void UserDefinedStringHasItsActualStructLayout()
        => Assert.Contains("**Size:** 4 bytes", Hover("struct string { public int value; } void Main() { str|ing item; }").GetProperty("contents").GetProperty("value").GetString());
}
