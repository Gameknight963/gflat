namespace gflat.LanguageServer.Tests;

public sealed class EditorIndexCancellationTests
{
    [Fact]
    public void CancellationDuringSourceEnumerationStopsIndexConstruction()
    {
        var file = new SourceFile(Path.GetFullPath("cancel-index.gf"), "int Answer() => 42;");
        var snapshot = new AnalysisSnapshot([file], new() { NoStdLib = true });
        using var cancellation = new CancellationTokenSource();
        IEnumerable<SourceFile> Files()
        {
            yield return file;
            cancellation.Cancel();
        }
        Assert.Throws<OperationCanceledException>(() => new EditorModel(snapshot, Files(), cancellation.Token));
        var retry = new EditorModel(snapshot, [file]);
        Assert.NotNull(retry.Hover(file.Path, file.Text.IndexOf("Answer", StringComparison.Ordinal)));
    }
}
