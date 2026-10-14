using System.Text.Json;
using gflat.LanguageServer;

namespace gflat.LanguageServer.Tests;

public sealed class NameofEditorTests
{
    private static (EditorModel Model, SourceFile Source, int Offset) Analyze(string marked, string library)
    {
        int offset = marked.IndexOf('|');
        var source = new SourceFile(Path.GetFullPath("nameof-main.gf"), marked.Remove(offset, 1));
        var extra = new SourceFile(Path.GetFullPath("nameof-library.gf"), library);
        var analysis = new AnalysisSnapshot([source, extra]);
        Assert.DoesNotContain(analysis.Diagnostics, diagnostic => diagnostic.Severity == gflat.diagnostics.DiagnosticSeverity.Error);
        return (new EditorModel(analysis, [source, extra]), source, offset);
    }

    [Fact]
    public void AliasNameCanBeHoveredAndFollowed()
    {
        var (model, source, offset) = Analyze("using Names; readonly(char)* Name() => nameof(|Handle);", "namespace Names; alias Handle = void*;");
        var hover = JsonSerializer.SerializeToElement(model.Hover(source.Path, offset), Protocol.JsonOptions);
        Assert.Contains("Handle", hover.GetProperty("contents").GetProperty("value").GetString());
        var definition = JsonSerializer.SerializeToElement(model.Definition(source.Path, offset), Protocol.JsonOptions);
        Assert.EndsWith("nameof-library.gf", definition.GetProperty("uri").GetString());
        Assert.Equal(2, model.References(source.Path, offset, true).Length);
    }

    [Theory]
    [InlineData("field")]
    [InlineData("Value")]
    [InlineData("Read")]
    public void NullableReceiverMembersHaveSemanticNavigation(string member)
    {
        var (model, source, offset) = Analyze("using Names; readonly(char)* Name() { Item*? item = null; return nameof(item.|" + member + "); }",
            "namespace Names; struct Item { public int field; public int Value => field; public int Read(int n) => n; public int Read(char n) => 1; }");
        var hover = JsonSerializer.SerializeToElement(model.Hover(source.Path, offset), Protocol.JsonOptions);
        Assert.Contains(member, hover.GetProperty("contents").GetProperty("value").GetString());
        var definition = JsonSerializer.SerializeToElement(model.Definition(source.Path, offset), Protocol.JsonOptions);
        Assert.EndsWith("nameof-library.gf", definition.GetProperty("uri").GetString());
    }

    [Fact]
    public void UninitializedLocalNameIsStillAReference()
    {
        var (model, source, offset) = Analyze("readonly(char)* Name() { int |uninitialized; return nameof(uninitialized); }", "");
        Assert.Equal(2, model.References(source.Path, offset, true).Length);
        Assert.Equal(2, model.DocumentHighlights(source.Path, offset).Length);
    }
}
