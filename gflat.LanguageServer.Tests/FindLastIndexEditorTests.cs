using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class FindLastIndexEditorTests
{
    [Fact]
    public void BorrowedPredicateSearchHasItsActualDeclarationAndVisibleArguments()
    {
        const string text="using std; using std::Collections;bool Match(immutable(int)* value)=>*value%2==0;void Probe(){int[3] data=[1,2,3];Span<int> values=new(data,3);nuint at=FindLastIndex<int>(values.AsImmutable(),Match);}";
        var files=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"StandardLibrary"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).ToList();
        var library=files.Single(file=>Path.GetFileName(file.Path)=="Algorithms.gf");
        var source=new SourceFile(Path.GetFullPath("find-last-index-editor.gf"),text);files.Add(source);
        var snapshot=new AnalysisSnapshot(files,new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,error=>error.Severity==DiagnosticSeverity.Error);
        var model=new EditorModel(snapshot,files);int use=text.IndexOf("FindLastIndex<int>",StringComparison.Ordinal);
        var items=Json(model.Completion(source.Path,use)).GetProperty("items");
        Assert.Contains(items.EnumerateArray(),item=>item.GetProperty("label").GetString()=="FindLastIndex");
        string hover=Json(model.Hover(source.Path,use)).GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("nuint",hover);Assert.Contains("FindLastIndex<int>",hover);Assert.DoesNotContain('$',hover);
        Assert.Contains("Span<readonly(int)>",hover);Assert.Contains("bool(readonly(int)*)* predicate",hover);Assert.DoesNotContain("+1 overload",hover);
        const string anchor="public nuint FindLastIndex<T>(";int start=library.Text.IndexOf(anchor,StringComparison.Ordinal);Assert.True(start>=0);
        int declaration=start+anchor.IndexOf("FindLastIndex",StringComparison.Ordinal);var expected=model.Definition(library.Path,declaration);Assert.NotNull(expected);
        Assert.Equal(expected,model.Definition(source.Path,use));
        int argument=use+"FindLastIndex<int>(values.AsImmutable(),".Length;
        var help=Json(model.SignatureHelp(source.Path,argument));Assert.Equal(1,help.GetProperty("activeParameter").GetInt32());
        var signature=Assert.Single(help.GetProperty("signatures").EnumerateArray());Assert.Equal(2,signature.GetProperty("parameters").GetArrayLength());
        string label=signature.GetProperty("label").GetString()!;
        Assert.Contains("FindLastIndex<int>",label);Assert.Contains("Span<readonly(int)>",label);Assert.Contains("bool(readonly(int)*)* predicate",label);Assert.Contains("nuint",label);
    }
    private static JsonElement Json(object? value)=>JsonSerializer.SerializeToElement(value,Protocol.JsonOptions);
}
