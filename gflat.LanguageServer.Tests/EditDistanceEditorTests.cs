using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class EditDistanceEditorTests
{
    [Fact]
    public void CountedByteKernelNavigatesAndShowsCallerScratchAndRefOutput()
    {
        const string text = """
            using std; using std::Memory; using std::Queries; using std::Collections;
            void Observe() {
                Span<readonly(char)> first = new("ab", 2);
                Span<readonly(char)> second = new("ba", 2);
                nuint[3] storage; Span<nuint> scratch = new(storage, 3);
                nuint distance = 99;
                bool computed = EditDistance::TryCompute(first, second, scratch, ref distance);
            }
            """;
        string root = Path.Combine(AppContext.BaseDirectory, "StandardLibrary", "core");
        string[] names = ["EditDistance.gf", "Memory/Span.gf", "Queries/Enumerable.gf", "Collections/IEnumerable.gf"];
        var files = names.Select(name => Path.Combine(root, name))
            .Select(path => new SourceFile(path, File.ReadAllText(path))).ToList();
        var source = new SourceFile(Path.GetFullPath("edit-distance-editor.gf"), text);
        files.Add(source);
        SourceFile library = files.Single(file => Path.GetFileName(file.Path) == "EditDistance.gf");
        var snapshot = new AnalysisSnapshot(files);
        Assert.DoesNotContain(snapshot.Diagnostics, error => error.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, files);
        int call = text.IndexOf("TryCompute(", StringComparison.Ordinal);
        Assert.True(call >= 0);
        Assert.Single(Json(model.Completion(source.Path, call)).GetProperty("items").EnumerateArray(),
            item => item.GetProperty("label").GetString() == "TryCompute");
        string hover = Json(model.Hover(source.Path, call)).GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("TryCompute", hover);
        Assert.Contains("bool", hover);
        Assert.Contains("nothrow", hover);
        Assert.DoesNotContain('$', hover);
        int declaration = library.Text.IndexOf("public static bool TryCompute(", StringComparison.Ordinal);
        Assert.True(declaration >= 0);
        declaration += "public static bool ".Length;
        var expected = model.Definition(library.Path, declaration);
        Assert.NotNull(expected);
        Assert.Equal(expected, model.Definition(source.Path, call));
        int output = text.IndexOf("ref distance", call, StringComparison.Ordinal);
        Assert.True(output >= 0);
        var help = Json(model.SignatureHelp(source.Path, output + "ref ".Length));
        Assert.Equal(3, help.GetProperty("activeParameter").GetInt32());
        var signatures = help.GetProperty("signatures");
        Assert.Equal(1, signatures.GetArrayLength());
        Assert.Equal(4, signatures[0].GetProperty("parameters").GetArrayLength());
        string label = signatures[0].GetProperty("label").GetString()!;
        Assert.Contains("Span<readonly(char)>", label.Replace(" ", ""));
        Assert.Contains("Span<nuint>", label.Replace(" ", ""));
        Assert.Contains("ref nuint distance", label);
        Assert.DoesNotContain('$', label);
    }

    private static JsonElement Json(object? value)
        => JsonSerializer.SerializeToElement(value, Protocol.JsonOptions);
}
