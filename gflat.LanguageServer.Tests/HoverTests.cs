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
        Assert.Contains("Size: 16 bytes; alignment: 8 bytes.", result.GetProperty("contents").GetProperty("value").GetString());
    }

    [Theory]
    [InlineData("class Wi|de { public extralong field; }", "Object size: 32 bytes; alignment: 16 bytes.")]
    [InlineData("class Wide { public extralong field; } void F(Wide* po|inter) {}", "Size: 8 bytes; alignment: 8 bytes.")]
    [InlineData("interface I {} void F(I* po|inter) {}", "Size: 16 bytes; alignment: 8 bytes.")]
    [InlineData("int main() { do|uble value = 0; return 0; }", "Size: 8 bytes; alignment: 8 bytes.")]
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
