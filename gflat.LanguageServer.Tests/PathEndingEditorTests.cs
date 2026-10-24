using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

// Exercise overload identity through the actual standard-library source.
public sealed class PathEndingEditorTests
{
    [Theory]
    [InlineData("EndsInDirectorySeparator")]
    [InlineData("TrimEndingDirectorySeparator")]
    public void PointerAndSpanCallsSelectDistinctSourceOverloadsWithReadableContracts(string name)
    {
        string pointerResult=name=="EndsInDirectorySeparator"?"bool":"String";
        string spanResult=name=="EndsInDirectorySeparator"?"bool":"Span<readonly(char)>";
        string text="using std;void Probe(){Span<readonly(char)> path=new(\"folder/\",7);"
            +pointerResult+" pointerResult=Paths::"+name+"(\"folder/\",PathStyle::Unix);"
            +spanResult+" spanResult=Paths::"+name+"(path,PathStyle::Unix);}";
        var files=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"StandardLibrary"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).ToList();
        var library=files.Single(file=>Path.GetFileName(file.Path)=="Paths.gf");
        var source=new SourceFile(Path.GetFullPath("path-ending-editor.gf"),text);files.Add(source);
        var snapshot=new AnalysisSnapshot(files,new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,error=>error.Severity==DiagnosticSeverity.Error);
        var model=new EditorModel(snapshot,files);
        int pointerUse=text.IndexOf("Paths::"+name,StringComparison.Ordinal)+"Paths::".Length;
        int spanUse=text.LastIndexOf("Paths::"+name,StringComparison.Ordinal)+"Paths::".Length;
        foreach(var row in new[]{(Use:pointerUse,Span:false,Return:pointerResult),(Use:spanUse,Span:true,Return:spanResult)})
        {
            var items=Json(model.Completion(source.Path,row.Use)).GetProperty("items");
            Assert.Single(items.EnumerateArray(),item=>item.GetProperty("label").GetString()==name);
            string hover=Json(model.Hover(source.Path,row.Use)).GetProperty("contents").GetProperty("value").GetString()!;
            Assert.Contains(name,hover);Assert.DoesNotContain('$',hover);
            Assert.Contains(row.Return,hover);
            if(name=="EndsInDirectorySeparator")Assert.Contains("nothrow",hover);
            else Assert.DoesNotContain("nothrow",hover);
            if(row.Span)Assert.Contains("Span<readonly(char)> path",hover);
            else {Assert.DoesNotContain("Span<",hover);Assert.Contains("path",hover);Assert.Contains('*',hover);}

            int style=text.IndexOf("PathStyle::Unix",row.Use,StringComparison.Ordinal);
            var help=Json(model.SignatureHelp(source.Path,style));
            Assert.Equal(1,help.GetProperty("activeParameter").GetInt32());
            var signatures=help.GetProperty("signatures");Assert.Equal(2,signatures.GetArrayLength());
            var selected=signatures[help.GetProperty("activeSignature").GetInt32()];
            Assert.Equal(2,selected.GetProperty("parameters").GetArrayLength());
            string label=selected.GetProperty("label").GetString()!;
            Assert.Contains(name,label);Assert.Contains("PathStyle",label);Assert.DoesNotContain('$',label);
            // Both overloads have two arguments: receiver input and return identify selection.
            if(row.Span)Assert.Matches(@"(?:std::)?Span<readonly\(char\)>\s+path",label);
            else {Assert.DoesNotContain("Span<",label);Assert.Contains('*',label);}
            const string owner=@"(?:std::)?Paths::";
            if(row.Return=="String")Assert.Matches(@"\b(?:std::)?String\s+"+owner+name,label);
            else if(row.Return=="bool")Assert.Matches(@"\bbool\s+"+owner+name,label);
            else Assert.Matches(@"(?:std::)?Span<readonly\(char\)>\s+"+owner+name,label);
            if(name=="EndsInDirectorySeparator")Assert.Contains("nothrow",label);
            else Assert.DoesNotContain("nothrow",label);

            string declaration="public static "+row.Return+" "+name+"("+(row.Span?"Span<readonly(char)> path":"immutable(char*) path");
            int start=library.Text.IndexOf(declaration,StringComparison.Ordinal);Assert.True(start>=0,declaration);
            int declaredName=start+("public static "+row.Return+" ").Length;
            var expected=model.Definition(library.Path,declaredName);Assert.NotNull(expected);
            Assert.Equal(expected,model.Definition(source.Path,row.Use));
            Assert.Contains("Paths.gf",JsonSerializer.Serialize(expected,Protocol.JsonOptions));
        }
        Assert.NotEqual(model.Definition(source.Path,pointerUse),model.Definition(source.Path,spanUse));
    }

    private static JsonElement Json(object? value)=>JsonSerializer.SerializeToElement(value,Protocol.JsonOptions);
}
