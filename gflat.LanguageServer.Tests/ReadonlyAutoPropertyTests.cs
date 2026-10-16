using System.Text.Json;
using gflat.ast;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class ReadonlyAutoPropertyTests
{
    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    public void GenericReadonlyResultsKeepPropertyTypingAndNavigationAcrossFiles(string kind)
    {
        string declaration = "namespace Lib { public " + kind + " Box<T>{public readonly(T) Value{get;set;} public Box(T value){Value=value;}}}";
        const string usage = "int main(){Lib::Box<int> box=new Lib::Box<int>(42);return box.Value;}";
        var library = new SourceFile(Path.GetFullPath("readonly-auto-library.gf"), declaration);
        var consumer = new SourceFile(Path.GetFullPath("readonly-auto-consumer.gf"), usage);
        var snapshot = new AnalysisSnapshot([library, consumer], new() { NoStdLib=true });
        Assert.DoesNotContain(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var read = snapshot.Nodes.Select(n => n.Node).OfType<MemberAccessExpression>().Single(n => n.Member == "Value" && n.Span.SourceId == consumer.Id);
        Assert.Equal("readonly(int)", TypeChecker.TypeName(snapshot.Checker!.GetType(read)));
        var model = new EditorModel(snapshot, [library, consumer]);
        var target = model.Definition(library.Path, declaration.IndexOf("Value{", StringComparison.Ordinal));
        Assert.NotNull(target);
        int position = usage.IndexOf("Value", StringComparison.Ordinal);
        Assert.Equal(target, model.Definition(consumer.Path, position));
        var hover = JsonSerializer.SerializeToElement(model.Hover(consumer.Path, position), Protocol.JsonOptions);
        string? value = hover.GetProperty("contents").GetProperty("value").GetString();
        Assert.Contains("readonly(int)", value);
        Assert.DoesNotContain("$", value);
    }
}
