using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class StopwatchEditorTests
{
    [Fact]
    public void StaticClockSpecializationRetainsImmutableObserversNoThrowResetAndSourceNavigation()
    {
        const string text = "using std; using std::Time;static class Clock:IMonotonicClock{public static long GetTimestamp()=>0l;public static long GetFrequency()=>3l;}alias Timer=Stopwatch<Clock>;void Probe(){Timer value=new();value.Start();immutable(Timer)* view=&value;long ticks=view.ElapsedTicks;TimeSpan elapsed=view.Elapsed;value.Reset();}";
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "StandardLibrary"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path))).ToList();
        var library = files.Single(file => Path.GetFileName(file.Path) == "Stopwatch.gf");
        var source = new SourceFile(Path.GetFullPath("stopwatch-editor.gf"), text);
        files.Add(source);
        var snapshot = new AnalysisSnapshot(files, new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, error => error.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, files);
        foreach (var row in new[] {
            (Name: "Start", Type: "void", Immutable: false),
            (Name: "Reset", Type: "void", Immutable: false),
            (Name: "ElapsedTicks", Type: "long", Immutable: true),
            (Name: "Elapsed", Type: "TimeSpan", Immutable: true) })
        {
            int use = text.IndexOf("." + row.Name + (row.Type == "void" ? "(" : ";"), StringComparison.Ordinal) + 1;
            Assert.Single(Json(model.Completion(source.Path, use)).GetProperty("items").EnumerateArray(),
                item => item.GetProperty("label").GetString() == row.Name);
            string hover = Json(model.Hover(source.Path, use)).GetProperty("contents").GetProperty("value").GetString()!;
            Assert.Contains(row.Type, hover);
            Assert.Contains(row.Name, hover);
            Assert.DoesNotContain('$', hover);
            if (row.Immutable) Assert.Contains("immutable", hover);
            if (row.Name == "Reset") Assert.Contains("nothrow", hover);
            int declaration = library.Text.IndexOf("public " + row.Type + " " + row.Name, StringComparison.Ordinal);
            Assert.True(declaration >= 0);
            var expected = model.Definition(library.Path, declaration + "public ".Length + row.Type.Length + 1);
            Assert.NotNull(expected);
            Assert.Equal(expected, model.Definition(source.Path, use));
        }
        int argument = text.IndexOf("Reset(", StringComparison.Ordinal) + "Reset(".Length;
        var help = Json(model.SignatureHelp(source.Path, argument));
        var signature = Assert.Single(help.GetProperty("signatures").EnumerateArray());
        Assert.Equal(0, signature.GetProperty("parameters").GetArrayLength());
        Assert.Contains("nothrow", signature.GetProperty("label").GetString()!);
    }

    private static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value, Protocol.JsonOptions);
}
