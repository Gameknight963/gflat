using gflat.ast;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class AccessibilityRecoveryTests
{
    [Theory]
    [InlineData("public private class Holder { BODY }")]
    [InlineData("class Holder { public private static int Broken()=>0; BODY }")]
    [InlineData("private extern public int Foreign(int value); class Holder { BODY }")]
    [InlineData("extern private public int Foreign(int value); class Holder { BODY }")]
    public void ConflictingModifiersPreserveTypedBodiesAndCrossFileNavigation(string template)
    {
        string declaration = template.Replace("BODY", "public static int Healthy(){long actual=42;return (int)actual;}", StringComparison.Ordinal);
        const string usage = "int main()=>Holder::Healthy();";
        var library = new SourceFile(Path.GetFullPath("accessibility-library.gf"), declaration);
        var consumer = new SourceFile(Path.GetFullPath("accessibility-consumer.gf"), usage);
        var snapshot = new AnalysisSnapshot([library, consumer], new() { NoStdLib=true });
        var error = Assert.Single(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("accessibility modifier", error.Message);
        var local = snapshot.Nodes.Select(n => n.Node).OfType<VariableDeclaration>().Single(n => n.Name == "actual");
        Assert.Equal("long", TypeChecker.TypeName(snapshot.Checker!.GetType(local)));
        var model = new EditorModel(snapshot, [library, consumer]);
        var target = model.Definition(library.Path, declaration.IndexOf("Healthy()", StringComparison.Ordinal));
        Assert.NotNull(target);
        Assert.Equal(target, model.Definition(consumer.Path, usage.IndexOf("Healthy()", StringComparison.Ordinal)));
        var foreign = snapshot.Nodes.Select(n => n.Node).OfType<ExternDeclaration>().SingleOrDefault();
        if (foreign != null) Assert.Equal(TokenKind.Private, foreign.Accessibility);
    }
}
