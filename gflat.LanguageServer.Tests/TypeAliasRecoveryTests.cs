using System.Text.Json;
using gflat.ast;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class TypeAliasRecoveryTests
{
    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    public void UnsupportedTypeAliasesDoNotPreventAnalysisOfLaterBodies(string kind)
    {
        string text = $$"""
            {{kind}} Holder { public alias Slot=Missing; }
            int Read() { int later=7; return later; }
            """;
        var source = new SourceFile(Path.GetFullPath("type-alias-recovery.gf"), text);
        var caller = new SourceFile(Path.GetFullPath("type-alias-caller.gf"), "int main()=>Read();");
        var snapshot = new AnalysisSnapshot([source, caller], new() { NoStdLib=true });
        var error = Assert.Single(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("alias", error.Message, StringComparison.OrdinalIgnoreCase);
        var local = snapshot.Nodes.Select(n => n.Node).OfType<VariableDeclaration>().Single(n => n.Name == "later");
        Assert.Equal("int", TypeChecker.TypeName(snapshot.Checker!.GetType(local)));
        var model = new EditorModel(snapshot, [source, caller]);
        int declaration = text.IndexOf("later=", StringComparison.Ordinal);
        int use = text.LastIndexOf("later", StringComparison.Ordinal);
        var expected = model.Definition(source.Path, declaration);
        Assert.NotNull(expected);
        Assert.Equal(expected, model.Definition(source.Path, use));
        var hover = JsonSerializer.SerializeToElement(model.Hover(source.Path, use), Protocol.JsonOptions);
        Assert.Contains("int later", hover.GetProperty("contents").GetProperty("value").GetString());
        var method = model.Definition(source.Path, text.IndexOf("Read()", StringComparison.Ordinal));
        Assert.NotNull(method);
        Assert.Equal(method, model.Definition(caller.Path, caller.Text.IndexOf("Read()", StringComparison.Ordinal)));
    }
}
