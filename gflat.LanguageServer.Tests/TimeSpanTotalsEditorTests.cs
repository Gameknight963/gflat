using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class TimeSpanTotalsEditorTests
{
    [Fact]
    public void AllApproximateTotalsNavigateActualImmutableNoThrowDoubleProperties()
    {
        const string text = "using std; using std::Time;void Observe(immutable(TimeSpan)* value){double a=value.TotalMilliseconds;double b=value.TotalMinutes;double c=value.TotalHours;double d=value.TotalDays;}";
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "StandardLibrary"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path))).ToList();
        var library = files.Single(file => Path.GetFileName(file.Path) == "TimeSpan.gf");
        var source = new SourceFile(Path.GetFullPath("time-span-totals-editor.gf"), text);
        files.Add(source);
        var snapshot = new AnalysisSnapshot(files, new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, error => error.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, files);

        foreach (string name in new[] { "TotalMilliseconds", "TotalMinutes", "TotalHours", "TotalDays" })
        {
            int use = text.IndexOf(name, StringComparison.Ordinal);
            Assert.Contains(Json(model.Completion(source.Path, use)).GetProperty("items").EnumerateArray(),
                item => item.GetProperty("label").GetString() == name);
            string hover = Json(model.Hover(source.Path, use)).GetProperty("contents").GetProperty("value").GetString()!;
            Assert.Contains("double", hover);
            Assert.Contains(name, hover);
            Assert.Contains("immutable get nothrow", hover);

            string anchor = "public double " + name + " {";
            int start = library.Text.IndexOf(anchor, StringComparison.Ordinal);
            Assert.True(start >= 0);
            var expected = model.Definition(library.Path, start + "public double ".Length);
            Assert.NotNull(expected);
            Assert.Equal(expected, model.Definition(source.Path, use));
        }
    }

    private static JsonElement Json(object? value)
        => JsonSerializer.SerializeToElement(value, Protocol.JsonOptions);
}
