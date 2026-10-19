using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class InferredLocalEditorTests
{
    [Fact]
    public void InferredLocalsHaveConcreteHoversAndVarIsAContextualKeyword()
    {
        const string text = "struct Box<T>{public T Value;public Box(T value){Value=value;}} int main(){var box=new Box<int>(42);return box.Value;}";
        var source = new SourceFile(Path.GetFullPath("inferred-local.gf"), text);
        var snapshot = new AnalysisSnapshot([source]);
        Assert.DoesNotContain(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, [source]);
        int offset = text.LastIndexOf("box.Value", StringComparison.Ordinal);
        string hover = JsonSerializer.SerializeToElement(model.Hover(source.Path, offset), Protocol.JsonOptions)
            .GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("Box<int>", hover);
        Assert.NotNull(model.Definition(source.Path, offset));
        int[] data = JsonSerializer.SerializeToElement(model.SemanticTokens(source.Path), Protocol.JsonOptions)
            .GetProperty("data").EnumerateArray().Select(value => value.GetInt32()).ToArray();
        int line = 0, column = 0;
        bool found = false;
        for (int i = 0; i < data.Length; i += 5)
        {
            line += data[i];
            column = data[i] == 0 ? column + data[i + 1] : data[i + 1];
            if (line == 0 && column == text.IndexOf("var box", StringComparison.Ordinal))
            {
                Assert.Equal("modifier", EditorModel.TokenTypes[data[i + 3]]);
                found = true;
            }
        }
        Assert.True(found);
    }
}
