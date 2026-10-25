using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class DateTimeOffsetArithmeticEditorTests
{
    [Fact]
    public void DurationArithmeticUsesTheActualDeclarationsAndQualifiedValueSignatures()
    {
        const string text="using std;void Probe(){DateTimeOffset first=DateTimeOffset::Parse(\"2024-06-01T12:30:00+02:00\");TimeSpan span=TimeSpan::FromMinutes(1l);DateTimeOffset later=first.Add(span);DateTimeOffset earlier=later.Subtract(span);TimeSpan elapsed=later.Difference(first);}";
        var files=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"StandardLibrary"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).ToList();
        var library=files.Single(file=>Path.GetFileName(file.Path)=="DateTimeOffset.gf");
        var source=new SourceFile(Path.GetFullPath("date-time-offset-arithmetic-editor.gf"),text);files.Add(source);
        var snapshot=new AnalysisSnapshot(files,new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,error=>error.Severity==DiagnosticSeverity.Error);
        var model=new EditorModel(snapshot,files);
        foreach(var row in new[]{
            (Name:"Add",Declaration:"public immutable DateTimeOffset Add(TimeSpan value)",Type:"std::DateTimeOffset",Parameter:"std::TimeSpan value"),
            (Name:"Subtract",Declaration:"public immutable DateTimeOffset Subtract(TimeSpan value)",Type:"std::DateTimeOffset",Parameter:"std::TimeSpan value"),
            (Name:"Difference",Declaration:"public immutable TimeSpan Difference(DateTimeOffset other) nothrow",Type:"std::TimeSpan",Parameter:"std::DateTimeOffset other")}){
            int call=text.IndexOf("."+row.Name+"(",StringComparison.Ordinal);Assert.True(call>=0);int use=call+1;
            var items=Json(model.Completion(source.Path,use)).GetProperty("items");
            Assert.Contains(items.EnumerateArray(),item=>item.GetProperty("label").GetString()==row.Name);
            string hover=Json(model.Hover(source.Path,use)).GetProperty("contents").GetProperty("value").GetString()!;
            Assert.Contains(row.Name,hover);Assert.DoesNotContain('$',hover);
            int declarationStart=library.Text.IndexOf(row.Declaration,StringComparison.Ordinal);Assert.True(declarationStart>=0);
            int declaration=declarationStart+row.Declaration.IndexOf(row.Name+"(",StringComparison.Ordinal);
            var expected=model.Definition(library.Path,declaration);Assert.NotNull(expected);
            Assert.Equal(expected,model.Definition(source.Path,use));
            var help=Json(model.SignatureHelp(source.Path,use+row.Name.Length+1));
            var signature=Assert.Single(help.GetProperty("signatures").EnumerateArray());
            Assert.Single(signature.GetProperty("parameters").EnumerateArray());
            Assert.Equal(0,help.GetProperty("activeParameter").GetInt32());
            string label=signature.GetProperty("label").GetString()!;
            Assert.Contains(row.Type+" std::DateTimeOffset::"+row.Name+"(",label);
            Assert.Contains(row.Parameter,label);
        }
    }
    private static JsonElement Json(object? value)=>JsonSerializer.SerializeToElement(value,Protocol.JsonOptions);
}
