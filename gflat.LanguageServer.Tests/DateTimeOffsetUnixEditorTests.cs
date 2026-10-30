using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class DateTimeOffsetUnixEditorTests
{
    [Fact]
    public void UnixFactoriesAndObserversNavigateTheirActualSignatures()
    {
        const string text="using Standard; using Standard::Time;void Probe(){DateTimeOffset sec=DateTimeOffset::FromUnixTimeSeconds(0l);DateTimeOffset milli=DateTimeOffset::FromUnixTimeMilliseconds(-1l);long seconds=sec.ToUnixTimeSeconds();long milliseconds=milli.ToUnixTimeMilliseconds();}";
        var files=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"StandardLibrary"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).ToList();
        var library=files.Single(file=>Path.GetFileName(file.Path)=="DateTimeOffset.gf");
        var source=new SourceFile(Path.GetFullPath("date-time-offset-unix-editor.gf"),text);files.Add(source);
        var snapshot=new AnalysisSnapshot(files,new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,error=>error.Severity==DiagnosticSeverity.Error);
        var model=new EditorModel(snapshot,files);
        foreach(var row in new[]{
            (Name:"FromUnixTimeSeconds",Arity:1,Declaration:"public static DateTimeOffset FromUnixTimeSeconds(",Type:"Standard::Time::DateTimeOffset",Parameter:"long seconds"),
            (Name:"FromUnixTimeMilliseconds",Arity:1,Declaration:"public static DateTimeOffset FromUnixTimeMilliseconds(",Type:"Standard::Time::DateTimeOffset",Parameter:"long milliseconds"),
            (Name:"ToUnixTimeSeconds",Arity:0,Declaration:"public immutable long ToUnixTimeSeconds(",Type:"long",Parameter:""),
            (Name:"ToUnixTimeMilliseconds",Arity:0,Declaration:"public immutable long ToUnixTimeMilliseconds(",Type:"long",Parameter:"")}){
            int use=text.IndexOf(row.Name+"(",StringComparison.Ordinal);Assert.True(use>=0);
            var items=Json(model.Completion(source.Path,use)).GetProperty("items");
            Assert.Contains(items.EnumerateArray(),item=>item.GetProperty("label").GetString()==row.Name);
            string hover=Json(model.Hover(source.Path,use)).GetProperty("contents").GetProperty("value").GetString()!;
            Assert.Contains(row.Name,hover);Assert.DoesNotContain('$',hover);
            int start=library.Text.IndexOf(row.Declaration,StringComparison.Ordinal);Assert.True(start>=0);
            int declaration=start+row.Declaration.IndexOf(row.Name,StringComparison.Ordinal);
            var expected=model.Definition(library.Path,declaration);Assert.NotNull(expected);
            Assert.Equal(expected,model.Definition(source.Path,use));
            var help=Json(model.SignatureHelp(source.Path,use+row.Name.Length+1));
            var signature=Assert.Single(help.GetProperty("signatures").EnumerateArray());
            Assert.Equal(row.Arity,signature.GetProperty("parameters").GetArrayLength());
            string label=signature.GetProperty("label").GetString()!;
            Assert.Contains(row.Type,label);Assert.Contains(row.Name,label);
            if(row.Arity==1)Assert.Contains(row.Parameter,label);
            else Assert.Contains("long",hover);
        }
    }
    private static JsonElement Json(object? value)=>JsonSerializer.SerializeToElement(value,Protocol.JsonOptions);
}
