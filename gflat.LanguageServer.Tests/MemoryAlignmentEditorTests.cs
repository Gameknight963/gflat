using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class MemoryAlignmentEditorTests
{
    [Fact]
    public void IntegerAlignmentFunctionsExposeConcreteSignaturesAndTheirActualDeclarations()
    {
        const string text="void Probe(){nuint up=Memory::AlignUp(13,8);nuint down=Memory::AlignDown(13,8);bool aligned=Memory::IsAligned(16,8);}";
        var files=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"StandardLibrary"),"*.gf",SearchOption.AllDirectories).Select(path=>new SourceFile(path,File.ReadAllText(path))).ToList();
        var library=files.Single(file=>Path.GetFileName(file.Path)=="Alignment.gf");var source=new SourceFile(Path.GetFullPath("memory-alignment-editor.gf"),text);files.Add(source);
        var snapshot=new AnalysisSnapshot(files,new(){NoStdLib=true});Assert.DoesNotContain(snapshot.Diagnostics,error=>error.Severity==DiagnosticSeverity.Error);var model=new EditorModel(snapshot,files);
        foreach(var row in new[]{(Name:"AlignUp",Return:"nuint"),(Name:"AlignDown",Return:"nuint"),(Name:"IsAligned",Return:"bool")}){
            int use=text.IndexOf(row.Name+"(",StringComparison.Ordinal);Assert.True(use>=0);
            Assert.Contains(Json(model.Completion(source.Path,use)).GetProperty("items").EnumerateArray(),item=>item.GetProperty("label").GetString()==row.Name);
            string hover=Json(model.Hover(source.Path,use)).GetProperty("contents").GetProperty("value").GetString()!;Assert.Contains(row.Return,hover);Assert.Contains(row.Name,hover);
            string anchor="public "+row.Return+" "+row.Name+"(nuint value, nuint alignment)";int start=library.Text.IndexOf(anchor,StringComparison.Ordinal);Assert.True(start>=0);
            var expected=model.Definition(library.Path,start+anchor.IndexOf(row.Name+"(",StringComparison.Ordinal));Assert.NotNull(expected);Assert.Equal(expected,model.Definition(source.Path,use));
            int argument=text.IndexOf(',',use)+1;var help=Json(model.SignatureHelp(source.Path,argument));Assert.Equal(1,help.GetProperty("activeParameter").GetInt32());
            var signatures=help.GetProperty("signatures");Assert.Equal(1,signatures.GetArrayLength());var selected=signatures[0];Assert.Equal(2,selected.GetProperty("parameters").GetArrayLength());string label=selected.GetProperty("label").GetString()!;Assert.Contains("nuint value",label);Assert.Contains("nuint alignment",label);
        }
    }
    private static JsonElement Json(object? value)=>JsonSerializer.SerializeToElement(value,Protocol.JsonOptions);
}
