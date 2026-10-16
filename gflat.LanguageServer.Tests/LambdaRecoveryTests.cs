using gflat.ast;
using gflat.diagnostics;
using gflat.LanguageServer;

namespace gflat.LanguageServer.Tests;

public sealed class LambdaRecoveryTests
{
    [Theory]
    [InlineData("static (Missing argument) => 1")]
    [InlineData("(Missing argument) => 1")]
    public void InvalidLambdaDoesNotTurnLaterLocalsIntoCaptures(string expression)
    {
        string text = "int main() { int value = 42; int(int)* broken = " + expression +
            "; int later = value; return later; }";
        var source = new SourceFile(Path.GetFullPath("lambda-recovery.gf"), text);
        var snapshot = new AnalysisSnapshot([source]);
        Assert.Contains(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(snapshot.Diagnostics, d => d.Message.Contains("outer local variable", StringComparison.Ordinal));
        var later = snapshot.Nodes.Select(n => n.Node).OfType<VariableDeclaration>().Single(n => n.Name == "later");
        Assert.Equal("int", TypeChecker.TypeName(snapshot.Checker!.GetType(later)));
        var model = new EditorModel(snapshot, [source]);
        int use = text.LastIndexOf("value", StringComparison.Ordinal);
        Assert.NotNull(model.Hover(source.Path, use));
        Assert.NotNull(model.Definition(source.Path, use));
    }
}
