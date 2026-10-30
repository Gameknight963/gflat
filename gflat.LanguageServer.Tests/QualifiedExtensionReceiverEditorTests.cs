using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class QualifiedExtensionReceiverEditorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QualifiedGenericReceiversHaveCompletionHoverAndNavigationWithUnrelatedErrors(bool brokenFile)
    {
        var library=new SourceFile(Path.GetFullPath("qualified-extension-library.gf"),"""
            namespace Models { struct Box<T> { public T Value; } }
            namespace Extras { extension<T> Models::Box<T>{public int Measure()=> sizeof(T);} }
            """);
        var source=new SourceFile(Path.GetFullPath("qualified-extension-main.gf"),"""
            using Extras;
            int main() { Models::Box<int> box=new(); return box.Measure(); }
            """);
        var sources=new List<SourceFile>{library,source};
        string brokenPath=Path.GetFullPath("qualified-extension-broken.gf");
        if(brokenFile)sources.Add(new(brokenPath,"void Broken(){missing();}"));
        var snapshot=new AnalysisSnapshot(sources,new(){NoStdLib=true});
        var errors=snapshot.Diagnostics.Where(d=>d.Severity==DiagnosticSeverity.Error).ToArray();
        if(brokenFile){Assert.NotEmpty(errors);Assert.All(errors,d=>Assert.Equal(brokenPath,d.FilePath));}
        else Assert.Empty(errors);
        var model=new EditorModel(snapshot,sources);
        int member=source.Text.IndexOf("box.Measure",StringComparison.Ordinal)+4;
        var completion=JsonSerializer.SerializeToElement(model.Completion(source.Path,member),Protocol.JsonOptions);
        Assert.Contains(completion.GetProperty("items").EnumerateArray(),item=>item.GetProperty("label").GetString()=="Measure");
        var hover=JsonSerializer.SerializeToElement(model.Hover(source.Path,member+2),Protocol.JsonOptions);
        Assert.Contains("int Extras::Measure()",hover.GetProperty("contents").GetProperty("value").GetString());
        Assert.Contains("qualified-extension-library.gf",JsonSerializer.Serialize(model.Definition(source.Path,member+2),Protocol.JsonOptions));
    }
}
