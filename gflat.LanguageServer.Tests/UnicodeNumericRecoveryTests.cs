using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class UnicodeNumericRecoveryTests
{
    [Theory]
    [InlineData("float bad=1\u0662f;", "\u0662")]
    [InlineData("double bad=1.\uff12d;", "\uff12")]
    public void NonAsciiNumericCharacterIsDiagnosedWithoutErasingLaterOrOtherFileServices(string invalid,string character)
    {
        const string follow="int Follow(int argument){int retained=argument;return retained;}";
        var broken=new SourceFile(Path.GetFullPath("a-unicode-number.gf"),invalid+"\n"+follow);
        var healthy=new SourceFile(Path.GetFullPath("z-unicode-number-healthy.gf"),
            "int Healthy(){int answer=Follow(42);return answer;}");
        var snapshot=new AnalysisSnapshot([broken,healthy],new(){NoStdLib=true});
        int offending=broken.Text.IndexOf(character,StringComparison.Ordinal);
        var error=Assert.Single(snapshot.Diagnostics,diagnostic=>
            diagnostic.Descriptor.Id=="GF0004"&&diagnostic.FilePath==broken.Path&&diagnostic.Span.Start==offending);
        Assert.Equal(DiagnosticSeverity.Error,error.Severity);
        Assert.Equal(1,error.Span.Length);
        Assert.Equal(character,broken.Text.Substring(error.Span.Start,error.Span.Length));
        Assert.Contains("Unexpected character",error.Message);
        Assert.DoesNotContain(snapshot.Diagnostics,diagnostic=>diagnostic.Severity==DiagnosticSeverity.Error&&
            diagnostic.FilePath==broken.Path&&diagnostic.Span.Start>=invalid.Length+1);
        Assert.DoesNotContain(snapshot.Diagnostics,diagnostic=>diagnostic.Severity==DiagnosticSeverity.Error&&diagnostic.FilePath==healthy.Path);

        var model=new EditorModel(snapshot,[broken,healthy]);
        int method=broken.Text.IndexOf("Follow",StringComparison.Ordinal);
        int call=healthy.Text.IndexOf("Follow",StringComparison.Ordinal);
        var definition=model.Definition(broken.Path,method);Assert.NotNull(definition);
        Assert.Equal(definition,model.Definition(healthy.Path,call));
        Assert.Contains("int Follow",Hover(model,healthy,call));
        Assert.Equal("method",Colors(model,broken)[method]);
        Assert.Equal("method",Colors(model,healthy)[call]);
        foreach(var item in new[]{(Source:broken,Name:"retained"),(Source:healthy,Name:"answer")})
        {
            int declaration=item.Source.Text.IndexOf(item.Name+"=",StringComparison.Ordinal);
            int use=item.Source.Text.LastIndexOf(item.Name,StringComparison.Ordinal);
            var local=model.Definition(item.Source.Path,declaration);Assert.NotNull(local);
            Assert.Equal(local,model.Definition(item.Source.Path,use));
            Assert.Contains("int "+item.Name,Hover(model,item.Source,use));
            Assert.Equal("variable",Colors(model,item.Source)[use]);
        }
    }

    private static string Hover(EditorModel model,SourceFile source,int offset)
        =>JsonSerializer.SerializeToElement(model.Hover(source.Path,offset),Protocol.JsonOptions)
            .GetProperty("contents").GetProperty("value").GetString()!;

    // Same semantic-token decoding idiom as RecoveryTests.Colors.
    private static Dictionary<int,string> Colors(EditorModel model,SourceFile source)
    {
        var data=JsonSerializer.SerializeToElement(model.SemanticTokens(source.Path)).GetProperty("data")
            .EnumerateArray().Select(value=>value.GetInt32()).ToArray();
        int[] starts=[0,..source.Text.Select((value,index)=>(value,index)).Where(item=>item.value=='\n').Select(item=>item.index+1)];
        var result=new Dictionary<int,string>();int line=0,column=0;
        for(int i=0;i<data.Length;i+=5){
            line+=data[i];column=data[i]==0?column+data[i+1]:data[i+1];
            result[starts[line]+column]=EditorModel.TokenTypes[data[i+3]];
        }
        return result;
    }
}
