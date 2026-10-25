using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class QueryOrderEditorTests
{
    [Fact]
    public void DefaultAndCustomOrderShareCompletionAndSelectTheirSourceOverloads()
    {
        const string text="using std;int Compare(immutable(int)* a,immutable(int)* b){if(*a<*b)return -1;if(*a>*b)return 1;return 0;}void Probe(){int[3] data=[3,1,2];Span<int> values=new(data,3);var ascending=values.AsEnumerable().Order();var custom=values.AsEnumerable().Order(&Compare);}";
        var (model,source,files)=Analyze(text);
        var library=files.Single(file=>Path.GetFileName(file.Path)=="EnumerableOrder.gf");
        foreach(var row in new[]{(Call:".Order()",Arity:0,Declaration:"Order<T : IComparable<T>, S, E>"),(Call:".Order(&Compare)",Arity:1,Declaration:"Order<T, S, E>")})
        {
            int use=text.IndexOf(row.Call,StringComparison.Ordinal)+1;
            var items=Json(model.Completion(source.Path,use)).GetProperty("items");
            Assert.Single(items.EnumerateArray(),item=>item.GetProperty("label").GetString()=="Order");
            string hover=Hover(model,source,use);Assert.Contains("Order",hover);Assert.Contains("OrderSource<int",hover.Replace(" ",""));Assert.Contains("Enumerator",hover);Assert.DoesNotContain('$',hover);
            Definition(model,source,use,library,Declaration(library,row.Declaration));
            var help=Json(model.SignatureHelp(source.Path,use+"Order(".Length));
            var signatures=help.GetProperty("signatures");Assert.Equal(2,signatures.GetArrayLength());
            var selected=signatures[help.GetProperty("activeSignature").GetInt32()];
            Assert.Equal(row.Arity,selected.GetProperty("parameters").GetArrayLength());
            Assert.Contains("OrderSource",selected.GetProperty("label").GetString());
            if(row.Arity==1){Assert.Equal(0,help.GetProperty("activeParameter").GetInt32());Assert.Contains("compare",selected.GetProperty("label").GetString());}
        }
    }

    [Fact]
    public void QualifiedNestedCursorRetainsConcreteTypeNavigationAndBorrowedCurrent()
    {
        const string text="using std;int Probe(){int[2] data=[2,1];std::Span<int> values=new(data,2);var query=values.AsEnumerable().Order();std::OrderSource<int,std::Span<int>,std::Span<int>::Enumerator>::Enumerator iterator=query.GetEnumerator();bool moved=iterator.MoveNext();immutable(int)* borrowed=&iterator.Current;return *borrowed;}";
        var (model,source,files)=Analyze(text);
        var library=files.Single(file=>Path.GetFileName(file.Path)=="OrderSource.gf");
        int nested=text.IndexOf("Enumerator iterator",StringComparison.Ordinal);
        string nestedHover=Hover(model,source,nested);Assert.Contains("OrderSource<int",nestedHover.Replace(" ",""));Assert.Contains("Enumerator",nestedHover);Assert.DoesNotContain('$',nestedHover);
        Definition(model,source,nested,library,Declaration(library,"public struct Enumerator","public struct "));
        int current=text.IndexOf("Current;",StringComparison.Ordinal);
        string currentHover=Hover(model,source,current);Assert.Contains("ref ",currentHover);Assert.Contains("int",currentHover);Assert.Contains("Current",currentHover);Assert.DoesNotContain('$',currentHover);
        Definition(model,source,current,library,Declaration(library,"public ref immutable(T) Current","public ref immutable(T) "));
        int factory=text.IndexOf("GetEnumerator",StringComparison.Ordinal);
        string factoryHover=Hover(model,source,factory);Assert.Contains("OrderSource<int",factoryHover.Replace(" ",""));Assert.Contains("Enumerator",factoryHover);
        var enumerable=files.Single(file=>Path.GetFileName(file.Path)=="Enumerable.gf");
        Definition(model,source,factory,enumerable,Declaration(enumerable,"public Iterator GetEnumerator()","public Iterator "));
    }

    private static (EditorModel Model,SourceFile Source,List<SourceFile> Files) Analyze(string text)
    {
        var files=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"StandardLibrary"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).ToList();
        var source=new SourceFile(Path.GetFullPath("query-order-editor.gf"),text);files.Add(source);
        var snapshot=new AnalysisSnapshot(files,new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,error=>error.Severity==DiagnosticSeverity.Error);
        return(new EditorModel(snapshot,files),source,files);
    }
    private static JsonElement Json(object? value)=>JsonSerializer.SerializeToElement(value,Protocol.JsonOptions);
    private static string Hover(EditorModel model,SourceFile source,int offset)
        =>Json(model.Hover(source.Path,offset)).GetProperty("contents").GetProperty("value").GetString()!;
    private static int Declaration(SourceFile library,string marker,string prefix="")
    {
        int offset=library.Text.IndexOf(marker,StringComparison.Ordinal);Assert.True(offset>=0,$"Missing declaration: {marker}");
        return offset+prefix.Length;
    }
    private static void Definition(EditorModel model,SourceFile source,int use,SourceFile library,int declaration)
    {
        Assert.True(declaration>=0);var expected=model.Definition(library.Path,declaration);Assert.NotNull(expected);
        Assert.Equal(expected,model.Definition(source.Path,use));
    }
}
