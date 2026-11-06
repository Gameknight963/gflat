using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class CodeActionTests
{
    [Theory]
    [InlineData("interface I { int Read(int value); } struct Item : I { }", "Item :", "Implement I")]
    [InlineData("interface I { int @class(int @while); } struct Item : I { }", "Item :", "Implement I")]
    [InlineData("interface I { int @class { get; set; } } struct Item : I { }", "Item :", "Implement I")]
    [InlineData("interface I<T> { T Read(T value); } struct Item : I<int> { }", "Item :", "Implement I")]
    [InlineData("interface I { int Value { get; set; } } struct Item : I { }", "Item :", "Implement I")]
    [InlineData("struct Item { private int value; }", "Item", "Generate constructor")]
    [InlineData("struct Item { private int* value; }", "Item", "Generate constructor")]
    [InlineData("struct Item { private int @class; }", "Item", "Generate constructor")]
    [InlineData("struct @class { private int value; }", "@class", "Generate constructor")]
    [InlineData("namespace Library { public struct Item {} } Item F() => default;", "Item F", "Add using Library")]
    [InlineData("using Library;\n\nnamespace Library { public struct Item {} } int F() => 0;", "int F", "Remove unused using Library")]
    public void ApplyingQuickFixProducesValidSource(string code, string marker, string title)
    {
        var source = new SourceFile(Path.GetFullPath("code-action.gf"), code);
        var support = new SourceFile(Path.GetFullPath("code-action-support.gf"), """
            class Exception { public Exception(readonly(char)* message) {} }
            namespace Memory {
                public void*? Allocate(nuint bytes) => null;
                public void Free(void* memory) nothrow {}
            }
            namespace __gflat_runtime { public void*? Allocate(nuint bytes) => global::Memory::Allocate(bytes); public void Free(void* storage) nothrow => global::Memory::Free(storage); }
            """);
        var snapshot = new AnalysisSnapshot([source, support], new() { NoStdLib = true });
        var model = new EditorModel(snapshot, [source, support]);
        var range = Workspace.ToRange(source.Span(code.IndexOf(marker, StringComparison.Ordinal), 0));
        var action = Assert.Single(model.CodeActions(source.Path, range), a => a.Title == title);
        string changed = code;
        foreach (var edit in action.Edit!.Changes[Workspace.FileUri(source.Path)].OrderByDescending(e => Workspace.Offset(code, JsonSerializer.SerializeToElement(e.Range.Start, Protocol.JsonOptions))))
        {
            int start = Workspace.Offset(code, JsonSerializer.SerializeToElement(edit.Range.Start, Protocol.JsonOptions));
            int end = Workspace.Offset(code, JsonSerializer.SerializeToElement(edit.Range.End, Protocol.JsonOptions));
            changed = changed.Remove(start, end - start).Insert(start, edit.NewText);
        }
        var checkedChange = new AnalysisSnapshot([new SourceFile(source.Path, changed), support], new() { NoStdLib = true });
        Assert.DoesNotContain(checkedChange.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }
}
