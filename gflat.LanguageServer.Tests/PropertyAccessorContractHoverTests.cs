using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class PropertyAccessorContractHoverTests
{
    [Theory]
    [InlineData("immutable get nothrow => storage;", "set { storage = value; }", "{ immutable get nothrow; set; }")]
    [InlineData("readonly get => storage;", "set nothrow { storage = value; }", "{ readonly get; set nothrow; }")]
    [InlineData("get nothrow => storage;", "set nothrow { storage = value; }", "{ get nothrow; set nothrow; }")]
    [InlineData("get => storage;", "set { storage = value; }", "{ get; set; }")]
    public void PropertyHoverShowsEachAccessorsOwnExceptionContract(string getter, string setter, string expected)
    {
        string text = "struct Meter { private int storage; public Meter(){storage=0;} public int Value { " +
            getter + " " + setter + " } } int Use(){Meter item=new();item.Value=2;return item.Value;}";
        var source = new SourceFile(Path.GetFullPath("property-contract-hover.gf"), text);
        SourceFile[] sources = [source];
        var snapshot = new AnalysisSnapshot(sources);
        Assert.DoesNotContain(snapshot.Diagnostics, item => item.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, sources);
        foreach (int use in new[] { text.IndexOf("Value=", StringComparison.Ordinal), text.IndexOf("Value;", StringComparison.Ordinal) })
        {
            var hover = JsonSerializer.SerializeToElement(model.Hover(source.Path, use), Protocol.JsonOptions);
            string signature = hover.GetProperty("contents").GetProperty("value").GetString()!;
            Assert.Contains("int Meter.Value", signature);
            Assert.Contains(expected, signature);
            Assert.DoesNotContain('$', signature);
        }
    }
}
