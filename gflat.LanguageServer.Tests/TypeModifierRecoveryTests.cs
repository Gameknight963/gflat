using System.Text.Json;
using gflat.ast;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class TypeModifierRecoveryTests
{
    [Theory]
    [InlineData("readonly")]
    [InlineData("immutable")]
    public void InvalidClassModifierPreservesBodyAndCrossFileMemberNavigation(string modifier)
    {
        string declaration = modifier + " class Holder { public int Read(){int retained=7;return retained;} }";
        const string usage = "int ReadHolder(Holder* holder)=>holder.Read();";
        var library = new SourceFile(Path.GetFullPath("invalid-modifier-library.gf"), declaration);
        var consumer = new SourceFile(Path.GetFullPath("invalid-modifier-consumer.gf"), usage);
        var snapshot = new AnalysisSnapshot([library, consumer], new() { NoStdLib=true });
        var error = Assert.Single(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal(modifier, declaration.Substring(error.Span.Start, error.Span.Length));
        var local = snapshot.Nodes.Select(n => n.Node).OfType<VariableDeclaration>().Single(n => n.Name == "retained");
        Assert.Equal("int", TypeChecker.TypeName(snapshot.Checker!.GetType(local)));
        var model = new EditorModel(snapshot, [library, consumer]);
        var method = model.Definition(library.Path, declaration.IndexOf("Read()", StringComparison.Ordinal));
        Assert.NotNull(method);
        Assert.Equal(method, model.Definition(consumer.Path, usage.IndexOf("Read()", StringComparison.Ordinal)));
        var type = model.Definition(library.Path, declaration.IndexOf("Holder", StringComparison.Ordinal));
        Assert.NotNull(type);
        Assert.Equal(type, model.Definition(consumer.Path, usage.IndexOf("Holder*", StringComparison.Ordinal)));
        var hover = JsonSerializer.SerializeToElement(model.Hover(library.Path, declaration.LastIndexOf("retained", StringComparison.Ordinal)), Protocol.JsonOptions);
        Assert.Contains("int retained", hover.GetProperty("contents").GetProperty("value").GetString());
    }

    [Theory]
    [InlineData("readonly alias Slot=int*;", "readonly")]
    [InlineData("immutable alias Slot=int*;", "immutable")]
    [InlineData("abstract struct Unused{}", "abstract")]
    [InlineData("const class Unused{}", "const")]
    public void InvalidTypeAndAliasModifiersPreserveLaterSemanticBindings(string declaration, string modifier)
    {
        string text = declaration + "\nint Read(){int later=7;return later;}\nint main()=>Read();";
        var source = new SourceFile(Path.GetFullPath("type-modifier-recovery.gf"), text);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib=true });
        var error = Assert.Single(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("'" + modifier + "'", error.Message);
        Assert.Equal(modifier, text.Substring(error.Span.Start, error.Span.Length));
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
