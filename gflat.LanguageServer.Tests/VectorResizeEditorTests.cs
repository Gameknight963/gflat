using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class VectorResizeEditorTests
{
    [Fact]
    public void CopyingResizeBindsConcreteFillAndNavigatesToItsExtension()
    {
        const string text = "using std;void Work(){Vector<int> values=new();values.Resize(8,7);nuint count=values.Count;}";
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"StandardLibrary"),"*.gf",SearchOption.AllDirectories)
            .Select(path => new SourceFile(path,File.ReadAllText(path))).ToList();
        var library = files.Single(file => Path.GetFileName(file.Path)=="VectorResize.gf");
        var source = new SourceFile(Path.GetFullPath("vector-resize-editor.gf"),text);
        files.Add(source);
        var snapshot = new AnalysisSnapshot(files,new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,error => error.Severity==DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot,files);

        int anchor = text.IndexOf("values.Resize(8,7)",StringComparison.Ordinal);
        Assert.True(anchor>=0);
        int use = anchor+"values.".Length;
        var items = Json(model.Completion(source.Path,use)).GetProperty("items");
        Assert.Single(items.EnumerateArray(),item => item.GetProperty("label").GetString()=="Resize");
        string hover = Json(model.Hover(source.Path,use)).GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("void",hover);
        Assert.Contains("nuint count",hover);
        Assert.Contains("int fill",hover);
        Assert.DoesNotContain('$',hover);

        const string declaration = "public void Resize<T>(";
        int declarationOffset = library.Text.IndexOf(declaration,StringComparison.Ordinal);
        Assert.True(declarationOffset>=0);
        var expected = model.Definition(library.Path,declarationOffset+"public void ".Length);
        Assert.NotNull(expected);
        Assert.Equal(expected,model.Definition(source.Path,use));

        var help = Json(model.SignatureHelp(source.Path,use+"Resize(8,".Length));
        Assert.Equal(1,help.GetProperty("activeParameter").GetInt32());
        var signature = Assert.Single(help.GetProperty("signatures").EnumerateArray());
        Assert.Equal(2,signature.GetProperty("parameters").GetArrayLength());
        Assert.Contains("nuint count",signature.GetProperty("label").GetString());
        Assert.Contains("int fill",signature.GetProperty("label").GetString());
    }

    private static JsonElement Json(object? value)
        => JsonSerializer.SerializeToElement(value,Protocol.JsonOptions);
}
