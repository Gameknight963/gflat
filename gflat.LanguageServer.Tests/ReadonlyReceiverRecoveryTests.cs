using System.Text.Json;
using gflat.ast;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class ReadonlyReceiverRecoveryTests
{
    [Theory]
    [InlineData("class", "public static immutable int Broken()=>0;")]
    [InlineData("struct", "public static immutable int Broken()=>0;")]
    [InlineData("static class", "public static immutable int Broken()=>0;")]
    [InlineData("static class", "public static int Broken{immutable get=>0;}")]
    public void ReceiverErrorsPreserveHealthySiblingBodiesAndCrossFileCalls(string kind, string invalid)
    {
        string declaration = kind + " Holder{" + invalid + " public static int Healthy(){long actual=42;return (int)actual;}}";
        const string usage = "int main()=>Holder::Healthy();";
        var library = new SourceFile(Path.GetFullPath("readonly-receiver-library.gf"), declaration);
        var consumer = new SourceFile(Path.GetFullPath("readonly-receiver-consumer.gf"), usage);
        var snapshot = new AnalysisSnapshot([library, consumer], new() { NoStdLib=true });
        var error = Assert.Single(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal("Readonly methods require an instance receiver", error.Message);
        var local = snapshot.Nodes.Select(n => n.Node).OfType<VariableDeclaration>().Single(n => n.Name == "actual");
        Assert.Equal("long", TypeChecker.TypeName(snapshot.Checker!.GetType(local)));
        var model = new EditorModel(snapshot, [library, consumer]);
        var method = model.Definition(library.Path, declaration.IndexOf("Healthy()", StringComparison.Ordinal));
        Assert.NotNull(method);
        Assert.Equal(method, model.Definition(consumer.Path, usage.IndexOf("Healthy()", StringComparison.Ordinal)));
        var hover = JsonSerializer.SerializeToElement(model.Hover(library.Path, declaration.LastIndexOf("actual", StringComparison.Ordinal)), Protocol.JsonOptions);
        Assert.Contains("long actual", hover.GetProperty("contents").GetProperty("value").GetString());
    }
}
