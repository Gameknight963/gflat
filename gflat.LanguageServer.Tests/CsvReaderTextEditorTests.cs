using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

// Actual standard sources supply both the property and helper.
public sealed class CsvReaderTextEditorTests
{
    [Fact]
    public void HasValueIsAVisibleImmutableNoThrowBooleanPropertyWithItsSourceDefinition()
    {
        const string text="using std; using std::Text; using std::Csv;bool Observe(immutable(CsvReader)* reader) nothrow=>reader.HasValue;";
        var (model,source,files)=Analyze(text);int use=text.IndexOf("HasValue",StringComparison.Ordinal);
        Assert.Single(Json(model.Completion(source.Path,use)).GetProperty("items").EnumerateArray(),item=>item.GetProperty("label").GetString()=="HasValue");
        string hover=Hover(model,source,use);
        Assert.Contains("bool",hover);Assert.Contains("HasValue",hover);Assert.Contains("immutable",hover);Assert.Contains("nothrow",hover);
        Assert.DoesNotContain('$',hover);
        SourceFile library=files.Single(file=>Path.GetFileName(file.Path)=="CsvReader.gf");
        int declaration=library.Text.IndexOf("bool HasValue",StringComparison.Ordinal);Assert.True(declaration>=0);
        Definition(model,source,use,library,declaration+"bool ".Length);
    }

    [Fact]
    public void CopyValuePresentsItsQualifiedStaticReaderAndRefStringArguments()
    {
        const string text="using std; using std::Text; using std::Csv;void Copy(immutable(CsvReader)* reader,ref String output){CsvReaderText::CopyValue(reader,ref output);}";
        var (model,source,files)=Analyze(text);int use=text.IndexOf("CopyValue",StringComparison.Ordinal);
        Assert.Single(Json(model.Completion(source.Path,use)).GetProperty("items").EnumerateArray(),item=>item.GetProperty("label").GetString()=="CopyValue");
        string hover=Hover(model,source,use);
        Assert.Contains("void",hover);Assert.Contains("static",hover);Assert.Contains("CopyValue",hover);Assert.Contains("CsvReader",hover);
        Assert.Matches(@"\bref\s+(?:std::Text::)?String\s+output\b",hover);Assert.DoesNotContain('$',hover);
        SourceFile library=files.Single(file=>Path.GetFileName(file.Path)=="CsvReaderText.gf");
        int declaration=library.Text.IndexOf("CopyValue(",StringComparison.Ordinal);Assert.True(declaration>=0);
        Definition(model,source,use,library,declaration);
        var help=Json(model.SignatureHelp(source.Path,text.LastIndexOf("ref output",StringComparison.Ordinal)+4));
        var signature=Assert.Single(help.GetProperty("signatures").EnumerateArray());
        Assert.Equal(1,help.GetProperty("activeParameter").GetInt32());
        Assert.Equal(2,signature.GetProperty("parameters").GetArrayLength());
        string label=signature.GetProperty("label").GetString()!;
        Assert.Contains("void",label);Assert.Contains("CopyValue",label);Assert.Contains("CsvReader",label);
        Assert.Matches(@"\bref\s+(?:std::Text::)?String\s+output\b",label);Assert.DoesNotContain('$',label);
    }

    private static (EditorModel Model,SourceFile Source,SourceFile[] Files) Analyze(string text)
    {
        var files=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"StandardLibrary"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).ToList();
        var source=new SourceFile(Path.GetFullPath("csv-reader-text-editor.gf"),text);files.Add(source);
        var snapshot=new AnalysisSnapshot(files,new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,error=>error.Severity==DiagnosticSeverity.Error);
        return(new EditorModel(snapshot,files),source,files.ToArray());
    }
    private static JsonElement Json(object? value)=>JsonSerializer.SerializeToElement(value,Protocol.JsonOptions);
    private static string Hover(EditorModel model,SourceFile source,int offset)
        =>Json(model.Hover(source.Path,offset)).GetProperty("contents").GetProperty("value").GetString()!;
    private static void Definition(EditorModel model,SourceFile source,int use,SourceFile library,int declaration)
    {
        var expected=model.Definition(library.Path,declaration);Assert.NotNull(expected);
        Assert.Equal(expected,model.Definition(source.Path,use));
        Assert.Contains(Path.GetFileName(library.Path),JsonSerializer.Serialize(expected,Protocol.JsonOptions));
    }
}
