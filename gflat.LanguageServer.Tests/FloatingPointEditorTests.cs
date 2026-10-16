using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class FloatingPointEditorTests
{
    [Theory]
    [InlineData("double F(float x, double y) => x + y;")]
    [InlineData("bool F(float x, double y) => x < y;")]
    [InlineData("double F(double x) { x++; x %= 2.0d; return -x; }")]
    public void FloatingOperationsHaveNoEditorErrors(string code)
    {
        var snapshot = new AnalysisSnapshot([new SourceFile("floating.gf", code)]);
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData("double F(double x, double y) => x & y;")]
    [InlineData("float F(float x) => x << 1;")]
    public void InvalidFloatingOperatorsProduceEditorDiagnostics(string code)
    {
        var snapshot = new AnalysisSnapshot([new SourceFile("floating.gf", code)]);
        Assert.Contains(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }
}
