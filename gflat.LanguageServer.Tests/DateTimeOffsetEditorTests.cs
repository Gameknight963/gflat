using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class DateTimeOffsetEditorTests
{
    [Fact]
    public void CivilOffsetConstructionParsingAndConversionExposeTheirActualValueSignatures()
    {
        const string text="using Standard; using Standard::Time;void Probe(){DateTime wall=DateTime::Parse(\"2024-06-01T12:30:00\");TimeSpan offset=TimeSpan::FromHours(2l);DateTimeOffset first=new DateTimeOffset(wall,offset);DateTimeOffset parsed=DateTimeOffset::Parse(\"2024-06-01T12:30:00+02:00\");DateTimeOffset changed=first.ToOffset(TimeSpan::FromTicks(0l));DateTime utc=parsed.UtcDateTime;TimeSpan saved=parsed.Offset;}";
        var files=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"StandardLibrary"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).ToList();
        var library=files.Single(file=>Path.GetFileName(file.Path)=="DateTimeOffset.gf");
        var source=new SourceFile(Path.GetFullPath("date-time-offset-editor.gf"),text);files.Add(source);
        var snapshot=new AnalysisSnapshot(files,new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,error=>error.Severity==DiagnosticSeverity.Error);
        var model=new EditorModel(snapshot,files);
        foreach(var row in new[]{
            (Marker:"DateTimeOffset::Parse",Name:"Parse",Declaration:"public static DateTimeOffset Parse(readonly(char)* text)",Type:"DateTimeOffset"),
            (Marker:"first.ToOffset",Name:"ToOffset",Declaration:"public immutable DateTimeOffset ToOffset(",Type:"DateTimeOffset"),
            (Marker:"parsed.UtcDateTime",Name:"UtcDateTime",Declaration:"public Standard::Time::DateTime UtcDateTime",Type:"DateTime"),
            (Marker:"parsed.Offset",Name:"Offset",Declaration:"public TimeSpan Offset",Type:"TimeSpan")}){
            int marker=text.IndexOf(row.Marker,StringComparison.Ordinal);Assert.True(marker>=0);
            int use=marker+row.Marker.LastIndexOf(row.Name,StringComparison.Ordinal);
            var items=Json(model.Completion(source.Path,use)).GetProperty("items");
            Assert.Contains(items.EnumerateArray(),item=>item.GetProperty("label").GetString()==row.Name);
            string hover=Json(model.Hover(source.Path,use)).GetProperty("contents").GetProperty("value").GetString()!;
            Assert.Contains(row.Type,hover);Assert.Contains(row.Name,hover);Assert.DoesNotContain('$',hover);
            int declarationStart=library.Text.IndexOf(row.Declaration,StringComparison.Ordinal);Assert.True(declarationStart>=0);
            int declaration=declarationStart+row.Declaration.LastIndexOf(row.Name,StringComparison.Ordinal);
            var expected=model.Definition(library.Path,declaration);Assert.NotNull(expected);
            Assert.Equal(expected,model.Definition(source.Path,use));
        }
        int constructor=text.IndexOf("new DateTimeOffset(",StringComparison.Ordinal)+"new DateTimeOffset(wall,".Length;
        var constructorHelp=Json(model.SignatureHelp(source.Path,constructor));
        Assert.Equal(1,constructorHelp.GetProperty("activeParameter").GetInt32());
        var selected=constructorHelp.GetProperty("signatures")[constructorHelp.GetProperty("activeSignature").GetInt32()];
        Assert.Equal(2,selected.GetProperty("parameters").GetArrayLength());
        Assert.Contains("Standard::Time::DateTime value",selected.GetProperty("label").GetString());
        Assert.Contains("Standard::Time::TimeSpan offset",selected.GetProperty("label").GetString());
        int parse=text.IndexOf("DateTimeOffset::Parse(",StringComparison.Ordinal)+"DateTimeOffset::Parse(".Length;
        var parseHelp=Json(model.SignatureHelp(source.Path,parse));
        Assert.Equal(2,parseHelp.GetProperty("signatures").GetArrayLength());
        var parseSignature=parseHelp.GetProperty("signatures")[parseHelp.GetProperty("activeSignature").GetInt32()];
        Assert.Single(parseSignature.GetProperty("parameters").EnumerateArray());
        int conversion=text.IndexOf("ToOffset(",StringComparison.Ordinal)+"ToOffset(".Length;
        var conversionHelp=Json(model.SignatureHelp(source.Path,conversion));
        var signature=Assert.Single(conversionHelp.GetProperty("signatures").EnumerateArray());
        Assert.Single(signature.GetProperty("parameters").EnumerateArray());
        Assert.Contains("Standard::Time::TimeSpan offset",signature.GetProperty("label").GetString());
    }
    private static JsonElement Json(object? value)=>JsonSerializer.SerializeToElement(value,Protocol.JsonOptions);
}
