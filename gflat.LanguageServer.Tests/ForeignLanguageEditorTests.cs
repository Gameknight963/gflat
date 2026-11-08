using System.Text.Json;

namespace gflat.LanguageServer.Tests;

public sealed class ForeignLanguageEditorTests
{
    [Fact]
    public void WorkspaceReusesForeignAnalysisAndInvalidatesOnNativeHeaderContentChanges()
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat_foreign_editor_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string header = Path.Combine(directory, "api.h");
            File.WriteAllText(header, "static int native() { return 1; }");
            DateTime written = File.GetLastWriteTimeUtc(header);
            var source = new SourceFile(Path.Combine(directory, "main.gf"), "#lang cpp\n#include \"api.h\"\n#lang gflat\nint main() { return native(); }");
            File.WriteAllText(source.Path, source.Text);
            var cache = new WorkspaceAnalysisCache();
            var options = new CompilationOptions { NoStdLib = true };
            var first = cache.Get([source], options).Snapshot;
            Assert.Empty(first.Diagnostics);
            Assert.Single(first.ForeignDependencies);
            Assert.DoesNotContain(first.PreprocessorDependencies, path => path.EndsWith("clang.exe", StringComparison.OrdinalIgnoreCase));
            Assert.Same(first, cache.Get([source], options).Snapshot);
            File.WriteAllText(header, "static int native() { return 2; }");
            File.SetLastWriteTimeUtc(header, written);
            var changed = cache.Get([source], options).Snapshot;
            Assert.NotSame(first, changed);
            Assert.Empty(changed.Diagnostics);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void ForeignCodeHasNoGflatHoverCompletionOrSemanticClassifications()
    {
        const string text = "#lang cpp\nint native() { return 42; }\n#lang gflat\nint Read(int value) { return value; }\n";
        var source = new SourceFile(Path.GetFullPath("foreign-editor.gf"), text);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true }, checkTypes: false);
        Assert.Empty(snapshot.Diagnostics);
        var model = new EditorModel(snapshot, [source]);
        int offset = text.IndexOf("native", StringComparison.Ordinal);
        Assert.Null(model.Hover(source.Path, offset));
        Assert.Null(model.Definition(source.Path, offset));
        var completion = JsonSerializer.SerializeToElement(model.Completion(source.Path, offset));
        Assert.Empty(completion.GetProperty("items").EnumerateArray());
        var data = JsonSerializer.SerializeToElement(model.SemanticTokens(source.Path)).GetProperty("data").EnumerateArray().Select(value => value.GetInt32()).ToArray();
        int line = 0;
        for (int index = 0; index < data.Length; index += 5)
        {
            line += data[index];
            Assert.NotEqual(1, line);
        }
        Assert.NotNull(model.Hover(source.Path, text.LastIndexOf("value", StringComparison.Ordinal)));
    }

    [Fact]
    public void FormattingPreservesForeignTextExactlyAndFormatsGflatAfterward()
    {
        const string foreign = "#lang cpp\nint  native( ){return  42;}\n#define OPEN {\n";
        const string normal = "#lang gflat\nint Read(){return 1;}\n";
        var source = new SourceFile(Path.GetFullPath("foreign-format.gf"), foreign + normal);
        var edits = SourceFormatter.Format(source, new());
        Assert.NotEmpty(edits);
        Assert.All(edits, edit => Assert.True(edit.Range.Start.Line >= 3));
    }

    [Fact]
    public void ImportedFreeFunctionsHaveGflatCallSiteHovers()
    {
        const string text = "#lang cpp\nint native(int x) { return x + 1; }\n#lang gflat\nint main() { return native(2); }\n";
        var source = new SourceFile(Path.GetFullPath("foreign-call.gf"), text);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
        Assert.Empty(snapshot.Diagnostics);
        var model = new EditorModel(snapshot, [source]);
        var hover = model.Hover(source.Path, text.LastIndexOf("native", StringComparison.Ordinal));
        Assert.NotNull(hover);
        Assert.Contains("native", JsonSerializer.Serialize(hover));
    }
}
