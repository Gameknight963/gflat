using System.Text.Json;

namespace gflat.LanguageServer.Tests;

public sealed class EnumValueHoverTests
{
    [Theory]
    [InlineData("sbyte", "First = -3, Value", "-2")]
    [InlineData("int", "Value = 7 * 6", "42")]
    [InlineData("ulong", "Value = 18446744073709551615ul", "18446744073709551615")]
    public void MemberHoversShowEvaluatedValues(string storage, string members, string expected)
    {
        string text = "namespace Sample; enum Code : " + storage + " { " + members + " } int main() => (int)Code::Value;";
        var source = new SourceFile(Path.GetFullPath("enum-value-hover.gf"), text);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
        Assert.Empty(snapshot.Diagnostics);
        var model = new EditorModel(snapshot, [source]);
        string hover = JsonSerializer.Serialize(model.Hover(source.Path, text.LastIndexOf("Value", StringComparison.Ordinal)), Protocol.JsonOptions);
        Assert.Contains(" = " + expected, hover);
        var rich = JsonSerializer.SerializeToElement(model.Hover(source.Path, text.LastIndexOf("Value", StringComparison.Ordinal),
            new VisualStudioHover(JsonSerializer.SerializeToElement(new { }))), Protocol.JsonOptions);
        var runs = rich.GetProperty("_vs_rawContent").GetProperty("Elements")[0].GetProperty("Elements")[0].GetProperty("Runs").EnumerateArray();
        Assert.Contains(runs, run => run.GetProperty("Text").GetString() == expected && run.GetProperty("ClassificationTypeName").GetString() == "number");
    }
}
