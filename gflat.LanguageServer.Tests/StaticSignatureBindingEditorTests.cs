using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class StaticSignatureBindingEditorTests
{
    [Theory]
    [InlineData("class", false)]
    [InlineData("class", true)]
    [InlineData("struct", false)]
    [InlineData("struct", true)]
    public void BoundStaticSignaturesRetainSourceAliases(string kind, bool generic)
    {
        string library = "namespace Library { alias Word=int; public " + kind + " Box" + (generic ? "<T>" : "") + " { public static Word Identity(Word value)=>value; } }";
        string owner = "Library::Box" + (generic ? "<int>" : "");
        string program = "alias Word=long; int main(){int(int)* callback=&" + owner + "::Identity;return " + owner + "::Identity(42);}";
        var source = new SourceFile(Path.GetFullPath("static-signature-library.gf"), library);
        var caller = new SourceFile(Path.GetFullPath("static-signature-caller.gf"), program);
        var snapshot = new AnalysisSnapshot([source, caller], new() { NoStdLib=true });
        Assert.DoesNotContain(snapshot.Diagnostics, d => d.Severity==DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, [source, caller]);
        var alias = model.Definition(source.Path, library.IndexOf("Word=int", StringComparison.Ordinal));
        Assert.NotNull(alias);
        foreach (int use in new[] {
            library.IndexOf("Word Identity", StringComparison.Ordinal),
            library.IndexOf("Word value", StringComparison.Ordinal)
        }) Assert.Equal(alias, model.Definition(source.Path, use));
        Assert.NotEqual(alias, model.Definition(caller.Path, program.IndexOf("Word=long", StringComparison.Ordinal)));

        int declaration = library.IndexOf("Identity", StringComparison.Ordinal);
        foreach (int use in new[] { declaration, library.IndexOf("value)", StringComparison.Ordinal) })
        {
            string text=HoverText(model,source.Path,use);
            Assert.Contains("Word", text);
            Assert.DoesNotContain("long", text);
        }
        if (generic)
        {
            int typeParameter = library.IndexOf("T>", StringComparison.Ordinal);
            Assert.Contains("T", HoverText(model,source.Path,typeParameter));
        }
        else
        {
            foreach (int use in new[] { program.IndexOf("Identity", StringComparison.Ordinal), program.LastIndexOf("Identity", StringComparison.Ordinal) })
            {
                Assert.Contains("Word", HoverText(model,caller.Path,use));
                Assert.Equal(model.Definition(source.Path, declaration), model.Definition(caller.Path, use));
            }
            var signature = JsonSerializer.SerializeToElement(model.SignatureHelp(caller.Path, program.LastIndexOf("Identity(", StringComparison.Ordinal)+"Identity(".Length), Protocol.JsonOptions);
            Assert.Contains("Word", signature.GetProperty("signatures")[0].GetProperty("label").GetString());
        }
    }

    private static string HoverText(EditorModel model,string path,int offset)
    {
        var payload=model.Hover(path,offset);
        Assert.NotNull(payload);
        return JsonSerializer.SerializeToElement(payload,Protocol.JsonOptions)
            .GetProperty("contents").GetProperty("value").GetString()!;
    }
}
