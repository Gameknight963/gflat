using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class MacroClassificationEditorTests
{
    private static (EditorModel Model, AnalysisSnapshot Snapshot, SourceFile Source) Analyze(string code)
    {
        var source = new SourceFile(Path.GetFullPath("macro-classifications.gf"), code);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        return (new(snapshot, [source]), snapshot, source);
    }

    private static Dictionary<int, string> Classifications(EditorModel model, SourceFile source)
    {
        int[] data = JsonSerializer.SerializeToElement(model.SemanticTokens(source.Path), Protocol.JsonOptions)
            .GetProperty("data").EnumerateArray().Select(value => value.GetInt32()).ToArray();
        var starts = new List<int> { 0 };
        for (int i = 0; i < source.Text.Length; i++) if (source.Text[i] == '\n') starts.Add(i + 1);
        var result = new Dictionary<int, string>();
        int line = 0, column = 0, end = -1;
        for (int i = 0; i < data.Length; i += 5)
        {
            Assert.True(data[i] >= 0);
            column = data[i] == 0 ? column + data[i + 1] : data[i + 1];
            line += data[i];
            int start = starts[line] + column;
            Assert.True(start >= end, "Semantic tokens overlap");
            Assert.True(column + data[i + 2] <= (line + 1 < starts.Count ? starts[line + 1] - starts[line] - 1 : source.Text.Length - starts[line]));
            end = start + data[i + 2];
            result.Add(start, EditorModel.VisualStudioTokenTypes[data[i + 3]]);
        }
        return result;
    }

    private static JsonElement Hover(EditorModel model, SourceFile source, int offset)
        => JsonSerializer.SerializeToElement(model.Hover(source.Path, offset,
            new VisualStudioHover(JsonSerializer.SerializeToElement(new { }))), Protocol.JsonOptions).GetProperty("_vs_rawContent");

    private static JsonElement[] DeclarationRuns(JsonElement hover)
        => hover.GetProperty("Elements")[0].GetProperty("Elements")[0].GetProperty("Runs").EnumerateArray().ToArray();

    [Fact]
    public void NumericDefinitionsAndExpansionsRetainTheSourceClassification()
    {
        const string code = "#define VERSION 12\nint main()=>VERSION;";
        var (model, _, source) = Analyze(code);
        string numberClass = Classifications(model, source)[code.IndexOf("12", StringComparison.Ordinal)];
        Assert.Equal("number", numberClass);
        var declaration = Hover(model, source, code.IndexOf("VERSION", StringComparison.Ordinal));
        var number = Assert.Single(DeclarationRuns(declaration), run => run.GetProperty("Text").GetString() == "12");
        Assert.Equal(numberClass, number.GetProperty("ClassificationTypeName").GetString());
        Assert.Equal(16, number.GetProperty("Style").GetInt32());
        var use = Hover(model, source, code.LastIndexOf("VERSION", StringComparison.Ordinal));
        var expansion = use.GetProperty("Elements")[1].GetProperty("Runs").EnumerateArray();
        var expandedNumber = Assert.Single(expansion, run => run.GetProperty("Text").GetString() == "12");
        Assert.Equal(numberClass, expandedNumber.GetProperty("ClassificationTypeName").GetString());
        Assert.Equal(8, expandedNumber.GetProperty("Style").GetInt32());
    }

    [Theory]
    [InlineData("#define JOIN(left, right) left##right\nint main()=>0;")]
    [InlineData("#define left 12\n#define JOIN(left, right) left##right\nint main()=>0;")]
    [InlineData("#define JOIN(left, \\\nright) left \\\n## right\nint main()=>0;")]
    public void MacroParametersAndPasteOperatorsHaveMatchingSourceAndTooltipClassifications(string code)
    {
        var (model, _, source) = Analyze(code);
        var classifications = Classifications(model, source);
        int join = code.IndexOf("JOIN", StringComparison.Ordinal);
        foreach (var token in Lexer.Tokenize(source, new DiagnosticBag()).Where(t => t.Start > join && t.Text is "left" or "right"))
            Assert.Equal("identifier", classifications[token.Start]);
        var runs = DeclarationRuns(Hover(model, source, join));
        Assert.All(runs.Where(run => run.GetProperty("Text").GetString() is "left" or "right"),
            run => Assert.Equal("identifier", run.GetProperty("ClassificationTypeName").GetString()));
        Assert.Equal("operator", classifications[code.IndexOf("##", StringComparison.Ordinal)]);
        Assert.Contains(runs, run => run.GetProperty("Text").GetString() == "##" && run.GetProperty("ClassificationTypeName").GetString() == "operator");
        Assert.DoesNotContain(runs, run => run.GetProperty("ClassificationTypeName").GetString() == "parameter name");
    }

    [Fact]
    public void TheWholeDefinitionUsesTheDescriptorTokensInBothPresentations()
    {
        const string code = "#define VALUE 7\n#define BODY(arg) if (arg) return VALUE + 42;\n#define TEXT(arg) #arg\n#define LITERAL \"VALUE\"\n#define RAW @VALUE\nint main()=>99;";
        var (model, snapshot, source) = Analyze(code);
        var sourceKinds = Classifications(model, source);
        foreach (var symbol in snapshot.MacroSymbols.Where(s => s.Definition))
        {
            var runs = DeclarationRuns(Hover(model, source, symbol.Span.Start));
            Assert.Equal(symbol.Signature, string.Concat(runs.Select(run => run.GetProperty("Text").GetString())));
            foreach (var token in symbol.DeclarationTokens!.Where(t => t.Span.Source != null))
            {
                string expected = EditorModel.VisualStudioTokenTypes[Array.IndexOf(EditorModel.TokenTypes, token.Classification)];
                Assert.Equal(expected, sourceKinds[token.Span.Start]);
                Assert.Contains(runs, run => run.GetProperty("Text").GetString() == token.Text && run.GetProperty("ClassificationTypeName").GetString() == expected);
            }
        }
        Assert.DoesNotContain(code.IndexOf("99", StringComparison.Ordinal), sourceKinds.Keys);
    }
}
