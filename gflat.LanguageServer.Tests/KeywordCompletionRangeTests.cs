using System.Text.Json;

namespace gflat.LanguageServer.Tests;

public sealed class KeywordCompletionRangeTests
{
    [Theory]
    [InlineData("void Read(){ret|urn;}", "return")]
    [InlineData("void Read(){return|;}", "return")]
    [InlineData("bool Read()=>tr|ue;", "true")]
    [InlineData("bool Read()=>true|;", "true")]
    [InlineData("cl|ass Item {}", "class")]
    [InlineData("class| Item {}", "class")]
    [InlineData("in|t value;", "int")]
    public void AcceptingExistingKeywordReplacesTheEntireWord(string marked, string keyword)
    {
        int offset = marked.IndexOf('|');
        string text = marked.Remove(offset, 1);
        var source = new SourceFile(Path.GetFullPath("keyword-completion.gf"), text);
        var model = new EditorModel(new AnalysisSnapshot([source], new() { NoStdLib = true }), [source]);
        var completion = JsonSerializer.SerializeToElement(model.Completion(source.Path, offset), Protocol.JsonOptions);
        var item = Assert.Single(completion.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("label").GetString() == keyword && item.GetProperty("kind").GetInt32() == 14);
        var edit = item.GetProperty("textEdit");
        var range = edit.GetProperty("range");
        int start = range.GetProperty("start").GetProperty("character").GetInt32();
        int end = range.GetProperty("end").GetProperty("character").GetInt32();
        Assert.Equal(text, text[..start] + edit.GetProperty("newText").GetString() + text[end..]);
    }
}
