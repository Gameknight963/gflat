using System.Text.Json;

namespace gflat.LanguageServer.Tests;

public sealed partial class ServerTests
{
    [Theory]
    [InlineData("", 0, 0, "x", "x")]
    [InlineData("abc", 0, 3, "xyz", "xyz")]
    [InlineData("abc", 0, 3, "", "")]
    [InlineData("abc", 0, 0, "x", "xabc")]
    [InlineData("abc", 3, 3, "x", "abcx")]
    [InlineData("abc", 1, 2, "x", "axc")]
    [InlineData("abc", 1, 1, "", "abc")]
    [InlineData("a\U0001F600b", 1, 3, "x", "axb")]
    public void IncrementalEditsHandleEmptyAndWholeDocumentRanges(string original, int start, int end, string replacement, string expected)
    {
        var changes = JsonSerializer.SerializeToElement(new[] {
            new { range = new { start = new { line = 0, character = start }, end = new { line = 0, character = end } }, text = replacement }
        });
        Assert.Equal(expected, Workspace.ApplyChanges(original, changes));
    }
}
