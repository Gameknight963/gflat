using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

// Analyze the actual std method through an immutable receiver.
public sealed class BigIntegerTryFormatEditorTests
{
    [Fact]
    public void ImmutableReceiverExposesNoThrowTryFormatWithItsConcreteOutputSignatureAndDefinition()
    {
        const string text="using std; using std::Numerics;void Probe(){BigInteger value=new BigInteger(123l);immutable(BigInteger)* view=&value;char[32] output;nuint written=0;bool success=view.TryFormat(output,32,ref written);}";
        var files=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"StandardLibrary"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).ToList();
        var library=files.Single(file=>Path.GetFileName(file.Path)=="BigInteger.gf");
        var source=new SourceFile(Path.GetFullPath("big-integer-try-format-editor.gf"),text);files.Add(source);
        var snapshot=new AnalysisSnapshot(files,new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,error=>error.Severity==DiagnosticSeverity.Error);
        var model=new EditorModel(snapshot,files);
        int call=text.IndexOf("TryFormat",StringComparison.Ordinal);
        Assert.Single(Json(model.Completion(source.Path,call)).GetProperty("items").EnumerateArray(),
            item=>item.GetProperty("label").GetString()=="TryFormat");
        string hover=Json(model.Hover(source.Path,call)).GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("bool",hover);Assert.Contains("TryFormat(",hover);
        Assert.Contains("immutable",hover);Assert.Contains("nothrow",hover);
        Assert.Contains("char* output",hover);Assert.Contains("nuint capacity",hover);Assert.Contains("ref nuint written",hover);
        Assert.DoesNotContain('$',hover);

        const string declaration="public immutable bool TryFormat(";
        int start=library.Text.IndexOf(declaration,StringComparison.Ordinal);Assert.True(start>=0);
        int declaredName=start+"public immutable bool ".Length;
        var expected=model.Definition(library.Path,declaredName);Assert.NotNull(expected);
        Assert.Equal(expected,model.Definition(source.Path,call));
        Assert.Contains("BigInteger.gf",JsonSerializer.Serialize(expected,Protocol.JsonOptions));

        int outputArgument=text.IndexOf("ref written",call,StringComparison.Ordinal)+"ref ".Length;
        var help=Json(model.SignatureHelp(source.Path,outputArgument));
        Assert.Equal(2,help.GetProperty("activeParameter").GetInt32());
        var signature=Assert.Single(help.GetProperty("signatures").EnumerateArray());
        Assert.Equal(3,signature.GetProperty("parameters").GetArrayLength());
        string label=signature.GetProperty("label").GetString()!;
        // The callable label may include its qualified owner before the method name.
        Assert.Contains("bool",label);Assert.Contains("TryFormat(",label);Assert.Contains("nothrow",label);
        Assert.Contains("char* output",label);Assert.Contains("nuint capacity",label);Assert.Contains("ref nuint written",label);
        Assert.DoesNotContain('$',label);
    }

    private static JsonElement Json(object? value)=>JsonSerializer.SerializeToElement(value,Protocol.JsonOptions);
}
