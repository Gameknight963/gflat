using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public class StandardCollectionEditorTests
{
    private static readonly string[] Files = ["IHashable.gf", "IEquatable.gf", "IComparable.gf", "Hashing.gf", "IStringConvertable.gf", "String.gf", "ByteSearch.gf",
        "HashMap.gf", "HashSet.gf", "Queue.gf", "Stack.gf", "Vector.gf", "Span.gf", "IEnumerable.gf", "Enumerable.gf"];

    [Theory]
    [InlineData("HashMap<String,int> map=new(); map.Add(s\"key\",42); String key=s\"key\"; map.Find(&key);", "Find(", "Find")]
    [InlineData("Queue<String> queue=new(); queue.Enqueue(s\"item\"); queue.Dequeue();", "Dequeue(", "Dequeue")]
    [InlineData("Stack<String> stack=new(); stack.Push(s\"item\"); stack.Pop();", "Pop(", "Pop")]
    [InlineData("HashSet<String> set=new(); String key=s\"item\"; set.Add(key); set.Contains(&key);", "Contains(", "Contains")]
    [InlineData("String text=s\"ad\"; text.Insert(1,\"bc\");", "Insert(", "Insert")]
    public void ActualStandardCollectionCallsHaveReadableHoversAndDefinitions(string body, string target, string member)
    {
        var (model, source, text)=Analyze("using std; using std::Collections; int main(){"+body+"return 0;}");
        int offset=text.LastIndexOf(target,StringComparison.Ordinal);
        var hover=JsonSerializer.SerializeToElement(model.Hover(source.Path,offset),Protocol.JsonOptions);
        string signature=hover.GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains(member,signature);
        Assert.DoesNotContain('$',signature);
        Assert.NotNull(model.Definition(source.Path,offset));
    }

    [Fact]
    public void NestedMapEnumeratorKeepsStructClassificationAndNavigation()
    {
        var (model,source,text)=Analyze("using std; using std::Collections; int main(){HashMap<String,int> map=new(); HashMap<String,int>::Enumerator iterator=map.GetEnumerator(); return 0;}");
        int offset=text.IndexOf("Enumerator iterator",StringComparison.Ordinal);
        Assert.NotNull(model.Hover(source.Path,offset));
        Assert.NotNull(model.Definition(source.Path,offset));
        var data=JsonSerializer.SerializeToElement(model.SemanticTokens(source.Path)).GetProperty("data").EnumerateArray().Select(n=>n.GetInt32()).ToArray();
        int column=0;
        for(int i=0;i<data.Length;i+=5) {
            column=data[i]==0 ? column+data[i+1] : data[i+1];
            if(column==offset) { Assert.Equal("struct",EditorModel.TokenTypes[data[i+3]]); return; }
        }
        Assert.Fail("Missing semantic token for the nested enumerator");
    }

    private static (EditorModel Model,SourceFile Source,string Text) Analyze(string text)
    {
        var sources=Files.Select(file=>new SourceFile(Path.GetFullPath("std-editor/"+file),
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"EditorFixtures",file)))).ToList();
        var source=new SourceFile(Path.GetFullPath("std-editor/main.gf"),text);
        sources.Add(source);
        var snapshot=new AnalysisSnapshot(sources);
        Assert.DoesNotContain(snapshot.Diagnostics,d=>d.Severity==DiagnosticSeverity.Error);
        return (new EditorModel(snapshot,sources),source,text);
    }
}
