using System.Text.Json;
using gflat.ast;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class DateTimeEditorTests
{
    [Fact]
    public void CalendarAndClockPropertiesRetainTheirTypesAndFormatExposesItsOutputContract()
    {
        const string text = "using std; using std::Time;void Probe(){DateTime value=new(new Date(2026,10,24),new TimeOfDay(12,30));immutable(DateTime)* view=&value;Date date=view.Date;TimeOfDay time=view.TimeOfDay;char[27] output;nuint written=0;bool success=view.TryFormat(output,27,ref written);}";
        var (model, source, library, _) = Analyze(text);
        foreach (string name in new[] { "Date", "TimeOfDay" })
        {
            int use = text.IndexOf("view." + name, StringComparison.Ordinal) + "view.".Length;
            string hover = Hover(model, source, use);
            Assert.Contains(name, hover);
            Assert.Contains("immutable", hover);
            Assert.Contains("nothrow", hover);
            Assert.DoesNotContain('$', hover);
            int declaration = library.Text.IndexOf("public " + name + " " + name, StringComparison.Ordinal);
            Assert.True(declaration >= 0);
            Definition(model, source, use, library, declaration + "public ".Length + name.Length + 1);
        }
        int call = text.IndexOf("TryFormat", StringComparison.Ordinal);
        Assert.Single(Json(model.Completion(source.Path, call)).GetProperty("items").EnumerateArray(),
            item => item.GetProperty("label").GetString() == "TryFormat");
        string format = Hover(model, source, call);
        Assert.Contains("ref nuint written", format);
        Assert.Contains("nothrow", format);
        Assert.DoesNotContain('$', format);
        int argument = text.IndexOf("ref written", call, StringComparison.Ordinal) + "ref ".Length;
        var help = Json(model.SignatureHelp(source.Path, argument));
        Assert.Equal(2, help.GetProperty("activeParameter").GetInt32());
        Assert.Equal(3, Assert.Single(help.GetProperty("signatures").EnumerateArray()).GetProperty("parameters").GetArrayLength());
    }

    [Fact]
    public void CountedAndTerminatedParsingNavigateToTheirDistinctDeclarations()
    {
        const string text = "using std; using std::Time;void Probe(){DateTime value=new();bool first=DateTime::TryParse(\"2026-10-24T12:30:00\",19,&value);bool second=DateTime::TryParse(\"2026-10-24T12:30:00\",&value);}";
        var (model, source, library, snapshot) = Analyze(text);
        foreach (var row in new[] { (Use: text.IndexOf("TryParse", StringComparison.Ordinal), Arity: 3), (Use: text.LastIndexOf("TryParse", StringComparison.Ordinal), Arity: 2) })
        {
            string hover = Hover(model, source, row.Use);
            Assert.Contains("bool", hover);
            Assert.Contains("nothrow", hover);
            Assert.DoesNotContain('$', hover);
            var method = Assert.Single(snapshot.Nodes.Where(node => node.Span.SourceId == library.Id).Select(node => node.Node).OfType<MethodDeclaration>(),
                method => method.Name == "TryParse" && method.Parameters.Count == row.Arity);
            Definition(model, source, row.Use, library, library.Text.IndexOf("TryParse", method.Span.Start, StringComparison.Ordinal));
            int argument = text.IndexOf("&value", row.Use, StringComparison.Ordinal) + 1;
            var help = Json(model.SignatureHelp(source.Path, argument));
            var signatures = help.GetProperty("signatures");
            Assert.Equal(2, signatures.GetArrayLength());
            Assert.Equal(row.Arity, signatures[help.GetProperty("activeSignature").GetInt32()].GetProperty("parameters").GetArrayLength());
        }
    }

    private static (EditorModel Model, SourceFile Source, SourceFile Library, AnalysisSnapshot Snapshot) Analyze(string text)
    {
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "StandardLibrary"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path))).ToList();
        var library = files.Single(file => Path.GetFileName(file.Path) == "DateTime.gf");
        var source = new SourceFile(Path.GetFullPath("date-time-editor.gf"), text);
        files.Add(source);
        var snapshot = new AnalysisSnapshot(files, new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, error => error.Severity == DiagnosticSeverity.Error);
        return (new EditorModel(snapshot, files), source, library, snapshot);
    }

    private static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value, Protocol.JsonOptions);
    private static string Hover(EditorModel model, SourceFile source, int use)
        => Json(model.Hover(source.Path, use)).GetProperty("contents").GetProperty("value").GetString()!;
    private static void Definition(EditorModel model, SourceFile source, int use, SourceFile library, int declaration)
    {
        var expected = model.Definition(library.Path, declaration);
        Assert.NotNull(expected);
        Assert.Equal(expected, model.Definition(source.Path, use));
        Assert.Contains("DateTime.gf", JsonSerializer.Serialize(expected, Protocol.JsonOptions));
    }
}
