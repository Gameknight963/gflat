using System.Text.Json;
using gflat.ast;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class PrivateFunctionRecoveryTests
{
    [Theory]
    [InlineData("private int Hidden()=>1;", "return Lib::Hidden();")]
    [InlineData("private T Hidden<T>(T value)=>value;", "return Lib::Hidden<int>(1);")]
    [InlineData("private int Hidden()=>1;", "int()* callback=Lib::Hidden; return callback();")]
    public void PrivateFunctionErrorsPreserveHealthyCrossFileBinding(string declaration, string brokenBody)
    {
        var library = new SourceFile(Path.GetFullPath("private-function-library.gf"),
            "namespace Lib; " + declaration + " public long Healthy()=>42;");
        string text = "int Broken(){" + brokenBody + "} long Read(){long marker=Lib::Healthy();return marker;}";
        var consumer = new SourceFile(Path.GetFullPath("private-function-consumer.gf"), text);
        var snapshot = new AnalysisSnapshot([library, consumer], new() { NoStdLib = true });
        Assert.Contains(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Message.Contains("private", StringComparison.Ordinal));
        var marker = snapshot.Nodes.Select(node => node.Node).OfType<VariableDeclaration>().Single(node => node.Name == "marker");
        Assert.Equal("long", TypeChecker.TypeName(snapshot.Checker!.GetType(marker)));
        var model = new EditorModel(snapshot, [library, consumer]);
        var definition = model.Definition(library.Path, library.Text.IndexOf("Healthy", StringComparison.Ordinal));
        Assert.NotNull(definition);
        Assert.Equal(definition, model.Definition(consumer.Path, text.IndexOf("Healthy", StringComparison.Ordinal)));
        var hover = JsonSerializer.SerializeToElement(model.Hover(consumer.Path, text.LastIndexOf("marker", StringComparison.Ordinal)), Protocol.JsonOptions);
        Assert.Contains("long marker", hover.GetProperty("contents").GetProperty("value").GetString());
    }
}
