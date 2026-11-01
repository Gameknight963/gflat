using gflat.ast;
using gflat.diagnostics;

namespace gflat.Tests;

public sealed class CompilerCancellationTests
{
    [Fact]
    public void PreCanceledCompilationAndSnapshotDoNotEnumerateSources()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        bool enumerated = false;
        IEnumerable<SourceFile> Sources()
        {
            enumerated = true;
            yield return new SourceFile("main.gf", "int main() => 0;");
        }
        var diagnostics = new DiagnosticBag();
        var compilationError = Assert.Throws<OperationCanceledException>(() =>
            new Compilation(Sources(), diagnostics, cancellationToken: cancellation.Token));
        var snapshotError = Assert.Throws<OperationCanceledException>(() =>
            new AnalysisSnapshot(Sources(), cancellationToken: cancellation.Token));
        Assert.Equal(cancellation.Token, compilationError.CancellationToken);
        Assert.Equal(cancellation.Token, snapshotError.CancellationToken);
        Assert.False(enumerated);
        Assert.Empty(diagnostics.Items);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancellationDuringEnumerationStopsBeforeParsingAndReportingErrors(bool recoverErrors)
    {
        using var cancellation = new CancellationTokenSource();
        bool reachedThird = false;
        IEnumerable<SourceFile> Sources()
        {
            yield return new SourceFile("first.gf", "int main() => 0;");
            cancellation.Cancel();
            yield return new SourceFile("second.gf", "not valid gflat syntax");
            reachedThird = true;
            yield return new SourceFile("third.gf", "also invalid");
        }
        var diagnostics = new DiagnosticBag();
        var error = Assert.Throws<OperationCanceledException>(() =>
            new Compilation(Sources(), diagnostics, recoverErrors, cancellationToken: cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.False(reachedThird);
        Assert.Empty(diagnostics.Items);
    }

    [Fact]
    public void CancellationAtTheEndOfEnumerationStillPreventsParsing()
    {
        using var cancellation = new CancellationTokenSource();
        IEnumerable<SourceFile> Sources()
        {
            yield return new SourceFile("main.gf", "not valid gflat syntax");
            cancellation.Cancel();
        }
        var diagnostics = new DiagnosticBag();
        Assert.Throws<OperationCanceledException>(() =>
            new Compilation(Sources(), diagnostics, cancellationToken: cancellation.Token));
        Assert.Empty(diagnostics.Items);
    }

    [Fact]
    public void SnapshotRecoveryDoesNotConvertCancellationIntoDiagnostics()
    {
        using var cancellation = new CancellationTokenSource();
        IEnumerable<SourceFile> Sources()
        {
            yield return new SourceFile("first.gf", "int main() => 0;");
            cancellation.Cancel();
            yield return new SourceFile("second.gf", "not valid gflat syntax");
        }
        var error = Assert.Throws<OperationCanceledException>(() =>
            new AnalysisSnapshot(Sources(), cancellationToken: cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CheckerObservesCancellationAfterConstructionBeforeUnitAndMethodAnalysis(bool recoverErrors)
    {
        using var cancellation = new CancellationTokenSource();
        var diagnostics = new DiagnosticBag();
        var checker = new TypeChecker(diagnostics, recoverErrors, cancellation.Token);
        var compilation = new Compilation([new SourceFile("main.gf", "int main() => 0;")],
            new DiagnosticBag(), options: new() { NoStdLib = true });
        var method = Assert.Single(compilation.Unit.Members.OfType<MethodDeclaration>());
        cancellation.Cancel();
        var unitError = Assert.Throws<OperationCanceledException>(() => compilation.Unit.Accept(checker));
        var methodError = Assert.Throws<OperationCanceledException>(() => method.Accept(checker));
        Assert.Equal(cancellation.Token, unitError.CancellationToken);
        Assert.Equal(cancellation.Token, methodError.CancellationToken);
        Assert.Empty(diagnostics.Items);
    }

    [Fact]
    public void CheckerRejectsPreCancellationAndDefaultInstancesRemainIndependent()
    {
        using var cancellation = new CancellationTokenSource();
        var defaultChecker = new TypeChecker();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => new TypeChecker(cancellationToken: cancellation.Token));
        var compilation = new Compilation([new SourceFile("main.gf", "int main() => 0;")],
            new DiagnosticBag(), options: new() { NoStdLib = true });
        compilation.Unit.Accept(defaultChecker);
        Assert.False(defaultChecker.Diagnostics.HasErrors);
        var snapshot = new AnalysisSnapshot([new SourceFile("main.gf", "int main() => 0;")], new() { NoStdLib = true });
        Assert.NotNull(snapshot.Checker);
        Assert.Empty(snapshot.Diagnostics);
    }
}
