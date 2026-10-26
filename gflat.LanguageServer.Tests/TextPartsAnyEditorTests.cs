using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

// Exercise the actual standard-library adapters and borrowed view.
public sealed class TextPartsAnyEditorTests
{
    [Theory]
    [InlineData("Span<char>","char[3] buffer=['a',',','b'];Span<char> text=new(&buffer[0],3);","text_parts_any_span.gf")]
    [InlineData("Span<readonly(char)>","Span<readonly(char)> text=new(\"a,b\",3);","text_parts_any_span.gf")]
    [InlineData("ref immutable(String)","String text=String::From(\"a,b\",3);","string_split_any.gf")]
    public void SplitAnyMergesCompletionAndSelectsTheConcreteReceiverAndSeparatorAdapter(string receiver,string setup,string adapter)
    {
        string text="using std; using std::Memory;void Probe(){"+setup+"Span<readonly(char)> separators=new(\",;\",2);"
            +"TextPartsAny counted=text.SplitAny(separators,false);TextPartsAny terminated=text.SplitAny(\",;\",false);}";
        var files=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"StandardLibrary"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).ToList();
        var source=new SourceFile(Path.GetFullPath("text-parts-any-editor.gf"),text);files.Add(source);
        var snapshot=new AnalysisSnapshot(files,new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,error=>error.Severity==DiagnosticSeverity.Error);
        var model=new EditorModel(snapshot,files);
        var library=files.Single(file=>Path.GetFileName(file.Path)==adapter);
        int counted=text.IndexOf("SplitAny",StringComparison.Ordinal),terminated=text.LastIndexOf("SplitAny",StringComparison.Ordinal);
        foreach(var row in new[]{(Use:counted,Counted:true),(Use:terminated,Counted:false)})
        {
            Assert.Single(Json(model.Completion(source.Path,row.Use)).GetProperty("items").EnumerateArray(),
                item=>item.GetProperty("label").GetString()=="SplitAny");
            string hover=Json(model.Hover(source.Path,row.Use)).GetProperty("contents").GetProperty("value").GetString()!;
            Assert.Contains("TextPartsAny",hover);Assert.Contains("SplitAny(",hover);Assert.DoesNotContain('$',hover);
            Assert.Matches(@"bool\s+removeEmpty\s*=\s*false",hover);
            if(row.Counted)Assert.Contains("Span<readonly(char)> separators",hover);
            else {Assert.Contains("char",hover);Assert.Contains("* separators",hover);}

            int lastArgument=text.IndexOf("false",row.Use,StringComparison.Ordinal);
            var help=Json(model.SignatureHelp(source.Path,lastArgument));
            Assert.Equal(1,help.GetProperty("activeParameter").GetInt32());
            var signatures=help.GetProperty("signatures");Assert.Equal(2,signatures.GetArrayLength());
            var selected=signatures[help.GetProperty("activeSignature").GetInt32()];
            Assert.Equal(2,selected.GetProperty("parameters").GetArrayLength());
            string label=selected.GetProperty("label").GetString()!;
            Assert.Contains("TextPartsAny",label);Assert.Contains("SplitAny(",label);Assert.DoesNotContain('$',label);
            Assert.Matches(@"bool\s+removeEmpty\s*=\s*false",label);
            if(row.Counted)Assert.Matches(@"(?:std::Memory::)?Span<readonly\(char\)>\s+separators",label);
            else {Assert.Contains("char",label);Assert.Contains("* separators",label);}

            string declaration="TextPartsAny SplitAny(this "+receiver+" text, "
                +(row.Counted?"Span<readonly(char)> separators":"readonly(char)* separators");
            int start=library.Text.IndexOf(declaration,StringComparison.Ordinal);Assert.True(start>=0,declaration);
            int declaredName=start+"TextPartsAny ".Length;
            var expected=model.Definition(library.Path,declaredName);Assert.NotNull(expected);
            Assert.Equal(expected,model.Definition(source.Path,row.Use));
            Assert.Contains(adapter,JsonSerializer.Serialize(expected,Protocol.JsonOptions));
        }
        Assert.NotEqual(model.Definition(source.Path,counted),model.Definition(source.Path,terminated));
    }

    private static JsonElement Json(object? value)=>JsonSerializer.SerializeToElement(value,Protocol.JsonOptions);
}
