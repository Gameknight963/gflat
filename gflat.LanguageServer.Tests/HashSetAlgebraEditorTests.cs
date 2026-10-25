using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class HashSetAlgebraEditorTests
{
    [Fact]
    public void ActualSetAlgebraCallsResolveHoverAndNavigateToLibraryDefinitions()
    {
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"StandardLibrary"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).ToList();
        string text="""
            using std; using std::Collections;
            int main(){HashSet<int> target=new();HashSet<int> source=new();
                target.Add(1);source.Add(2);
                target.IntersectWith(&source);
                target.SymmetricExceptWith(&source);
                return (int)target.Count;}
            """;
        var source=new SourceFile(Path.GetFullPath("set-algebra-usage.gf"),text);sources.Add(source);
        var snapshot=new AnalysisSnapshot(sources,new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,d=>d.Severity==DiagnosticSeverity.Error);
        var model=new EditorModel(snapshot,sources);
        foreach(var method in new[]{("IntersectWith","HashSet.gf"),("SymmetricExceptWith","HashSetExtensions.gf")})
        {
            int offset=text.IndexOf(method.Item1,StringComparison.Ordinal);
            string definition=JsonSerializer.Serialize(model.Definition(source.Path,offset));
            Assert.Contains(method.Item2,definition);
            string hover=JsonSerializer.Serialize(model.Hover(source.Path,offset),Protocol.JsonOptions);
            Assert.Contains(method.Item1,hover);
        }
    }
}
