using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class MemberSignatureAliasEditorTests
{
    [Theory]
    [InlineData("early-qualified")]
    [InlineData("late-qualified")]
    [InlineData("nongeneric-qualified")]
    [InlineData("early-imported")]
    [InlineData("late-imported")]
    [InlineData("early-relative-qualified")]
    public void FieldIdentityBindingPreservesDeclaredAliasHoverAndNavigation(string form)
    {
        string libraryText="""
            namespace Library {
                alias Word=int;
                class Box {public Word Value;public Word Read(Word* input)=>*input;}
                struct Storage {public Word OtherValue;}
            }
            """;
        string helperText="""
            namespace Helpers {
                alias Word=long;
                struct Probe<T> {
                    public static int Field(Library::Box* owner)=>owner.Value;
                    public static int OtherField(Library::Storage* owner)=>owner.OtherValue;
                    public static int Method(Library::Box* owner,int* input)=>owner.Read(input);
                }
            }
            """;
        if(form.Contains("imported"))helperText="using Library;\n"+helperText.Replace("Library::Box","Box",StringComparison.Ordinal)
            .Replace("Library::Storage","Storage",StringComparison.Ordinal);
        if(form.StartsWith("nongeneric",StringComparison.Ordinal))helperText=helperText.Replace("Probe<T>","Probe",StringComparison.Ordinal);
        if(form.Contains("relative")){
            libraryText=libraryText.Replace("namespace Library","namespace Root::Library",StringComparison.Ordinal);
            helperText="using Root;\n"+helperText;
        }
        var library=new SourceFile(Path.GetFullPath("a-member-library.gf"),libraryText);
        var helper=new SourceFile(Path.GetFullPath("b-member-helper.gf"),helperText);
        var client=new SourceFile(Path.GetFullPath("z-member-client.gf"),
            (form.StartsWith("early",StringComparison.Ordinal)?"int Trigger(Helpers::Probe<int>* value)=>0;":"")+"int main()=>0;");
        SourceFile[] files=[library,helper,client];
        var snapshot=new AnalysisSnapshot(files,new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,d=>d.Severity==DiagnosticSeverity.Error);
        var model=new EditorModel(snapshot,files);
        foreach(string name in new[]{"Value","OtherValue","Read"}) {
            int use=helper.Text.IndexOf("."+name,StringComparison.Ordinal)+1;
            int declaration=library.Text.IndexOf(" "+name,StringComparison.Ordinal)+1;
            Assert.True(use>0&&declaration>0);
            var expected=model.Definition(library.Path,declaration);
            Assert.NotNull(expected);
            Assert.Equal(Json(expected),Json(model.Definition(helper.Path,use)));
            string hover=JsonSerializer.SerializeToElement(model.Hover(helper.Path,use),Protocol.JsonOptions)
                .GetProperty("contents").GetProperty("value").GetString()!;
            Assert.Contains("Word",hover);
            Assert.DoesNotContain("long",hover);
            Assert.DoesNotContain('$',hover);
        }
    }

    [Fact]
    public void ATypeAliasPrefixRetainsPrecedenceOverTheSameNamespaceName()
    {
        var library=new SourceFile(Path.GetFullPath("a-prefix-library.gf"),"""
            namespace Library {public struct Box {public long Value;}}
            namespace Types {public class Owner {public class Box {public int Value;}}}
            """);
        var helper=new SourceFile(Path.GetFullPath("b-prefix-helper.gf"),"""
            namespace Helpers {
                alias Library=Types::Owner;
                struct Probe<T>{public static int Read(Library::Box* owner)=>owner.Value;}
            }
            """);
        SourceFile[] files=[library,helper];
        var snapshot=new AnalysisSnapshot(files,new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,d=>d.Severity==DiagnosticSeverity.Error);
        var model=new EditorModel(snapshot,files);
        int declaration=library.Text.LastIndexOf("Value",StringComparison.Ordinal);
        int use=helper.Text.LastIndexOf("Value",StringComparison.Ordinal);
        var expected=model.Definition(library.Path,declaration);
        Assert.NotNull(expected);
        Assert.Equal(Json(expected),Json(model.Definition(helper.Path,use)));
        string hover=JsonSerializer.SerializeToElement(model.Hover(helper.Path,use),Protocol.JsonOptions)
            .GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("int",hover);
        Assert.DoesNotContain("long",hover);
    }

    private static string Json(object? value)=>JsonSerializer.Serialize(value,Protocol.JsonOptions);
}
