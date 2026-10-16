using System.Text.Json;
using gflat.ast;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class FieldDeclarationRecoveryTests
{
    [Theory]
    [InlineData("class", "virtual int broken;")]
    [InlineData("struct", "virtual int broken;")]
    [InlineData("class", "override int broken;")]
    [InlineData("struct", "abstract int broken;")]
    [InlineData("class", "int broken<T>;")]
    [InlineData("struct", "int broken<T>; ")]
    public void InvalidFieldsPreserveLaterMethodBodiesAndCallNavigation(string kind, string invalid)
    {
        string text = $$"""
            {{kind}} Holder {
                public {{invalid}}
                public int Read() { int later=7; return later; }
            }
            int main() { Holder holder=new Holder(); return holder.Read(); }
            """;
        var source = new SourceFile(Path.GetFullPath("field-declaration-recovery.gf"), text);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib=true });
        Assert.Single(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var local = snapshot.Nodes.Select(n => n.Node).OfType<VariableDeclaration>().Single(n => n.Name == "later");
        Assert.Equal("int", TypeChecker.TypeName(snapshot.Checker!.GetType(local)));
        var model = new EditorModel(snapshot, [source]);
        var method = model.Definition(source.Path, text.IndexOf("Read()", StringComparison.Ordinal));
        Assert.NotNull(method);
        Assert.Equal(method, model.Definition(source.Path, text.LastIndexOf("Read()", StringComparison.Ordinal)));
        var hover = JsonSerializer.SerializeToElement(model.Hover(source.Path, text.LastIndexOf("later", StringComparison.Ordinal)), Protocol.JsonOptions);
        Assert.Contains("int later", hover.GetProperty("contents").GetProperty("value").GetString());
    }
}
