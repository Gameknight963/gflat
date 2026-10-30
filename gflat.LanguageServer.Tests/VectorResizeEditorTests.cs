using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class VectorResizeEditorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResizeOverloadsBindTheirConcreteArgumentsAndNavigateToTheirOwnExtensions(bool withFill)
    {
        string call = withFill ? "values.Resize(8,7)" : "values.Resize(8)";
        string text = "using Standard; using Standard::Collections;void Work(){Vector<int> values=new();" + call + ";nuint count=values.Count;}";
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"StandardLibrary"),"*.gf",SearchOption.AllDirectories)
            .Select(path => new SourceFile(path,File.ReadAllText(path))).ToList();
        var library = files.Single(file => Path.GetFileName(file.Path)=="vector_resize.gf");
        var source = new SourceFile(Path.GetFullPath("vector-resize-editor.gf"),text);
        files.Add(source);
        var snapshot = new AnalysisSnapshot(files,new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,error => error.Severity==DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot,files);

        int anchor = text.IndexOf(call,StringComparison.Ordinal);
        Assert.True(anchor>=0);
        int use = anchor+"values.".Length;
        var items = Json(model.Completion(source.Path,use)).GetProperty("items");
        var completion = Assert.Single(items.EnumerateArray(),item => item.GetProperty("label").GetString()=="Resize");
        string detail = completion.GetProperty("detail").GetString()!;
        Assert.Contains("void",detail);
        Assert.Contains("nuint count",detail);
        Assert.DoesNotContain('$',detail);
        string hover = Json(model.Hover(source.Path,use)).GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("void",hover);
        Assert.Contains("nuint count",hover);
        if (withFill) Assert.Contains("int fill",hover);
        else Assert.DoesNotContain("fill",hover);
        Assert.DoesNotContain('$',hover);

        string declaration = "public void Resize(nuint count" + (withFill ? ", T fill)" : ")");
        int declarationOffset = library.Text.IndexOf(declaration,StringComparison.Ordinal);
        Assert.True(declarationOffset>=0);
        int nameOffset = declarationOffset+"public void ".Length;
        // Derive the expected name range directly from the chosen source signature.
        int line = library.Text[..nameOffset].Count(character => character=='\n');
        int column = nameOffset-library.Text.LastIndexOf('\n',nameOffset)-1;
        var definition = Json(model.Definition(source.Path,use));
        Assert.Equal(library.Path,new Uri(definition.GetProperty("uri").GetString()!).LocalPath);
        var range = definition.GetProperty("range");
        Assert.Equal(line,range.GetProperty("start").GetProperty("line").GetInt32());
        Assert.Equal(column,range.GetProperty("start").GetProperty("character").GetInt32());
        Assert.Equal(line,range.GetProperty("end").GetProperty("line").GetInt32());
        Assert.Equal(column+"Resize".Length,range.GetProperty("end").GetProperty("character").GetInt32());

        var help = Json(model.SignatureHelp(source.Path,use+(withFill ? "Resize(8,".Length : "Resize(".Length)));
        Assert.Equal(withFill ? 1 : 0,help.GetProperty("activeParameter").GetInt32());
        var signatures = help.GetProperty("signatures");
        Assert.Contains(signatures.EnumerateArray(),signature => signature.GetProperty("parameters").GetArrayLength()==1);
        Assert.Contains(signatures.EnumerateArray(),signature => signature.GetProperty("parameters").GetArrayLength()==2);
        var signature = signatures[help.GetProperty("activeSignature").GetInt32()];
        Assert.Equal(withFill ? 2 : 1,signature.GetProperty("parameters").GetArrayLength());
        Assert.Contains("nuint count",signature.GetProperty("label").GetString());
        if (withFill) Assert.Contains("int fill",signature.GetProperty("label").GetString());
        else Assert.DoesNotContain("fill",signature.GetProperty("label").GetString());
    }

    private static JsonElement Json(object? value)
        => JsonSerializer.SerializeToElement(value,Protocol.JsonOptions);
}
