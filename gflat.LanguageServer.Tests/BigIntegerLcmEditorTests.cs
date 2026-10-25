using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

// Uses the actual BigInteger source and its dependencies.
public sealed class BigIntegerLcmEditorTests
{
    [Fact]
    public void StaticLcmBorrowsImmutableInputsAndPresentsItsOwningResultAndSourceSignature()
    {
        const string text="""
            using std; using std::Queries; using std::Collections;
            int Probe(){
                immutable(BigInteger) first=new(12l);
                immutable(BigInteger) second=new(18l);
                BigInteger result=BigInteger::LeastCommonMultiple(&first,&second);
                return result.Sign;
            }
            """;
        string root=Path.Combine(AppContext.BaseDirectory,"StandardLibrary","core");
        string[] names=["IStringConvertable.gf","IHashable.gf","IEquatable.gf","IComparable.gf","Hashing.gf",
            "String.gf","ByteSearch.gf","Formatting.gf","Collections/Vector.gf","Span.gf",
            "Collections/IEnumerable.gf","Queries/Enumerable.gf","Numerics/BigInteger.gf"];
        var sources=names.Select(name=>Path.Combine(root,name.Replace('/',Path.DirectorySeparatorChar)))
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).ToList();
        var source=new SourceFile(Path.GetFullPath("bigint-lcm-editor.gf"),text);sources.Add(source);
        var snapshot=new AnalysisSnapshot(sources);
        Assert.DoesNotContain(snapshot.Diagnostics,error=>error.Severity==DiagnosticSeverity.Error);
        var model=new EditorModel(snapshot,sources);
        int call=text.IndexOf("LeastCommonMultiple",StringComparison.Ordinal);
        var completion=Json(model.Completion(source.Path,call));
        Assert.Contains(completion.GetProperty("items").EnumerateArray(),item=>item.GetProperty("label").GetString()=="LeastCommonMultiple");
        string hover=Hover(model,source,call);
        Assert.Contains("BigInteger",hover);Assert.Contains("LeastCommonMultiple(",hover);
        Assert.Contains("immutable(BigInteger)* first",hover);Assert.Contains("immutable(BigInteger)* second",hover);
        Assert.DoesNotContain('$',hover);
        SourceFile library=sources.Single(file=>Path.GetFileName(file.Path)=="BigInteger.gf");
        int declaration=library.Text.IndexOf("LeastCommonMultiple(",StringComparison.Ordinal);Assert.True(declaration>=0);
        var expected=model.Definition(library.Path,declaration);Assert.NotNull(expected);
        Assert.Equal(expected,model.Definition(source.Path,call));
        Assert.Contains("BigInteger.gf",JsonSerializer.Serialize(expected,Protocol.JsonOptions));
        int argument=text.IndexOf("&second",call,StringComparison.Ordinal);
        var help=Json(model.SignatureHelp(source.Path,argument));
        var signature=Assert.Single(help.GetProperty("signatures").EnumerateArray());
        string label=signature.GetProperty("label").GetString()!;
        Assert.Contains("BigInteger",label);Assert.Contains("LeastCommonMultiple(",label);
        // Signature-help labels may qualify types that hover shortens in context.
        Assert.Matches(@"immutable\((?:std::)?BigInteger\)\* first",label);
        Assert.Matches(@"immutable\((?:std::)?BigInteger\)\* second",label);
        Assert.DoesNotContain('$',label);Assert.Equal(1,help.GetProperty("activeParameter").GetInt32());
        Assert.Equal(2,signature.GetProperty("parameters").GetArrayLength());
        int result=text.LastIndexOf("result",StringComparison.Ordinal);
        Assert.Contains("BigInteger result",Hover(model,source,result));
        var resultDeclaration=model.Definition(source.Path,text.IndexOf("result=",StringComparison.Ordinal));
        Assert.NotNull(resultDeclaration);Assert.Equal(resultDeclaration,model.Definition(source.Path,result));
    }

    private static JsonElement Json(object? value)=>JsonSerializer.SerializeToElement(value,Protocol.JsonOptions);
    private static string Hover(EditorModel model,SourceFile source,int offset)
        =>Json(model.Hover(source.Path,offset)).GetProperty("contents").GetProperty("value").GetString()!;
}
