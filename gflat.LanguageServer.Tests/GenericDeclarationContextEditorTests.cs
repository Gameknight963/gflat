using gflat.ast;
using gflat.diagnostics;
using gflat.LanguageServer;

namespace gflat.LanguageServer.Tests;

public sealed class GenericDeclarationContextEditorTests
{
    [Fact]
    public void LocalEnumSourceNamesAndScopesRemainUsable()
    {
        const string text = "int First() { enum Color { Red = 1 } Color first = Color::Red; return (int)first; } int Second() { enum Color { Red = 2 } Color second = Color::Red; return (int)second; }";
        var source = new SourceFile(Path.GetFullPath("local-enums.gf"), text);
        var snapshot = new AnalysisSnapshot([source]);
        Assert.DoesNotContain(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, [source]);
        int first = text.IndexOf("Color first", StringComparison.Ordinal);
        int second = text.IndexOf("Color second", StringComparison.Ordinal);
        Assert.NotNull(model.Hover(source.Path, first));
        Assert.NotNull(model.Definition(source.Path, first));
        Assert.NotEqual(model.Definition(source.Path, first), model.Definition(source.Path, second));
        Assert.Equal(3, model.References(source.Path, first, true).Length);
    }

    [Fact]
    public void CallerLocalAndGenericFieldKeepSeparateEditorBindings()
    {
        const string text = "struct Box<T> { public T* data; public Box(T* storage) { data = storage; } public T Read() => *data; } int main() { int[1] data = [42]; Box<int> box = new Box<int>(data); box.data = data; return box.Read(); }";
        var source = new SourceFile(Path.GetFullPath("generic-context.gf"), text);
        var snapshot = new AnalysisSnapshot([source]);
        Assert.DoesNotContain(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, [source]);
        int local = text.LastIndexOf("= data", StringComparison.Ordinal) + 2;
        int field = text.IndexOf("*data", StringComparison.Ordinal) + 1;
        Assert.NotNull(model.Hover(source.Path, local));
        Assert.NotNull(model.Hover(source.Path, field));
        Assert.NotEqual(model.Definition(source.Path, local), model.Definition(source.Path, field));
        Assert.Equal(3, model.References(source.Path, local, true).Length);
        Assert.Equal(4, model.References(source.Path, field, true).Length);
    }

    [Fact]
    public void FailedSpecializationRestoresCallerAliasAndLocalServices()
    {
        const string text = "T Broken<T>() => missing; int main() { alias Number = int; Number local = 42; Broken<int>(); Number later = local; return later; }";
        var source = new SourceFile(Path.GetFullPath("generic-recovery.gf"), text);
        var snapshot = new AnalysisSnapshot([source]);
        Assert.Contains(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var later = snapshot.Nodes.Select(n => n.Node).OfType<VariableDeclaration>().Single(n => n.Name == "later");
        Assert.Equal("int", TypeChecker.TypeName(snapshot.Checker!.GetType(later)));
        var model = new EditorModel(snapshot, [source]);
        int offset = text.LastIndexOf("local", StringComparison.Ordinal);
        Assert.NotNull(model.Hover(source.Path, offset));
        Assert.NotNull(model.Definition(source.Path, offset));
        Assert.Equal(2, model.References(source.Path, offset, true).Length);
    }
}
