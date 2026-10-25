using System.Text.Json;
using gflat.ast;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class CheckedCommonMultipleEditorTests
{
    [Fact]
    public void ThreeCheckedUnsignedOverloadsKeepTheirConcreteResultsAndDefinitions()
    {
        string path=Path.Combine(AppContext.BaseDirectory,"StandardLibrary","core","Math.gf");
        var library=new SourceFile(path,File.ReadAllText(path));
        const string text="""
            uint Narrow()=>std::Math::LeastCommonMultiple(6u,8u);
            ulong Wide()=>std::Math::LeastCommonMultiple(6ul,8ul);
            nuint Native()=>std::Math::LeastCommonMultiple((nuint)6,(nuint)8);
            """;
        var source=new SourceFile(Path.GetFullPath("checked-common-multiple-editor.gf"),text);
        SourceFile[] files=[library,source];
        var snapshot=new AnalysisSnapshot(files);
        Assert.DoesNotContain(snapshot.Diagnostics,error=>error.Severity==DiagnosticSeverity.Error);
        var calls=snapshot.Nodes.Where(node=>node.Span.SourceId==source.Id).Select(node=>node.Node)
            .OfType<CallExpression>().OrderBy(call=>call.Span.Start).ToArray();
        Assert.Equal(3,calls.Length);
        var model=new EditorModel(snapshot,files);
        string[] types=["uint","ulong","nuint"];
        int offset=0;
        for(int i=0;i<types.Length;i++)
        {
            string type=types[i];
            Assert.Equal(type,TypeChecker.TypeName(snapshot.Checker!.GetType(calls[i])));
            int use=text.IndexOf("LeastCommonMultiple(",offset,StringComparison.Ordinal);
            Assert.True(use>=0);offset=use+"LeastCommonMultiple".Length;
            Assert.Single(Json(model.Completion(source.Path,use)).GetProperty("items").EnumerateArray(),
                item=>item.GetProperty("label").GetString()=="LeastCommonMultiple");
            string hover=Json(model.Hover(source.Path,use)).GetProperty("contents").GetProperty("value").GetString()!;
            Assert.Matches(@"\b"+type+@" a\b",hover);
            Assert.Matches(@"\b"+type+@" b\b",hover);
            Assert.DoesNotContain("nothrow",hover);
            Assert.DoesNotContain('$',hover);
            string anchor="public static "+type+" LeastCommonMultiple(";
            int declaration=library.Text.IndexOf(anchor,StringComparison.Ordinal);
            Assert.True(declaration>=0);
            var expected=model.Definition(library.Path,declaration+("public static "+type+" ").Length);
            Assert.NotNull(expected);
            Assert.Equal(expected,model.Definition(source.Path,use));
            int comma=text.IndexOf(',',use);
            Assert.True(comma>use);
            var help=Json(model.SignatureHelp(source.Path,comma+1));
            Assert.Equal(1,help.GetProperty("activeParameter").GetInt32());
            var signatures=help.GetProperty("signatures");
            Assert.Equal(3,signatures.GetArrayLength());
            var active=signatures[help.GetProperty("activeSignature").GetInt32()];
            Assert.Equal(2,active.GetProperty("parameters").GetArrayLength());
            Assert.Matches(@"\b"+type+@" a\b",active.GetProperty("label").GetString()!);
            Assert.Matches(@"\b"+type+@" b\b",active.GetProperty("label").GetString()!);
        }
    }

    private static JsonElement Json(object? value)
        => JsonSerializer.SerializeToElement(value,Protocol.JsonOptions);
}
