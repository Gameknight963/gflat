using System.Text.Json;
using gflat.ast;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

// Tests the actual standard-library Math overloads.
public sealed class FloatingStepEditorTests
{
    [Theory]
    [InlineData("BitIncrement","float","1.0f")]
    [InlineData("BitIncrement","double","1.0")]
    [InlineData("BitDecrement","float","1.0f")]
    [InlineData("BitDecrement","double","1.0")]
    public void ActualMathStepsSelectLiteralPrecisionAndPresentBothNoThrowOverloads(string member,string type,string literal)
    {
        string path=Path.Combine(AppContext.BaseDirectory,"StandardLibrary","core","Numerics","Math.gf");
        var library=new SourceFile(path,File.ReadAllText(path));
        string text=type+" Probe() nothrow=>std::Math::"+member+"("+literal+");";
        var source=new SourceFile(Path.GetFullPath("floating-step-editor.gf"),text);
        SourceFile[] sources=[library,source];
        var snapshot=new AnalysisSnapshot(sources);
        Assert.DoesNotContain(snapshot.Diagnostics,error=>error.Severity==DiagnosticSeverity.Error);
        var call=Assert.Single(snapshot.Nodes.Where(node=>node.Span.SourceId==source.Id)
            .Select(node=>node.Node).OfType<CallExpression>());
        Assert.Equal(type,TypeChecker.TypeName(snapshot.Checker!.GetType(call)));
        var model=new EditorModel(snapshot,sources);
        int use=text.IndexOf(member,StringComparison.Ordinal);
        var completion=Json(model.Completion(source.Path,use));
        Assert.Contains(completion.GetProperty("items").EnumerateArray(),item=>item.GetProperty("label").GetString()==member);
        string hover=Json(model.Hover(source.Path,use)).GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains(member,hover);Assert.Contains(type+" value",hover);Assert.Contains("nothrow",hover);
        Assert.DoesNotContain('$',hover);

        string declaration=type+" "+member+"(";
        int declarationStart=library.Text.IndexOf(declaration,StringComparison.Ordinal);
        Assert.True(declarationStart>=0,"Missing actual Math "+declaration);
        int declarationName=declarationStart+type.Length+1;
        var expected=model.Definition(library.Path,declarationName);Assert.NotNull(expected);
        Assert.Equal(expected,model.Definition(source.Path,use));
        Assert.Contains("Math.gf",JsonSerializer.Serialize(expected,Protocol.JsonOptions));

        var help=Json(model.SignatureHelp(source.Path,text.IndexOf(literal,use,StringComparison.Ordinal)+1));
        var signatures=help.GetProperty("signatures");Assert.Equal(2,signatures.GetArrayLength());
        string active=signatures[help.GetProperty("activeSignature").GetInt32()].GetProperty("label").GetString()!;
        Assert.Contains(member,active);Assert.Contains(type+" value",active);
        Assert.Equal(0,help.GetProperty("activeParameter").GetInt32());
        foreach(string precision in new[]{"float","double"})
            Assert.Contains(signatures.EnumerateArray(),item=>item.GetProperty("label").GetString()!.Contains(precision+" value",StringComparison.Ordinal));
        foreach(var signature in signatures.EnumerateArray())
            Assert.DoesNotContain('$',signature.GetProperty("label").GetString()!);
    }

    private static JsonElement Json(object? value)=>JsonSerializer.SerializeToElement(value,Protocol.JsonOptions);
}
