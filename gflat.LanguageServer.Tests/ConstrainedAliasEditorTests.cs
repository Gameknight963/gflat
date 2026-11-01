using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class ConstrainedAliasEditorTests
{
    [Fact]
    public void ConstrainedModifierHasKeywordColorAndOrdinaryNamesKeepSymbolColors()
    {
        const string text = "static interface IReader{int Read();}static class Reader:IReader{public static int Read()=>42;}constrained public weak alias Selected:IReader=Reader;int main(){constrained alias Local:IReader=Reader;int constrained=Local::Read();return constrained;}";
        var source = new SourceFile(Path.GetFullPath("constrained-alias-editor.gf"), text);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, [source]);
        var data = JsonSerializer.SerializeToElement(model.SemanticTokens(source.Path), Protocol.JsonOptions)
            .GetProperty("data").EnumerateArray().Select(item => item.GetInt32()).ToArray();
        var kinds = new Dictionary<int, string>();
        int column = 0;
        for (int i = 0; i < data.Length; i += 5)
        {
            Assert.Equal(0, data[i]);
            column += data[i + 1];
            kinds[column] = EditorModel.TokenTypes[data[i + 3]];
        }
        Assert.Equal("modifier", kinds[text.IndexOf("constrained public", StringComparison.Ordinal)]);
        Assert.Equal("modifier", kinds[text.IndexOf("constrained alias", StringComparison.Ordinal)]);
        Assert.Equal("variable", kinds[text.IndexOf("constrained=", StringComparison.Ordinal)]);
        Assert.Equal("variable", kinds[text.LastIndexOf("constrained;", StringComparison.Ordinal)]);
        string hover = JsonSerializer.Serialize(model.Hover(source.Path, text.IndexOf("Selected:", StringComparison.Ordinal)), Protocol.JsonOptions);
        Assert.Contains("constrained", hover);
    }

    [Fact]
    public void AliasNamedConstrainedRemainsATypeName()
    {
        const string text = "constrained alias constrained=int;int main(){constrained value=42;return value;}";
        var source = new SourceFile(Path.GetFullPath("alias-named-constrained.gf"), text);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, [source]);
        int name = text.IndexOf("constrained=", StringComparison.Ordinal);
        Assert.NotNull(model.Definition(source.Path, text.IndexOf("constrained value", StringComparison.Ordinal)));
        Assert.NotNull(model.Hover(source.Path, name));
    }
}
