using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class JsonWriterIndentationEditorTests
{
    [Fact]
    public void ActualConstructorSignatureShowsTheOptionalIndentationFlag()
    {
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"StandardLibrary"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).ToList();
        string text="""
            using std; using std::Text; using std::Json;int main(){String text=new();JsonWriter compact=new(&text);
                JsonWriter pretty=new JsonWriter(&text,true);return 0;}
            """;
        var source=new SourceFile(Path.GetFullPath("json-indent-usage.gf"),text);sources.Add(source);
        var snapshot=new AnalysisSnapshot(sources,new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,d=>d.Severity==DiagnosticSeverity.Error);
        var model=new EditorModel(snapshot,sources);
        int offset=text.IndexOf("&text,true",StringComparison.Ordinal)+"&text,".Length;
        var help=JsonSerializer.SerializeToElement(model.SignatureHelp(source.Path,offset,visualStudio:true),Protocol.JsonOptions);
        var signature=help.GetProperty("signatures")[0];
        Assert.Contains("bool indented = false",signature.GetProperty("label").GetString());
        Assert.Equal("bool indented = false",signature.GetProperty("parameters")[1].GetProperty("label").GetString());
    }
}
