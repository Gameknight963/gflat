using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class QualifiedDeclarationSignatureEditorTests
{
    [Fact]
    public void QualifiedReplacementSignatureOffersTypesFromItsBoundNamespace()
    {
        var owner=new SourceFile(Path.GetFullPath("qualified-signature-owner.gf"),"namespace Engine { struct Token{} public expect Token Run(Token input); }");
        var source=new SourceFile(Path.GetFullPath("qualified-signature-main.gf"),"replace Token Engine::Run(Token input){return input;}");
        SourceFile[] sources=[owner,source];
        var snapshot=new AnalysisSnapshot(sources,new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,d=>d.Severity==DiagnosticSeverity.Error);
        var model=new EditorModel(snapshot,sources);
        int result=source.Text.IndexOf("Token",StringComparison.Ordinal);
        int parameter=source.Text.LastIndexOf("Token",StringComparison.Ordinal);
        foreach(int offset in new[]{result,parameter}){
            var completion=JsonSerializer.SerializeToElement(model.Completion(source.Path,offset),Protocol.JsonOptions);
            Assert.Contains(completion.GetProperty("items").EnumerateArray(),item=>item.GetProperty("label").GetString()=="Token");
        }
    }
}
