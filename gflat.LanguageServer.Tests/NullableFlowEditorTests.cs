using System.Text.Json;
using gflat.LanguageServer;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class NullableFlowEditorTests
{
    [Fact]
    public void GuardedCallsHaveHoverAndPreserveNullableParameterDeclaration()
    {
        var source = new SourceFile(Path.GetFullPath("nullable.gf"), """
            struct Item { public immutable int Get() => 42; }
            int Read(Item*? p) { if (p == null) return 0; return p.Get(); }
            """);
        var snapshot = new AnalysisSnapshot([source]);
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, [source]);
        int call = source.Text.LastIndexOf("Get", StringComparison.Ordinal);
        var hover = JsonSerializer.SerializeToElement(model.Hover(source.Path, call), Protocol.JsonOptions);
        Assert.Contains("Item.Get", hover.GetProperty("contents").GetProperty("value").GetString());
        Assert.NotNull(model.Definition(source.Path, call));
        int declaration = source.Text.IndexOf(" p)", StringComparison.Ordinal) + 1;
        var parameter = JsonSerializer.SerializeToElement(model.Hover(source.Path, declaration), Protocol.JsonOptions);
        Assert.Contains("Item*? p", parameter.GetProperty("contents").GetProperty("value").GetString());
    }

    [Fact]
    public void InvalidAssignmentDropsFactsAndDoesNotHideLaterDeclarations()
    {
        var source = new SourceFile(Path.GetFullPath("nullable-errors.gf"), """
            struct Item { public immutable int Get() => 42; }
            int Bad(Item*? p) { if (p == null) return 0; p = Missing(); return p.Get(); }
            int Good(Item*? p) { if (p == null) return 0; return p.Get(); }
            """);
        var snapshot = new AnalysisSnapshot([source]);
        Assert.Contains(snapshot.Diagnostics, diagnostic => diagnostic.Message.Contains("nullable", StringComparison.OrdinalIgnoreCase));
        var model = new EditorModel(snapshot, [source]);
        int goodCall = source.Text.LastIndexOf("Get", StringComparison.Ordinal);
        Assert.NotNull(model.Hover(source.Path, goodCall));
        Assert.NotNull(model.Definition(source.Path, goodCall));
    }
}
