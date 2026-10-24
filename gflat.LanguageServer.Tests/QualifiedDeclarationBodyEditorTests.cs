using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class QualifiedDeclarationBodyEditorTests
{
    [Fact]
    public void RemovedWeakBodyKeepsItsOriginalPrivateOwnerCompletion()
    {
        var owner=new SourceFile(Path.GetFullPath("removed-weak-owner.gf"),"static class Settings { private static int secret=42; public weak static int Read(){return secret;} }");
        var source=new SourceFile(Path.GetFullPath("removed-weak-replacement.gf"),"public replace static int Settings::Read()=>7; int main()=>Settings::Read();");
        SourceFile[] sources=[owner,source];
        var snapshot=new AnalysisSnapshot(sources,new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,d=>d.Severity==DiagnosticSeverity.Error);
        var model=new EditorModel(snapshot,sources);
        int field=owner.Text.LastIndexOf("secret",StringComparison.Ordinal);
        var completion=JsonSerializer.SerializeToElement(model.Completion(owner.Path,field),Protocol.JsonOptions);
        Assert.Contains(completion.GetProperty("items").EnumerateArray(),item=>item.GetProperty("label").GetString()=="secret");
    }

    [Fact]
    public void UninstantiatedGenericBodyKeepsItsOriginalPrivateOwnerCompletion()
    {
        var source=new SourceFile(Path.GetFullPath("ordinary-generic-body.gf"),"struct Box<T> { private T value; public T Read(){return value;} } int main()=>0;");
        var snapshot=new AnalysisSnapshot([source],new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,d=>d.Severity==DiagnosticSeverity.Error);
        var model=new EditorModel(snapshot,[source]);
        int field=source.Text.LastIndexOf("value",StringComparison.Ordinal);
        var completion=JsonSerializer.SerializeToElement(model.Completion(source.Path,field),Protocol.JsonOptions);
        Assert.Contains(completion.GetProperty("items").EnumerateArray(),item=>item.GetProperty("label").GetString()=="value");
    }

    [Fact]
    public void QualifiedNamespaceReplacementBodyOffersItsOwnersUnqualifiedHelpers()
    {
        var owner=new SourceFile(Path.GetFullPath("qualified-body-owner.gf"),"namespace Engine { public int Helper()=>42; public expect int Run(); }");
        var source=new SourceFile(Path.GetFullPath("qualified-body-main.gf"),"replace int Engine::Run(){return Helper();} int main()=>Engine::Run();");
        SourceFile[] sources=[owner,source];
        var snapshot=new AnalysisSnapshot(sources,new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,d=>d.Severity==DiagnosticSeverity.Error);
        var model=new EditorModel(snapshot,sources);
        int helper=source.Text.IndexOf("Helper",StringComparison.Ordinal);
        var completion=JsonSerializer.SerializeToElement(model.Completion(source.Path,helper),Protocol.JsonOptions);
        Assert.Contains(completion.GetProperty("items").EnumerateArray(),item=>item.GetProperty("label").GetString()=="Helper");
    }

    [Fact]
    public void QualifiedStaticReplacementBodyOffersItsOwnersPrivateStaticFields()
    {
        var owner=new SourceFile(Path.GetFullPath("qualified-static-body-owner.gf"),"static class Settings { private static int secret=42; public expect static int Read(); }");
        var source=new SourceFile(Path.GetFullPath("qualified-static-body-main.gf"),"public replace static int Settings::Read(){return secret;} int main()=>Settings::Read();");
        SourceFile[] sources=[owner,source];
        var snapshot=new AnalysisSnapshot(sources,new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,d=>d.Severity==DiagnosticSeverity.Error);
        var model=new EditorModel(snapshot,sources);
        int field=source.Text.IndexOf("secret",StringComparison.Ordinal);
        var completion=JsonSerializer.SerializeToElement(model.Completion(source.Path,field),Protocol.JsonOptions);
        Assert.Contains(completion.GetProperty("items").EnumerateArray(),item=>item.GetProperty("label").GetString()=="secret");
    }
}
