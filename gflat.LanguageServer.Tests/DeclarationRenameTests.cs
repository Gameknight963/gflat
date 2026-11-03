using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class DeclarationRenameTests
{
    [Theory]
    [InlineData("struct |Item { public Item() {} } int F() { Item value = new Item(); return 0; }", 4)]
    [InlineData("struct |Item { public Item() {} ~Item() {} } int F() { Item value = new Item(); return 0; }", 5)]
    [InlineData("struct Item { public int |Value; } int F() { Item value = default; return value.Value; }", 2)]
    [InlineData("struct Item { public int |Value { get => 42; } } int F() { Item value = default; return value.Value; }", 2)]
    [InlineData("int |Read(int value) => value; int F() => Read(4);", 2)]
    [InlineData("alias |Number = int; Number F(Number value) => value;", 3)]
    [InlineData("namespace |Example { public int Read() => 1; } int F() => Example::Read();", 2)]
    [InlineData("interface I { int |Read(); } class Base : I { public virtual int Read() => 1; } class Child : Base { public override int Read() => 2; } int F(I* value) => value.Read();", 4)]
    public void ApplyingDeclarationRenameProducesValidCode(string marked, int expected)
    {
        int at = marked.IndexOf('|');
        var source = new SourceFile(Path.GetFullPath("declaration-rename.gf"), marked.Remove(at, 1));
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, [source]);
        var edits = model.Rename(source.Path, at, "Renamed");
        Assert.Equal(expected, edits.Length);
        string changed = source.Text;
        foreach (var edit in edits.OrderByDescending(e => Workspace.Offset(source.Text, JsonSerializer.SerializeToElement(e.Range.Start, Protocol.JsonOptions))))
        {
            int start = Workspace.Offset(source.Text, JsonSerializer.SerializeToElement(edit.Range.Start, Protocol.JsonOptions));
            int end = Workspace.Offset(source.Text, JsonSerializer.SerializeToElement(edit.Range.End, Protocol.JsonOptions));
            changed = changed.Remove(start, end - start).Insert(start, edit.NewText);
        }
        Assert.DoesNotContain(new AnalysisSnapshot([new SourceFile(source.Path, changed)], new() { NoStdLib = true }).Diagnostics,
            d => d.Severity == DiagnosticSeverity.Error);
    }
}
