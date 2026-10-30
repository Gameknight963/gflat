using System.Text.Json;
using gflat.ast;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

// Exercise the single Sum<T,S,E> implementation through protected views.
public sealed class QueryGenericSumEditorTests
{
    [Theory]
    [InlineData("int","int","1,2,3","samples")]
    [InlineData("long","long","1l,2l,3l","samples")]
    [InlineData("int","readonly(int)","1,2,3","samples.AsReadOnly()")]
    [InlineData("long","immutable(long)","1l,2l,3l","samples.AsImmutable()")]
    public void GenericSumKeepsConcreteQualifiedElementArgumentAndReturnsLong(string storage,string element,string literals,string view)
    {
        string text="using Standard; using Standard::Memory; using Standard::Queries; using Standard::Collections;long Probe(){"+storage+"[3] numbers=["+literals+"];Span<"+storage+"> samples=new(numbers,3);"+
            "Span<"+element+"> values="+view+";long total=values.AsEnumerable().Sum();return total;}";
        string root=Path.Combine(AppContext.BaseDirectory,"StandardLibrary","core");
        string[] names=["Collections/IEnumerable.gf","Queries/Enumerable.gf","Memory/Span.gf","Queries/enumerable_sum.gf"];
        var files=names.Select(name=>Path.Combine(root,name.Replace('/',Path.DirectorySeparatorChar)))
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).ToList();
        var source=new SourceFile(Path.GetFullPath("query-generic-sum-editor.gf"),text);files.Add(source);
        SourceFile library=files.Single(file=>Path.GetFileName(file.Path)=="enumerable_sum.gf");
        var snapshot=new AnalysisSnapshot(files);
        Assert.DoesNotContain(snapshot.Diagnostics,error=>error.Severity==DiagnosticSeverity.Error);
        var call=Assert.Single(snapshot.Nodes.Where(node=>node.Span.SourceId==source.Id).Select(node=>node.Node).OfType<CallExpression>(),
            node=>node.Callee is MemberAccessExpression{Member:"Sum"});
        Assert.Equal("long",TypeChecker.TypeName(snapshot.Checker!.GetType(call)));
        var model=new EditorModel(snapshot,files);int use=text.IndexOf("Sum()",StringComparison.Ordinal);
        var items=Json(model.Completion(source.Path,use)).GetProperty("items");
        Assert.Single(items.EnumerateArray(),item=>item.GetProperty("label").GetString()=="Sum");
        Assert.DoesNotContain(items.EnumerateArray(),item=>item.GetProperty("label").GetString()=="CheckedSumAdd");
        string hover=Hover(model,source,use);
        // Deep protection on a scalar has the canonical readonly presentation.
        string displayElement=element=="immutable(long)"?"readonly(long)":element;
        Assert.Contains("long",hover);Assert.Contains("Sum()",hover);
        Assert.Equal(displayElement,snapshot.Checker!.DisplayTypeName(snapshot.Checker.GetGenericCallTypeArguments(call)![0]));Assert.DoesNotContain('$',hover);
        int declaration=EditorSourceDeclarations.ExtensionMethod(library,"Sum");Assert.True(declaration>=0);
        var expected=model.Definition(library.Path,declaration);Assert.NotNull(expected);
        Assert.Equal(expected,model.Definition(source.Path,use));Assert.Contains("enumerable_sum.gf",JsonSerializer.Serialize(expected,Protocol.JsonOptions));
        var help=Json(model.SignatureHelp(source.Path,text.IndexOf('(',use)+1));
        var signature=Assert.Single(help.GetProperty("signatures").EnumerateArray());
        Assert.Empty(signature.GetProperty("parameters").EnumerateArray());
        string label=signature.GetProperty("label").GetString()!;
        Assert.Contains("long",label);Assert.Contains("Sum()",label);Assert.DoesNotContain('$',label);
        int total=text.LastIndexOf("total",StringComparison.Ordinal);
        Assert.Contains("long total",Hover(model,source,total));
        var local=model.Definition(source.Path,text.IndexOf("total=",StringComparison.Ordinal));Assert.NotNull(local);
        Assert.Equal(local,model.Definition(source.Path,total));
    }

    private static JsonElement Json(object? value)=>JsonSerializer.SerializeToElement(value,Protocol.JsonOptions);
    private static string Hover(EditorModel model,SourceFile source,int offset)
        =>Json(model.Hover(source.Path,offset)).GetProperty("contents").GetProperty("value").GetString()!;
}
