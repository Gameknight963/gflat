using System.Text.Json;
using gflat.ast;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class DeclarationQualifierEditorTests
{
    [Theory]
    [InlineData("readonly")]
    [InlineData("immutable")]
    public void DeclarationShorthandKeepsAnalysisNavigationAndQualifiedHover(string qualifier)
    {
        string text = $"struct Box{{public {qualifier} int* data;public Box(int* p){{data=p;}}}} " +
            $"int main(){{int value=1;{qualifier} int* pointer=&value;Box box=new(&value);return *pointer;}}";
        var file = new SourceFile(Path.GetFullPath("declaration-qualifiers.gf"), text);
        var snapshot = new AnalysisSnapshot([file], new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var local = snapshot.Nodes.Select(n => n.Node).OfType<VariableDeclaration>().Single(n => n.Name == "pointer");
        var type = snapshot.Checker!.GetType(local);
        Assert.True(type.IsReadOnlyValue);
        Assert.Equal(qualifier == "immutable", type.IsImmutableValue);
        var model = new EditorModel(snapshot, [file]);
        int use = text.LastIndexOf("pointer", StringComparison.Ordinal);
        Assert.NotNull(model.Definition(file.Path, use));
        var hover = JsonSerializer.SerializeToElement(model.Hover(file.Path, use), Protocol.JsonOptions);
        string signature = hover.GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains(qualifier, signature);
        Assert.Contains("pointer", signature);
    }
}
