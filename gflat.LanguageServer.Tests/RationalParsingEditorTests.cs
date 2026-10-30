using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class RationalParsingEditorTests
{
    [Fact]
    public void CStringAndCountedParsersNavigateTheirOverloadsAndExposeTheirReferenceOutputs()
    {
        const string text="using Standard; using Standard::Numerics;void Probe(){Rational parsed=Rational::Parse(\"2/4\");Rational counted=Rational::Parse(\"2/4tail\",3);Rational output=new();bool ok=Rational::TryParse(\"2/4\",ref output);bool countedOk=Rational::TryParse(\"2/4tail\",3,ref output);}";
        var files=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"StandardLibrary"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).ToList();
        var library=files.Single(file=>Path.GetFileName(file.Path)=="Rational.gf");
        var source=new SourceFile(Path.GetFullPath("rational-parsing-editor.gf"),text);files.Add(source);
        var snapshot=new AnalysisSnapshot(files,new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,error=>error.Severity==DiagnosticSeverity.Error);
        var model=new EditorModel(snapshot,files);
        foreach(var row in new[]{
            (Call:"Rational::Parse(\"2/4\")",Name:"Parse",Arity:1,Argument:0,Declaration:"public static Rational Parse(readonly(char)* text)",Result:"Rational"),
            (Call:"Rational::Parse(\"2/4tail\",3)",Name:"Parse",Arity:2,Argument:"\"2/4tail\",".Length,Declaration:"public static Rational Parse(readonly(char)* text, nuint count)",Result:"Rational"),
            (Call:"Rational::TryParse(\"2/4\",ref output)",Name:"TryParse",Arity:2,Argument:"\"2/4\",".Length,Declaration:"public static bool TryParse(readonly(char)* text, ref Rational result)",Result:"bool"),
            (Call:"Rational::TryParse(\"2/4tail\",3,ref output)",Name:"TryParse",Arity:3,Argument:"\"2/4tail\",3,".Length,Declaration:"public static bool TryParse(readonly(char)* text, nuint count, ref Rational result)",Result:"bool")}){
            int call=text.IndexOf(row.Call,StringComparison.Ordinal);Assert.True(call>=0);int use=call+"Rational::".Length;
            var items=Json(model.Completion(source.Path,use)).GetProperty("items");
            Assert.Contains(items.EnumerateArray(),item=>item.GetProperty("label").GetString()==row.Name);
            string hover=Json(model.Hover(source.Path,use)).GetProperty("contents").GetProperty("value").GetString()!;
            Assert.Contains(row.Result,hover);Assert.Contains(row.Name,hover);Assert.DoesNotContain('$',hover);
            int declaration=library.Text.IndexOf(row.Declaration,StringComparison.Ordinal);Assert.True(declaration>=0);
            declaration+=row.Declaration.IndexOf(row.Name,StringComparison.Ordinal);
            var expected=model.Definition(library.Path,declaration);Assert.NotNull(expected);
            Assert.Equal(expected,model.Definition(source.Path,use));
            var help=Json(model.SignatureHelp(source.Path,use+row.Name.Length+1+row.Argument));
            Assert.Equal(row.Arity-1,help.GetProperty("activeParameter").GetInt32());
            var signatures=help.GetProperty("signatures");Assert.Equal(2,signatures.GetArrayLength());
            var selected=signatures[help.GetProperty("activeSignature").GetInt32()];
            Assert.Equal(row.Arity,selected.GetProperty("parameters").GetArrayLength());
            if(row.Name=="TryParse")Assert.Contains("ref Standard::Numerics::Rational result",selected.GetProperty("label").GetString());
        }
    }
    private static JsonElement Json(object? value)=>JsonSerializer.SerializeToElement(value,Protocol.JsonOptions);
}
