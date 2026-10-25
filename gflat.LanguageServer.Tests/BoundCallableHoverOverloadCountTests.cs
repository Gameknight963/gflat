using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class BoundCallableHoverOverloadCountTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HoverCountsAccessibleSourceDeclarationsRatherThanGenericSpecializations(bool actualOverload)
    {
        string libraryText="namespace Library{public T Pick<T>(T value)=>value;"+
            (actualOverload?"public int Pick(int value,bool tag)=>value;private int Pick(int value,long tag)=>value;":"")+"}";
        const string text="int Probe()=>Library::Pick<int>(7);";
        var library=new SourceFile(Path.GetFullPath("hover-count-library.gf"),libraryText);
        var source=new SourceFile(Path.GetFullPath("hover-count-caller.gf"),text);
        var snapshot=new AnalysisSnapshot([library,source],new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,error=>error.Severity==DiagnosticSeverity.Error);
        var model=new EditorModel(snapshot,[library,source]);int use=text.IndexOf("Pick<int>",StringComparison.Ordinal);
        string hover=Json(model.Hover(source.Path,use)).GetProperty("contents").GetProperty("value").GetString()!;
        if(actualOverload){Assert.Contains("+1 overload",hover);Assert.DoesNotContain("+2 overloads",hover);}
        else Assert.DoesNotContain("overload",hover);
        var signatures=Json(model.SignatureHelp(source.Path,use+"Pick<int>(".Length)).GetProperty("signatures");
        Assert.Equal(actualOverload?2:1,signatures.GetArrayLength());
    }
    private static JsonElement Json(object? value)=>JsonSerializer.SerializeToElement(value,Protocol.JsonOptions);
}
