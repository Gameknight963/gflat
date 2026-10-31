using System.Text.Json;
using System.Text.RegularExpressions;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class RecursiveTypeProtectionEditorTests
{
    [Fact]
    public void CheckedParameterAndMethodHoversAndSignatureHelpPreserveNestedProtection()
    {
        const string text = """
            struct Box { public int Field; }
            void Inspect(readonly(readonly(int)*[2]) pointers, readonly(immutable(Box)[2]) values) {}
            void Forward(readonly(readonly(int)*[2]) pointers, readonly(immutable(Box)[2]) values)
            {
                Inspect(pointers, values);
            }
            """;
        var source = new SourceFile(Path.GetFullPath("recursive-type-protection-editor.gf"), text);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.NotNull(snapshot.Checker);
        var model = new EditorModel(snapshot, [source]);

        const string pointerParameter = "readonly(readonly(int)*[2]) pointers";
        const string valueParameter = "readonly(immutable(Box)[2]) values";
        int pointerDeclaration = text.IndexOf("pointers", StringComparison.Ordinal);
        int valueDeclaration = text.IndexOf("values", StringComparison.Ordinal);
        Assert.Contains(pointerParameter, Hover(model, source, pointerDeclaration));
        Assert.Contains(valueParameter, Hover(model, source, valueDeclaration));

        int call = text.LastIndexOf("Inspect(", StringComparison.Ordinal);
        string methodHover = Hover(model, source, call);
        Assert.Contains(pointerParameter, methodHover);
        Assert.Contains(valueParameter, methodHover);
        Assert.DoesNotContain('$', methodHover);

        int secondArgument = text.IndexOf("values", call, StringComparison.Ordinal);
        Assert.Contains(pointerParameter, Hover(model, source, text.IndexOf("pointers", call, StringComparison.Ordinal)));
        Assert.Contains(valueParameter, Hover(model, source, secondArgument));
        var help = Json(model.SignatureHelp(source.Path, secondArgument));
        var signature = Assert.Single(help.GetProperty("signatures").EnumerateArray());
        string label = signature.GetProperty("label").GetString()!;
        Assert.Contains(pointerParameter, label);
        Assert.Contains(valueParameter, label);
        Assert.Equal(1, help.GetProperty("activeParameter").GetInt32());
        Assert.Equal(2, signature.GetProperty("parameters").GetArrayLength());

        var rich = Json(model.Hover(source.Path, call,
            new VisualStudioHover(JsonSerializer.SerializeToElement(new { }))));
        var runs = rich.GetProperty("_vs_rawContent").GetProperty("Elements")[0].GetProperty("Elements")
            .EnumerateArray().Single(element => element.GetProperty("_vs_type").GetString() == "ClassifiedTextElement")
            .GetProperty("Runs").EnumerateArray().ToArray();
        var qualifiers = runs.SelectMany(run => Regex.Matches(run.GetProperty("Text").GetString()!,
                @"\b(?:readonly|immutable)\b").Cast<Match>()
            .Select(match => (Text: match.Value, Classification: run.GetProperty("ClassificationTypeName").GetString())))
            .ToArray();
        Assert.Equal(3, qualifiers.Count(part => part.Text == "readonly"));
        Assert.Single(qualifiers, part => part.Text == "immutable");
        Assert.All(qualifiers, part => Assert.Equal("keyword", part.Classification));
    }

    private static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value, Protocol.JsonOptions);
    private static string Hover(EditorModel model, SourceFile source, int offset)
        => Json(model.Hover(source.Path, offset)).GetProperty("contents").GetProperty("value").GetString()!;
}
