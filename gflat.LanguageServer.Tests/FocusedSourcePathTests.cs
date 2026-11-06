using gflat.ast;

namespace gflat.LanguageServer.Tests;

public sealed class FocusedSourcePathTests
{
    [Fact]
    public void FocusedBodySelectionUsesTheHostsFilePathRules()
    {
        var source = new SourceFile(Path.GetFullPath("MixedCaseSource.gf"), "int Read() { int local = Missing(); return local; }");
        string selectedPath = source.Path.ToLowerInvariant();
        if (OperatingSystem.IsWindows()) selectedPath = selectedPath.Replace('\\', '/');
        var selected = new HashSet<SourceId> { new(selectedPath) };
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true }, bodySources: selected);
        bool expected = OperatingSystem.IsWindows();
        Assert.Equal(expected, snapshot.Nodes.Any(node => node.Node is VariableDeclaration { Name: "local" }));
        Assert.Equal(expected, snapshot.Diagnostics.Any(diagnostic => diagnostic.Message.Contains("Missing", StringComparison.Ordinal)));
    }
}
