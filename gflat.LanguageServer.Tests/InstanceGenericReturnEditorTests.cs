using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class InstanceGenericReturnEditorTests
{
    [Theory]
    [InlineData("class",false)]
    [InlineData("class",true)]
    [InlineData("struct",false)]
    [InlineData("struct",true)]
    public void SelectedMethodAndChainedFieldUseClosedConstructedTypes(string kind,bool explicitArguments)
    {
        string method=explicitArguments?"Make<int>":"Make";
        string text="class T{public int Bogus;}struct Box<T>{public T value;public Box(T item){value=item;}}"+
            kind+" Helpers{public Box<T> Make<T>(Box<T> value)=>value;}"+
            "int main(){Helpers helper=new();Box<int> input=new(42);return helper."+method+"(input).value;}";
        var source=new SourceFile(Path.GetFullPath("instance-generic-return.gf"),text);
        var snapshot=new AnalysisSnapshot([source],new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,d=>d.Severity==DiagnosticSeverity.Error);
        var spans=snapshot.Nodes.Select(entry=>(entry.Node,entry.Node.Span)).ToArray();
        int classes=snapshot.Checker!.GetAllClasses().Count();
        var model=new EditorModel(snapshot,[source]);
        int use=text.LastIndexOf(method+"(",StringComparison.Ordinal);
        int field=text.LastIndexOf("value",StringComparison.Ordinal);
        for(int iteration=0;iteration<2;iteration++)
        {
            string hover=Json(model.Hover(source.Path,use)).GetProperty("contents").GetProperty("value").GetString()!;
            Assert.Contains("Box<int>",hover);Assert.Contains("Make<int>",hover);
            Assert.DoesNotContain("Bogus",hover);
            string fieldHover=Json(model.Hover(source.Path,field)).GetProperty("contents").GetProperty("value").GetString()!;
            Assert.Contains("int",fieldHover);Assert.DoesNotContain("Bogus",fieldHover);
            Assert.Equal(model.Definition(source.Path,text.IndexOf("Make<T>",StringComparison.Ordinal)),model.Definition(source.Path,use));
            Assert.NotNull(model.Definition(source.Path,use));
            Assert.NotNull(model.Definition(source.Path,field));
            var help=Json(model.SignatureHelp(source.Path,use+method.Length+1));
            string label=Assert.Single(help.GetProperty("signatures").EnumerateArray()).GetProperty("label").GetString()!;
            Assert.Contains("Box<int>",label);Assert.Contains("Make<int>",label);
        }
        Assert.DoesNotContain(snapshot.Diagnostics,d=>d.Severity==DiagnosticSeverity.Error);
        Assert.Equal(classes,snapshot.Checker.GetAllClasses().Count());
        foreach(var (node,span) in spans)Assert.Equal(span,node.Span);
    }

    [Fact]
    public void RejectedUnboundValueDoesNotAcquireGlobalTsMembers()
    {
        const string text="class T{public int Bogus;}struct Box<A>{public A value;}"+
            "struct Helpers{public Box<T> Make<T>(Box<T> value)=>value;}"+
            "void Bad(){Helpers helper=new();var missing=helper.Make;missing.value.Bogus;}int Good()=>42;";
        var source=new SourceFile(Path.GetFullPath("unbound-instance-value.gf"),text);
        var snapshot=new AnalysisSnapshot([source],new(){NoStdLib=true});
        Assert.Contains(snapshot.Diagnostics,d=>d.Message.Contains("Unspecialized generic member 'Make' cannot be used as a value",StringComparison.Ordinal));
        var model=new EditorModel(snapshot,[source]);
        int use=text.LastIndexOf("Bogus",StringComparison.Ordinal);
        Assert.Null(model.Hover(source.Path,use));
        Assert.Null(model.Definition(source.Path,use));
        var items=Json(model.Completion(source.Path,use)).GetProperty("items").EnumerateArray();
        Assert.DoesNotContain(items,item=>item.GetProperty("label").GetString()=="Bogus");
        Assert.NotNull(model.Hover(source.Path,text.IndexOf("Good",StringComparison.Ordinal)));
    }

    private static JsonElement Json(object? value)=>JsonSerializer.SerializeToElement(value,Protocol.JsonOptions);
}
