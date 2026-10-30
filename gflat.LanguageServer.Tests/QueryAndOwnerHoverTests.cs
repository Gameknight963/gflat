using System.Text.Json;
using gflat.LanguageServer;

namespace gflat.LanguageServer.Tests;

public sealed class QueryAndOwnerHoverTests
{
    private static JsonElement Hover(string marked, bool layout = false)
    {
        int offset = marked.IndexOf('|');
        var source = new SourceFile(Path.GetFullPath("queries.gf"), marked.Remove(offset, 1));
        var model = new EditorModel(new AnalysisSnapshot([source]), [source]);
        return JsonSerializer.SerializeToElement(model.Hover(source.Path, offset,
            new VisualStudioHover(JsonSerializer.SerializeToElement(new { })), new(ShowTypeLayout: layout)), Protocol.JsonOptions);
    }

    private static string Text(JsonElement hover) => hover.GetProperty("contents").GetProperty("value").GetString()!;
    private static JsonElement[] Runs(JsonElement hover) => hover.GetProperty("_vs_rawContent").GetProperty("Elements")[0]
        .GetProperty("Elements").EnumerateArray().Single(e => e.GetProperty("_vs_type").GetString() == "ClassifiedTextElement")
        .GetProperty("Runs").EnumerateArray().ToArray();

    [Fact]
    public void FieldOwnerOmitsNamespaceWithoutChangingReturnTypeQualification()
    {
        var hover = Hover("void F() { typeof(int).Element|Type; }");
        string text = Text(hover);
        Assert.Contains("TypeInfo.ElementType", text);
        Assert.DoesNotContain("Standard::TypeInfo.ElementType", text);
        Assert.DoesNotContain("readonly readonly", text);
        var runs = Runs(hover);
        Assert.Contains(runs, r => r.GetProperty("Text").GetString() == "Standard" && r.GetProperty("ClassificationTypeName").GetString() == "namespace name");
        Assert.Contains(runs, r => r.GetProperty("Text").GetString() == "TypeInfo" && r.GetProperty("ClassificationTypeName").GetString() == "struct name");
        Assert.DoesNotContain(runs, r => r.GetProperty("Text").GetString()!.Contains("Standard::TypeInfo"));
    }

    [Fact]
    public void ReadonlyReceiverAndReadonlyReturnAreBothPreserved()
        => Assert.Contains("immutable readonly(int)*", Text(Hover("struct S { public immutable readonly(int)* R|ead() => (readonly(int)*)0; }")));

    [Theory]
    [InlineData("void F() { Standard|::TypeInfo info; }")]
    [InlineData("void F() { Standard:|:TypeInfo info; }")]
    public void EitherColonShowsTheQualifiedType(string source)
        => Assert.Contains("struct Standard::TypeInfo", Text(Hover(source)));

    [Theory]
    [InlineData("void F() { name|of(Standard::TypeInfo); }", "(readonly(char)*)\"TypeInfo\"")]
    [InlineData("void F() { align|of(int); }", "(int)4")]
    [InlineData("void F() { size|of(long); }", "(int)8")]
    public void ScalarQueryKeywordsShowCheckedValues(string source, string expected)
        => Assert.Contains(expected, Text(Hover(source)));

    [Fact]
    public void TypeofHasCompactDescriptorAndRespectsLayoutPreference()
    {
        const string source = "void F() { type|of(int**); }";
        string text = Text(Hover(source));
        Assert.Contains("typeof(int**)", text);
        Assert.Contains("**Name:** int**", text);
        Assert.Contains("**Kind:** Pointer", text);
        Assert.DoesNotContain("Size:", text);
        Assert.DoesNotContain("ElementType", text);
        string layout = Text(Hover(source, true));
        Assert.Contains("**Size:** 8 bytes", layout);
        Assert.Contains("**Alignment:** 8 bytes", layout);
    }

    [Fact]
    public void UnknownGenericQueriesDoNotInventConcreteValues()
    {
        string text = Text(Hover("int Size<T>() => align|of(T);"));
        Assert.Contains("alignof(T)", text);
        Assert.DoesNotContain("(int)", text);
    }
}
