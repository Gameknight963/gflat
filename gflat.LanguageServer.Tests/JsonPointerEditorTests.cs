using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

// Exercise the actual JSON Pointer source and borrowed result type.
public sealed class JsonPointerEditorTests
{
    private const string Program="using Standard; using Standard::Json; using Standard::Memory;nuint Probe(Span<readonly(char)> document,Span<readonly(char)> pointer){Span<readonly(char)> value=new();bool found=JsonPointer::TryResolve(document,pointer,ref value);readonly(char)*? data=value.Data;return value.Length;}";

    [Fact]
    public void PublicResolutionSignatureAndOutputArgumentNavigateWithoutExposingPrivateSelectionHelper()
    {
        var (model,source,files)=Analyze();int call=Program.IndexOf("TryResolve",StringComparison.Ordinal);
        var items=Json(model.Completion(source.Path,call)).GetProperty("items");
        Assert.Single(items.EnumerateArray(),item=>item.GetProperty("label").GetString()=="TryResolve");
        Assert.DoesNotContain(items.EnumerateArray(),item=>item.GetProperty("label").GetString()=="SelectChild");
        string hover=Hover(model,source,call);
        Assert.Contains("bool",hover);Assert.Contains("static",hover);Assert.Contains("TryResolve",hover);
        Assert.Matches(@"\bref\s+(?:Standard::Memory::)?Span<readonly\(char\)>\s+value\b",hover);Assert.DoesNotContain('$',hover);
        SourceFile library=files.Single(file=>Path.GetFileName(file.Path)=="JsonPointer.gf");
        int declaration=library.Text.IndexOf("TryResolve(",StringComparison.Ordinal);Assert.True(declaration>=0);
        Definition(model,source,call,library,declaration);
        var help=Json(model.SignatureHelp(source.Path,Program.IndexOf("ref value",StringComparison.Ordinal)+4));
        var signature=Assert.Single(help.GetProperty("signatures").EnumerateArray());
        Assert.Equal(2,help.GetProperty("activeParameter").GetInt32());Assert.Equal(3,signature.GetProperty("parameters").GetArrayLength());
        string label=signature.GetProperty("label").GetString()!;
        Assert.Contains("bool",label);Assert.Contains("TryResolve",label);Assert.DoesNotContain('$',label);
        Assert.Matches(@"\bref\s+(?:Standard::Memory::)?Span<readonly\(char\)>\s+value\b",label);
    }

    [Fact]
    public void StaticClassAndBorrowedOutputSpanKeepConcreteTypeClassificationHoverAndNavigation()
    {
        var (model,source,files)=Analyze();int type=Program.IndexOf("JsonPointer",StringComparison.Ordinal);
        Assert.Equal("class",Color(model,source,type));
        string hover=Hover(model,source,type);Assert.Contains("public static class",hover);Assert.Contains("JsonPointer",hover);Assert.DoesNotContain('$',hover);
        SourceFile library=files.Single(file=>Path.GetFileName(file.Path)=="JsonPointer.gf");
        int declaration=library.Text.IndexOf("class JsonPointer",StringComparison.Ordinal);Assert.True(declaration>=0);
        Definition(model,source,type,library,declaration+"class ".Length);
        int value=Program.LastIndexOf("value.Length",StringComparison.Ordinal);
        string valueHover=Hover(model,source,value);
        Assert.Matches(@"(?:Standard::Memory::)?Span<readonly\(char\)>\s+value\b",valueHover);Assert.DoesNotContain('$',valueHover);
        var expected=model.Definition(source.Path,Program.IndexOf("value=new",StringComparison.Ordinal));Assert.NotNull(expected);
        Assert.Equal(expected,model.Definition(source.Path,value));
        int data=Program.IndexOf("Data;",StringComparison.Ordinal);
        string dataHover=Hover(model,source,data);Assert.Contains("readonly(char)*?",dataHover);Assert.Contains("Data",dataHover);
        SourceFile span=files.Single(file=>Path.GetFileName(file.Path)=="Span.gf");
        int dataDeclaration=span.Text.IndexOf("Data {",StringComparison.Ordinal);Assert.True(dataDeclaration>=0);
        Definition(model,source,data,span,dataDeclaration);
    }

    private static (EditorModel Model,SourceFile Source,SourceFile[] Files) Analyze()
    {
        var files=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"StandardLibrary"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).ToList();
        var source=new SourceFile(Path.GetFullPath("json-pointer-editor.gf"),Program);files.Add(source);
        var snapshot=new AnalysisSnapshot(files,new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,error=>error.Severity==DiagnosticSeverity.Error);
        return(new EditorModel(snapshot,files),source,files.ToArray());
    }
    private static JsonElement Json(object? value)=>JsonSerializer.SerializeToElement(value,Protocol.JsonOptions);
    private static string Hover(EditorModel model,SourceFile file,int offset)
        =>Json(model.Hover(file.Path,offset)).GetProperty("contents").GetProperty("value").GetString()!;
    private static void Definition(EditorModel model,SourceFile file,int use,SourceFile library,int declaration)
    {
        var expected=model.Definition(library.Path,declaration);Assert.NotNull(expected);
        Assert.Equal(expected,model.Definition(file.Path,use));Assert.Contains(Path.GetFileName(library.Path),JsonSerializer.Serialize(expected,Protocol.JsonOptions));
    }
    private static string Color(EditorModel model,SourceFile file,int offset)
    {
        int[] data=Json(model.SemanticTokens(file.Path)).GetProperty("data").EnumerateArray().Select(value=>value.GetInt32()).ToArray();int column=0;
        for(int i=0;i<data.Length;i+=5){column=data[i]==0?column+data[i+1]:data[i+1];if(column==offset)return EditorModel.TokenTypes[data[i+3]];}
        Assert.Fail("Missing JsonPointer semantic token");return "";
    }
}
