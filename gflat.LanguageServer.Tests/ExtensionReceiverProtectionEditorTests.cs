using System.Text.Json;
using gflat.ast;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class ExtensionReceiverProtectionEditorTests
{
    private const string Extensions="extension int{public int Increment(){(*this)++;return *this;}public readonly int Read()=>*this;public const readonly int Next()=>*this+1;}const int Folded()=>41;";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MaterializedLiteralAndFoldedResultsHaveMutableBorrowedCompletion(bool folded)
    {
        string receiver=folded?"Folded()":"(41)";
        string text=Extensions+"int main()=>"+receiver+".Increment();";
        var source=new SourceFile(Path.GetFullPath("mutable-constant-result-editor.gf"),text);
        var snapshot=new AnalysisSnapshot([source],new(){NoStdLib=true,NoBuiltins=true});
        Assert.DoesNotContain(snapshot.Diagnostics,item=>item.Severity==DiagnosticSeverity.Error);
        var call=Assert.Single(snapshot.Nodes.Select(item=>item.Node).OfType<CallExpression>(),
            node=>node.Callee is MemberAccessExpression{Member:"Increment"});
        var operand=Assert.IsType<MemberAccessExpression>(call.Callee).Object;
        Assert.False(snapshot.Checker!.GetType(operand).IsReadOnlyValue);
        if(folded)Assert.True(snapshot.Checker.TryGetConstValue(operand,out _));
        Assert.False(snapshot.Checker.IsExtensionReceiverReadOnly(operand));
        var model=new EditorModel(snapshot,[source]);
        int use=text.LastIndexOf("Increment()",StringComparison.Ordinal);
        var completion=Json(model.Completion(source.Path,use));
        Assert.Contains(completion.GetProperty("items").EnumerateArray(),item=>item.GetProperty("label").GetString()=="Increment");
        string hover=Json(model.Hover(source.Path,use)).GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("Increment()",hover);Assert.DoesNotContain('$',hover);
        int declaration=text.IndexOf("Increment()",StringComparison.Ordinal);
        Assert.Equal(new Location(Workspace.FileUri(source.Path),Workspace.ToRange(source.Span(declaration,"Increment".Length))),
            Assert.IsType<Location>(model.Definition(source.Path,use)));
    }

    [Theory]
    [InlineData("const int saved=41;int main()=>saved.Read();","saved.Read")]
    [InlineData("int main(){const int saved=41;return saved.Read();}","saved.Read")]
    [InlineData("int Probe(readonly(int) saved)=>saved.Read();","saved.Read")]
    public void ConstStorageAndReadonlyParametersRemainReadonly(string program,string access)
    {
        string text=Extensions+program;
        var source=new SourceFile(Path.GetFullPath("readonly-borrowed-editor.gf"),text);
        var snapshot=new AnalysisSnapshot([source],new(){NoStdLib=true,NoBuiltins=true});
        Assert.DoesNotContain(snapshot.Diagnostics,item=>item.Severity==DiagnosticSeverity.Error);
        var model=new EditorModel(snapshot,[source]);int use=text.IndexOf(access,StringComparison.Ordinal)+"saved.".Length;
        var completion=Json(model.Completion(source.Path,use)).GetProperty("items").EnumerateArray().ToArray();
        Assert.Contains(completion,item=>item.GetProperty("label").GetString()=="Read");
        Assert.DoesNotContain(completion,item=>item.GetProperty("label").GetString()=="Increment");
    }

    private static JsonElement Json(object? value)=>JsonSerializer.SerializeToElement(value,Protocol.JsonOptions);
}
