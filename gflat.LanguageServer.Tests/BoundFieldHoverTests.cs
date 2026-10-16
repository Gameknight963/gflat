using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class BoundFieldHoverTests
{
    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    public void GenericFieldUsesShowTheirConcreteTypeAndDeclaration(string kind)
    {
        string text = "namespace Lib { public " + kind + " Box<T>{public T value; public Box(T initial){value=initial;}}} int main(){Lib::Box<int> box=new Lib::Box<int>(1);box.value=42;return box.value;}";
        var source = new SourceFile(Path.GetFullPath("bound-field-hover.gf"), text);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib=true });
        Assert.DoesNotContain(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, [source]);
        var definition = model.Definition(source.Path, text.IndexOf("value;", StringComparison.Ordinal));
        Assert.NotNull(definition);
        for (int position = text.IndexOf("box.value", StringComparison.Ordinal); position >= 0; position = text.IndexOf("box.value", position + 1, StringComparison.Ordinal))
        {
            int field = position + "box.".Length;
            var hover = JsonSerializer.SerializeToElement(model.Hover(source.Path, field), Protocol.JsonOptions);
            Assert.Contains("public int ", hover.GetProperty("contents").GetProperty("value").GetString());
            Assert.DoesNotContain("public T ", hover.GetProperty("contents").GetProperty("value").GetString());
            Assert.Equal(definition, model.Definition(source.Path, field));
        }
    }
}
