using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class NthElementEditorTests
{
    [Fact]
    public void ExplicitRankSelectionShowsConcreteBorrowedSpanAndActualDefinition()
    {
        const string text="""
            using std; using std::Collections;
            int Compare(readonly(int)* a,readonly(int)* b){if(*a<*b)return -1;if(*a>*b)return 1;return 0;}
            void Observe(){int[3] data=[3,1,2];Span<int> values=new(data,3);NthElement<int>(values,1,Compare);}
            """;
        var files=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"StandardLibrary"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).ToList();
        var library=files.Single(file=>Path.GetFileName(file.Path)=="NthElement.gf");
        var source=new SourceFile(Path.GetFullPath("nth-element-editor.gf"),text);files.Add(source);
        var snapshot=new AnalysisSnapshot(files,new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,error=>error.Severity==DiagnosticSeverity.Error);
        var model=new EditorModel(snapshot,files);
        int use=text.IndexOf("NthElement<int>(",StringComparison.Ordinal);Assert.True(use>=0);
        Assert.Single(Json(model.Completion(source.Path,use)).GetProperty("items").EnumerateArray(),
            item=>item.GetProperty("label").GetString()=="NthElement");
        string hover=Json(model.Hover(source.Path,use)).GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("NthElement<int>",hover);Assert.Contains("Span<int>",hover);
        Assert.Contains("nuint nth",hover);Assert.Contains("void",hover);Assert.DoesNotContain('$',hover);
        const string anchor="public void NthElement<T>(";
        int declaration=library.Text.IndexOf(anchor,StringComparison.Ordinal);Assert.True(declaration>=0);
        var expected=model.Definition(library.Path,declaration+"public void ".Length);Assert.NotNull(expected);
        Assert.Equal(expected,model.Definition(source.Path,use));
        int argument=text.IndexOf("values,",use,StringComparison.Ordinal);Assert.True(argument>use);
        var help=Json(model.SignatureHelp(source.Path,argument+"values,".Length));
        Assert.Equal(1,help.GetProperty("activeParameter").GetInt32());
        var signatures=help.GetProperty("signatures");
        Assert.Equal(2,signatures.GetArrayLength());
        Assert.Equal(new[]{2,3},signatures.EnumerateArray().Select(item=>item.GetProperty("parameters").GetArrayLength()).Order().ToArray());
        var signature=signatures[help.GetProperty("activeSignature").GetInt32()];
        Assert.Equal(3,signature.GetProperty("parameters").GetArrayLength());
        Assert.Contains("NthElement<int>",signature.GetProperty("label").GetString());
        Assert.Contains("nuint nth",signature.GetProperty("label").GetString());
        Assert.Contains("compare",signature.GetProperty("label").GetString());
    }
    private static JsonElement Json(object? value)=>JsonSerializer.SerializeToElement(value,Protocol.JsonOptions);
}
