using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class NestedEnumMemberQualifierTests
{
    [Theory]
    [InlineData("imported")]
    [InlineData("qualified")]
    [InlineData("alias")]
    [InlineData("namespace-shadow")]
    [InlineData("alias-shadow")]
    public void NestedEnumMembersUseTheirResolvedTypeQualifier(string form)
    {
        var library=new SourceFile(Path.GetFullPath("nested-enum-member/library.gf"),"""
            namespace Library {
                public static class Codec {
                    public enum Status {
                        Done=7,
                        NeedMore=8
                    }
                }
            }
            namespace Unrelated {
                public static class Codec { public enum Status { Done=99 } }
            }
            """);
        string prefix=form switch {
            "qualified"=>"Library::Codec",
            "alias"=>"Alias", _=>"Codec"
        };
        string imports=form is "imported" or "namespace-shadow"?"using Library;\n":"";
        if(form=="alias")imports="alias Alias=Library::Codec;\n";
        if(form=="alias-shadow")imports="alias Codec=Library::Codec;\n";
        string shadow=form.EndsWith("shadow",StringComparison.Ordinal)?"namespace Codec { public enum Status { Wrong=33 } }\n":"";
        var client=new SourceFile(Path.GetFullPath("nested-enum-member/client.gf"),imports+shadow+$$"""
            int Read() {
                Library::Codec::Status state={{prefix}}::Status::Done;
                if(state=={{prefix}}::Status::NeedMore)return 1;
                return 0;
            }
            """);
        SourceFile[] files=[library,client];
        var snapshot=new AnalysisSnapshot(files,new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,d=>d.Severity==DiagnosticSeverity.Error);
        var model=new EditorModel(snapshot,files);
        var classifications=Classifications(model,client);
        foreach(string name in new[]{"Done","NeedMore"}) {
            int declaration=library.Text.IndexOf(name+"=",StringComparison.Ordinal);
            int use=client.Text.IndexOf("::"+name,StringComparison.Ordinal)+2;
            Assert.True(declaration>=0&&use>1);
            Assert.Equal("enumMember",classifications.GetValueOrDefault(use));
            var hover=JsonSerializer.SerializeToElement(model.Hover(client.Path,use),Protocol.JsonOptions);
            Assert.NotEqual(JsonValueKind.Null,hover.ValueKind);
            string description=hover.GetProperty("contents").GetProperty("value").GetString()!;
            Assert.Contains(name,description);Assert.Contains("Status",description);Assert.DoesNotContain("$",description);
            var expected=model.Definition(library.Path,declaration);
            Assert.NotNull(expected);
            string actual=Json(model.Definition(client.Path,use));
            Assert.Equal(Json(expected),actual);
            Assert.Contains(Workspace.FileUri(library.Path),actual);
            Assert.Equal(Json(Workspace.ToRange(library.Span(declaration,name.Length))),
                JsonSerializer.SerializeToElement(expected,Protocol.JsonOptions).GetProperty("range").GetRawText());
        }
    }

    [Fact]
    public void ResolvingNestedQualifiersDoesNotExposePrivateSiblingTypes()
    {
        var source=new SourceFile(Path.GetFullPath("nested-enum-member/private.gf"),"""
            using Library;
            namespace Library {
                public static class Codec {
                    public enum Status { Done }
                    private enum Secret { Hidden }
                }
            }
            int Read(){return (int)Codec::Status::Done;}
            """);
        var snapshot=new AnalysisSnapshot([source],new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,d=>d.Severity==DiagnosticSeverity.Error);
        var model=new EditorModel(snapshot,[source]);
        int offset=source.Text.LastIndexOf("Codec::",StringComparison.Ordinal)+7;
        var labels=JsonSerializer.SerializeToElement(model.Completion(source.Path,offset),Protocol.JsonOptions)
            .GetProperty("items").EnumerateArray().Select(item=>item.GetProperty("label").GetString()).ToArray();
        Assert.Contains("Status",labels);Assert.DoesNotContain("Secret",labels);Assert.DoesNotContain("Hidden",labels);
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
