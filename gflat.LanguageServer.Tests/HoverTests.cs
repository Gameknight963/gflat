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
        Assert.Contains(runs, r => r.GetProperty("Text").GetString() == "Result" && r.GetProperty("ClassificationTypeName").GetString() == "type");
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
    [InlineData("class Wide { public extralong field; } void F(Wide* po|inter) {}", "**Size:** 8 bytes  \n**Alignment:** 8 bytes")]
    [InlineData("interface I {} void F(I* po|inter) {}", "**Size:** 16 bytes  \n**Alignment:** 8 bytes")]
    [InlineData("int main() { do|uble value = 0; return 0; }", "**Size:** 8 bytes  \n**Alignment:** 8 bytes")]
    public void LayoutDistinguishesObjectsPointersAndPrimitives(string source, string expected)
        => Assert.Contains(expected, Hover(source).GetProperty("contents").GetProperty("value").GetString());

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
