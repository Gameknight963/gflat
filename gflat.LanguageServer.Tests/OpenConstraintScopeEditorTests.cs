using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class OpenConstraintScopeEditorTests
{
    [Fact]
    public void BodyLocalAliasCannotWidenTheGenericHeadersAdvertisedConstraint()
    {
        const string text="""
            interface IAllowed{}
            interface IHidden{}
            extension<T:IHidden> T{public int Secret()=>7;}
            int Borrow<T:IAllowed>(ref T value){alias IAllowed=IHidden;return value.Secret();}
            int main()=>0;
            """;
        var source=new SourceFile(Path.GetFullPath("open-constraint-scope.gf"),text);
        var snapshot=new AnalysisSnapshot([source],new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,error=>error.Severity==DiagnosticSeverity.Error);
        var model=new EditorModel(snapshot,[source]);
        int member=text.LastIndexOf("Secret",StringComparison.Ordinal);
        var completion=JsonSerializer.SerializeToElement(model.Completion(source.Path,member),Protocol.JsonOptions);
        Assert.DoesNotContain(completion.GetProperty("items").EnumerateArray(),item=>item.GetProperty("label").GetString()=="Secret");
        Assert.Null(model.Definition(source.Path,member));
    }
}
