using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class BoundGenericFunctionValueArgumentsTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FunctionValueHoverUsesSelectedArgumentsRatherThanProtectedFormalTypes(bool explicitAddress, bool protectedFormal)
    {
        string value = explicitAddress ? "&Keep" : "Keep";
        string parameter = protectedFormal ? "immutable(T)" : "T";
        string text = "bool Keep<T>(" + parameter + "* value)=>true;void Probe(){bool(immutable(int)*)* predicate=" + value + ";}";
        var source = new SourceFile(Path.GetFullPath("generic-function-value-display.gf"), text);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, error => error.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, [source]);
        int use = text.LastIndexOf("Keep", StringComparison.Ordinal);
        string hover = Json(model.Hover(source.Path, use)).GetProperty("contents").GetProperty("value").GetString()!;
        // A protected formal does not qualify T; an unqualified T really does
        // bind to the protected pointee in the target signature.
        Assert.Contains(protectedFormal ? "Keep<int>" : "Keep<readonly(int)>", hover);
        Assert.Contains("readonly(int)* value", hover);
        Assert.DoesNotContain("overload", hover);
        int declaration = text.IndexOf("Keep<T>", StringComparison.Ordinal);
        var expected = model.Definition(source.Path, declaration);
        Assert.NotNull(expected);
        Assert.Equal(expected, model.Definition(source.Path, use));
    }
    private static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value, Protocol.JsonOptions);
}
