using System.Text.Json;
using gflat.LanguageServer;

namespace gflat.LanguageServer.Tests;

public class HoverTests
{
    private static readonly Guid Catalog = Guid.Parse("ae27a6b0-e345-4288-96df-5eaf394ee369");
    private static JsonElement Hover(string marked, bool rich = false)
    {
        int offset = marked.IndexOf('|');
        string path = Path.GetFullPath("hover.gf");
        var source = new SourceFile(path, marked.Remove(offset, 1));
        var model = new EditorModel(new AnalysisSnapshot([source]), [source]);
        var icons = JsonSerializer.SerializeToElement(new Dictionary<string, object> { ["method.public"] = new { guid = Catalog, id = 1 }, ["struct.public"] = new { guid = Catalog, id = 2 } });
        return JsonSerializer.SerializeToElement(model.Hover(path, offset, rich ? new VisualStudioHover(icons) : null), Protocol.JsonOptions);
    }

    [Fact]
    public void HoveringTheUnderlyingNameStillShowsTheUnderlyingLayout()
    {
        var text = Hover("struct S { int x; } void F(readonly(|S)*? p) {}").GetProperty("contents").GetProperty("value").GetString();
        Assert.Contains("struct S", text);
        Assert.Contains("**Size:** 4 bytes", text);
    }

    [Fact]
    public void TypeNamesAreNotShortenedToShadowingGenericParameters()
        => Assert.Contains("N::S", Hover("using N; namespace N { public struct S {} } N::S Ma|ke<S>() => new N::S();").GetProperty("contents").GetProperty("value").GetString());

    [Theory]
    [InlineData("struct S {} void F(read|only(S)*? p) {}", "readonly(S)*?", 8)]
    [InlineData("struct S {} void F(readonly(S)|*? p) {}", "readonly(S)*?", 8)]
    [InlineData("struct S {} void F(readonly(S)*|? p) {}", "readonly(S)*?", 8)]
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
        const string source = "namespace std { struct String { public static String Fr|om() => new String(); } }";
        var result = Hover(source, true);
        Assert.Contains("String String::From()", result.GetProperty("contents").GetProperty("value").GetString());
        Assert.DoesNotContain("$", result.GetProperty("contents").GetProperty("value").GetString());
        var runs = result.GetProperty("_vs_rawContent").GetProperty("Elements")[0].GetProperty("Elements")[1].GetProperty("Runs").EnumerateArray();
        var type = runs.First(r => r.GetProperty("Text").GetString() == "String" && r.GetProperty("_gflat_target").ValueKind == JsonValueKind.Object);
        Assert.Equal(source.IndexOf("String", StringComparison.Ordinal), type.GetProperty("_gflat_target").GetProperty("range").GetProperty("start").GetProperty("character").GetInt32());
    }

    [Theory]
    [InlineData("int F() => 1|23;", "int")]
    [InlineData("float F() => 1|.5f;", "float")]
    [InlineData("long F() => 1|23l;", "long")]
    [InlineData("bool F() => tr|ue;", "bool")]
    [InlineData("char F() => '|a';", "char")]
    [InlineData("readonly(char)* F() => c\"he|llo\";", "readonly(char)*")]
    public void LiteralsShowTheirCompilerTypeWithoutLayout(string source, string expected)
    {
        var hover = Hover(source);
        Assert.Equal("```gflat\n" + expected + "\n```", hover.GetProperty("contents").GetProperty("value").GetString());
    }

    [Fact]
    public void NestedExceptionAndReturnTypesUseSourceSpelling()
    {
        string text = Hover("class Outer { public class Failure : Exception {} } Outer::Failure* Re|ad() throws { throw new* Outer::Failure(); }").GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("Outer::Failure* Read() throws;", text);
        Assert.Contains("Exceptions:  \n  Outer::Failure", text);
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
        => Assert.DoesNotContain("Exceptions:", Hover("void Ru|n() { throw new* Exception(); }").GetProperty("contents").GetProperty("value").GetString());

    [Fact]
    public void HoverShowsEscapingExceptionsWithClassifiedTypes()
    {
        var result = Hover("class Failure : Exception {} void Fail() throws { throw new* Failure(); } void Ru|n() throws { Fail(); }", true);
        Assert.Contains("Exceptions:  \n  Failure", result.GetProperty("contents").GetProperty("value").GetString());
        var runs = result.GetProperty("_vs_rawContent").GetProperty("Elements")[2].GetProperty("Runs").EnumerateArray().ToArray();
        Assert.Equal(0, result.GetProperty("_vs_rawContent").GetProperty("Elements")[1].GetProperty("Runs")[0].GetProperty("Style").GetInt32());
        Assert.Contains(runs, r => r.GetProperty("Text").GetString() == "Failure" && r.GetProperty("ClassificationTypeName").GetString() == "class name");
    }

    [Theory]
    [InlineData("void Ru|n() throws {}")]
    [InlineData("void Ru|n() { try { throw new* Exception(); } catch {} }")]
    [InlineData("void Ru|n() throws { Missing(); throw new* Exception(); }")]
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
        var result = Hover("struct Result {} class Factory { public static Result Cr|eate(int count) throws => new Result(); }", true);
        var rows = result.GetProperty("_vs_rawContent").GetProperty("Elements");
        var header = rows[0].GetProperty("Elements");
        Assert.Equal("ImageElement", header[0].GetProperty("_vs_type").GetString());
        Assert.Equal(Catalog, header[0].GetProperty("ImageId").GetProperty("Guid").GetGuid());
        Assert.Equal(1, header[0].GetProperty("ImageId").GetProperty("Id").GetInt32());
        var runs = header[1].GetProperty("Runs").EnumerateArray().ToArray();
        Assert.Contains(runs, r => r.GetProperty("Text").GetString() == "Create" && r.GetProperty("ClassificationTypeName").GetString() == "method name");
        Assert.Contains(runs, r => r.GetProperty("Text").GetString() == "Result" && r.GetProperty("ClassificationTypeName").GetString() == "struct name");
        Assert.Contains(runs, r => r.GetProperty("Text").GetString() == "count" && r.GetProperty("ClassificationTypeName").GetString() == "parameter name");
        Assert.Contains(runs, r => r.GetProperty("Text").GetString() == "throws" && r.GetProperty("ClassificationTypeName").GetString() == "keyword");
        Assert.Contains("public static Result Factory::Create(int count) throws", result.GetProperty("contents").GetProperty("value").GetString());
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
    public void ReservedStringKeywordDoesNotInventALayoutOrCrashHover()
        => Assert.DoesNotContain("bytes", Hover("str|ing value;").GetProperty("contents").GetProperty("value").GetString());
}
