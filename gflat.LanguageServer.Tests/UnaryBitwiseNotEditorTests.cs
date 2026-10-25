using System.Text.Json;
using gflat.diagnostics;
using gflat.LanguageServer;

namespace gflat.LanguageServer.Tests;

public sealed class UnaryBitwiseNotEditorTests
{
    [Fact]
    public void ComplementUsesExistingInferredHoverAndOperatorOutline()
    {
        const string text="struct Bits{public int Value;public static Bits operator ~(Bits value)=>value;}void Probe(){Bits value=default;var result=~value;var mask=~(byte)0;}";
        var source=new SourceFile(Path.GetFullPath("unary-complement.gf"),text);
        var snapshot=new AnalysisSnapshot([source],new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,error=>error.Severity==DiagnosticSeverity.Error);
        var model=new EditorModel(snapshot,[source]);
        string result=Json(model.Hover(source.Path,text.IndexOf("result=~",StringComparison.Ordinal))).GetProperty("contents").GetProperty("value").GetString()!;
        string mask=Json(model.Hover(source.Path,text.IndexOf("mask=~",StringComparison.Ordinal))).GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("Bits",result);Assert.Contains("byte",mask);Assert.DoesNotContain("int",mask);
        var outline=Json(model.DocumentSymbols(source.Path));
        var type=outline.EnumerateArray().Single(item=>item.GetProperty("name").GetString()=="Bits");
        var op=type.GetProperty("children").EnumerateArray().Single(item=>item.GetProperty("name").GetString()=="operator ~");
        int start=op.GetProperty("selectionRange").GetProperty("start").GetProperty("character").GetInt32();
        Assert.Equal(text.IndexOf("operator ~",StringComparison.Ordinal),start);
    }
    private static JsonElement Json(object? value)=>JsonSerializer.SerializeToElement(value,Protocol.JsonOptions);
}
