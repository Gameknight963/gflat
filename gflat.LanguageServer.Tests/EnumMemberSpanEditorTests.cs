using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class EnumMemberSpanEditorTests
{
    [Theory]
    [InlineData("class",false)]
    [InlineData("class",true)]
    [InlineData("static class",false)]
    [InlineData("static class",true)]
    [InlineData("struct",false)]
    [InlineData("struct",true)]
    public void EnumMemberNavigationNamesTheExactDeclarationInEitherLayout(string owner,bool multiline)
    {
        string members=multiline?"\n First=7,\n Second=8\n":" First=7, Second=8 ";
        string text=$$"""
            namespace Library {
                public {{owner}} Codec { public enum Status : byte { {{members}} } }
            }
            int Read()=> (int)Library::Codec::Status::First + (int)Library::Codec::Status::Second;
            """;
        var source=new SourceFile(Path.GetFullPath("enum-member-span-editor.gf"),text);
        var snapshot=new AnalysisSnapshot([source],new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,d=>d.Severity==DiagnosticSeverity.Error);
        var model=new EditorModel(snapshot,[source]);
        var classifications=Classifications(model,source);
        foreach(string name in new[]{"First","Second"}) {
            int declaration=text.IndexOf(name+"=",StringComparison.Ordinal);
            int use=text.LastIndexOf("::"+name,StringComparison.Ordinal)+2;
            Assert.Equal("enumMember",classifications.GetValueOrDefault(use));
            var hover=JsonSerializer.SerializeToElement(model.Hover(source.Path,use),Protocol.JsonOptions);
            Assert.NotEqual(JsonValueKind.Null,hover.ValueKind);
            string description=hover.GetProperty("contents").GetProperty("value").GetString()!;
            Assert.Contains(name,description);Assert.Contains("Status",description);
            string expected=Json(new{uri=Workspace.FileUri(source.Path),range=Workspace.ToRange(source.Span(declaration,name.Length))});
            Assert.Equal(expected,Json(model.Definition(source.Path,use)));
            Assert.Equal(expected,Json(model.Definition(source.Path,declaration)));
        }
    }

    private static string Json(object? value)=>JsonSerializer.Serialize(value,Protocol.JsonOptions);
    private static Dictionary<int,string> Classifications(EditorModel model,SourceFile source)
    {
        int[] data=JsonSerializer.SerializeToElement(model.SemanticTokens(source.Path),Protocol.JsonOptions)
            .GetProperty("data").EnumerateArray().Select(value=>value.GetInt32()).ToArray();
        var starts=new List<int>{0};for(int i=0;i<source.Text.Length;i++)if(source.Text[i]=='\n')starts.Add(i+1);
        var result=new Dictionary<int,string>();int line=0,column=0;
        for(int i=0;i<data.Length;i+=5){line+=data[i];column=data[i]==0?column+data[i+1]:data[i+1];result[starts[line]+column]=EditorModel.TokenTypes[data[i+3]];}
        return result;
    }
}
