using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class BoundGenericCallArgumentsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProtectedFormalParametersDoNotBecomeGenericTypeArguments(bool explicitArgument)
    {
        string call = explicitArgument ? "Keep<int>" : "Keep";
        string text = "bool Keep<T>(immutable(T)* value)=>true;void Probe(){int value=7;bool result=" + call + "(&value);}";
        var (model, source) = Analyze(text);
        int use = text.LastIndexOf(call + "(", StringComparison.Ordinal);
        string hover = Json(model.Hover(source.Path, use)).GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("Keep<int>", hover);
        Assert.Contains("readonly(int)* value", hover);
        Assert.DoesNotContain("overload", hover);
        var help = Json(model.SignatureHelp(source.Path, use + call.Length + 1));
        string signature = Assert.Single(help.GetProperty("signatures").EnumerateArray()).GetProperty("label").GetString()!;
        Assert.Contains("Keep<int>", signature);
        Assert.Contains("readonly(int)* value", signature);
    }

    [Fact]
    public void UnusedMethodArgumentsRemainKnownInsideAClosedGenericOwner()
    {
        const string text = "struct Value{}struct Box<T>{public int Mark<U>()=>1;}void Probe(){Box<Value> box=new();int first=box.Mark<int>();int second=box.Mark<readonly(int)>();}";
        var (model, source) = Analyze(text);
        foreach (string argument in new[] { "int", "readonly(int)" })
        {
            string call = "Mark<" + argument + ">";
            int use = text.IndexOf(call + "(", StringComparison.Ordinal);
            string hover = Json(model.Hover(source.Path, use)).GetProperty("contents").GetProperty("value").GetString()!;
            Assert.Contains("Box<Value>", hover);
            Assert.Contains(call, hover);
            Assert.DoesNotContain("overload", hover);
            var help = Json(model.SignatureHelp(source.Path, use + call.Length + 1));
            string signature = Assert.Single(help.GetProperty("signatures").EnumerateArray()).GetProperty("label").GetString()!;
            Assert.Contains(call, signature);
        }
    }

    private static (EditorModel Model, SourceFile Source) Analyze(string text)
    {
        var source = new SourceFile(Path.GetFullPath("bound-generic-arguments.gf"), text);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, error => error.Severity == DiagnosticSeverity.Error);
        return (new EditorModel(snapshot, [source]), source);
    }

    private static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value, Protocol.JsonOptions);
}
