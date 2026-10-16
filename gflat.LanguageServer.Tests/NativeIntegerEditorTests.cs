using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class NativeIntegerEditorTests
{
    [Theory]
    [InlineData("bool Empty(nuint length) => length == 0;")]
    [InlineData("nuint Grow(nuint length) => 2 * length + 1;")]
    [InlineData("bool BelowZero(nint value) => value < 0ul;")]
    public void RepresentableNativeLiteralsDoNotProduceEditorErrors(string source)
    {
        var snapshot = new AnalysisSnapshot([new SourceFile("native-literals.gf", source)]);
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData("bool Invalid(nuint length) => length == -1;")]
    [InlineData("nuint Invalid(nuint length, int increment) => length + increment;")]
    [InlineData("bool Invalid(nint length) => length == 18446744073709551615ul;")]
    public void IncompatibleNativeOperandsRemainEditorErrors(string source)
    {
        var snapshot = new AnalysisSnapshot([new SourceFile("native-literals.gf", source)]);
        Assert.Contains(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }
}
