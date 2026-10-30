using System.Text.Json;
using gflat.ast;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

// Exercise the actual UUID byte API declarations.
public sealed class UuidBytesEditorTests
{
    [Fact]
    public void StaticByteImportExposesItsBooleanNoThrowResultAndUuidOutputArgument()
    {
        const string text="using Standard;void Probe(readonly(byte)* data,nuint count,Uuid* result){bool valid=Uuid::TryFromBytes(data,count,result);}";
        var (model,source,library,snapshot)=Analyze(text);int call=text.IndexOf("TryFromBytes",StringComparison.Ordinal);
        string hover=Hover(model,source,call);
        Assert.Contains("bool",hover);Assert.Contains("static",hover);Assert.Contains("nothrow",hover);Assert.Contains("TryFromBytes",hover);
        Assert.Matches(@"\b(?:std::)?Uuid\*\s+result\b",hover);Assert.DoesNotContain('$',hover);
        Check(model,source,library,snapshot,call,"TryFromBytes",text.LastIndexOf("result",StringComparison.Ordinal)+1,
            @"\b(?:std::)?Uuid\*\s+result\b");
    }

    [Fact]
    public void ImmutableByteExportExposesItsNoThrowRefCountAndActualSourceDefinition()
    {
        const string text="using Standard;void Probe(immutable(Uuid)* value,byte* output,nuint capacity,ref nuint written) nothrow{bool valid=value.TryWriteBytes(output,capacity,ref written);}";
        var (model,source,library,snapshot)=Analyze(text);int call=text.IndexOf("TryWriteBytes",StringComparison.Ordinal);
        string hover=Hover(model,source,call);
        Assert.Contains("bool",hover);Assert.Contains("immutable",hover);Assert.Contains("nothrow",hover);Assert.Contains("TryWriteBytes",hover);
        Assert.Contains("ref nuint written",hover);Assert.DoesNotContain('$',hover);
        Check(model,source,library,snapshot,call,"TryWriteBytes",text.LastIndexOf("written",StringComparison.Ordinal)+1,
            @"\bref\s+nuint\s+written\b");
    }

    private static void Check(EditorModel model,SourceFile source,SourceFile library,AnalysisSnapshot snapshot,int call,string member,int argument,string outputPattern)
    {
        Assert.Single(Json(model.Completion(source.Path,call)).GetProperty("items").EnumerateArray(),item=>item.GetProperty("label").GetString()==member);
        var method=Assert.Single(snapshot.Nodes.Where(node=>node.Span.SourceId==library.Id).Select(node=>node.Node).OfType<MethodDeclaration>(),
            declaration=>declaration.Name==member);
        int name=library.Text.IndexOf(member+"(",method.Span.Start,StringComparison.Ordinal);Assert.True(name>=0);
        var expected=model.Definition(library.Path,name);Assert.NotNull(expected);
        Assert.Equal(expected,model.Definition(source.Path,call));Assert.Contains("Uuid.gf",JsonSerializer.Serialize(expected,Protocol.JsonOptions));
        var help=Json(model.SignatureHelp(source.Path,argument));Assert.Equal(2,help.GetProperty("activeParameter").GetInt32());
        var signature=Assert.Single(help.GetProperty("signatures").EnumerateArray());
        Assert.Equal(3,signature.GetProperty("parameters").GetArrayLength());
        string label=signature.GetProperty("label").GetString()!;
        Assert.Contains("bool",label);Assert.Contains(member,label);Assert.Contains("nothrow",label);Assert.Matches(outputPattern,label);Assert.DoesNotContain('$',label);
    }

    private static (EditorModel Model,SourceFile Source,SourceFile Library,AnalysisSnapshot Snapshot) Analyze(string text)
    {
        string root=Path.Combine(AppContext.BaseDirectory,"StandardLibrary","core");
        string[] names=["Uuid.gf","IEquatable.gf","IComparable.gf","IHashable.gf","Hashing/primitive_hashing.gf"];
        var files=names.Select(name=>Path.Combine(root,name)).Select(path=>new SourceFile(path,File.ReadAllText(path))).ToList();
        SourceFile library=files.Single(file=>Path.GetFileName(file.Path)=="Uuid.gf");
        var source=new SourceFile(Path.GetFullPath("uuid-bytes-editor.gf"),text);files.Add(source);
        var snapshot=new AnalysisSnapshot(files);
        Assert.DoesNotContain(snapshot.Diagnostics,error=>error.Severity==DiagnosticSeverity.Error);
        return(new EditorModel(snapshot,files),source,library,snapshot);
    }
    private static JsonElement Json(object? value)=>JsonSerializer.SerializeToElement(value,Protocol.JsonOptions);
    private static string Hover(EditorModel model,SourceFile source,int offset)
        =>Json(model.Hover(source.Path,offset)).GetProperty("contents").GetProperty("value").GetString()!;
}
