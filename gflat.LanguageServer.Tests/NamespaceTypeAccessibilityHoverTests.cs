using System.Text.Json;

namespace gflat.LanguageServer.Tests;

public sealed class NamespaceTypeAccessibilityHoverTests
{
    [Theory]
    [InlineData("class Entry{}", "internal class Entry")]
    [InlineData("static class Entry{}", "internal static class Entry")]
    [InlineData("abstract class Entry{}", "internal class Entry")]
    [InlineData("interface Entry{}", "internal interface Entry")]
    [InlineData("static interface Entry{}", "internal static interface Entry")]
    [InlineData("struct Entry{}", "internal struct Entry")]
    [InlineData("enum Entry{Value}", "internal enum Entry")]
    [InlineData("public static class Entry{}", "public static class Entry")]
    [InlineData("private static interface Entry{}", "private static interface Entry")]
    public void TypeHoversUseTheDeclaredOrConsistentImplicitAccessibility(string text, string expected)
    {
        var source = new SourceFile(Path.GetFullPath("namespace-type-hover.gf"), text);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
        Assert.Empty(snapshot.Diagnostics);
        var model = new EditorModel(snapshot, [source]);
        int offset = text.IndexOf("Entry", StringComparison.Ordinal);
        var hover = JsonSerializer.SerializeToElement(model.Hover(source.Path, offset), Protocol.JsonOptions);
        Assert.Contains(expected, hover.GetProperty("contents").GetProperty("value").GetString());
        Assert.NotNull(model.Definition(source.Path, offset));
    }
}
