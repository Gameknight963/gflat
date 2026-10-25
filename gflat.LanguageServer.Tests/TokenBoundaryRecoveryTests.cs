using System.Text.Json;
using gflat.diagnostics;
using Xunit.Sdk;

namespace gflat.LanguageServer.Tests;

public sealed class TokenBoundaryRecoveryTests
{
    private const string Editable = """
        namespace Broken {
            public struct Data { public int value; }
            // The insertion corpus uses lexer starts, never comment/string interiors.
            public int Change(int input) {
                readonly(char)* text = "unchanged";
                int local = input + 1;
                if (local > 2) { local = local - 1; }
                return local;
            }
        }
        """;

    private const string Healthy = """
        namespace Safe {
            public class Marker { public int Read(int input) => input; }
            public int Echo(int argument) { int local = argument; return local; }
        }
        int Healthy() { int result = Safe::Echo(42); return result; }
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidTokenInsertionsPreserveIndependentFileServicesInBothFileOrders(bool healthyFirst)
    {
        var healthy = new SourceFile(Path.GetFullPath("healthy-boundary-recovery.gf"), Healthy);
        var original = new SourceFile(Path.GetFullPath("editable-boundary-recovery.gf"), Editable);
        SourceFile[] Order(SourceFile editable) => healthyFirst ? [healthy, editable] : [editable, healthy];
        SourceFile[] baselineFiles = Order(original);
        var baseline = new AnalysisSnapshot(baselineFiles, new() { NoStdLib = true });
        Assert.DoesNotContain(baseline.Diagnostics, error => error.Severity == DiagnosticSeverity.Error);
        var baselineModel = new EditorModel(baseline, baselineFiles);
        string baselineTokens = Json(baselineModel.SemanticTokens(healthy.Path)).GetRawText();
        var baselineColors = Colors(baselineModel, healthy);
        int namespaceUse = Healthy.LastIndexOf("Safe::", StringComparison.Ordinal);
        int classDeclaration = Healthy.IndexOf("Marker", StringComparison.Ordinal);
        int methodUse = Healthy.LastIndexOf("Echo(", StringComparison.Ordinal);
        int localUse = Healthy.LastIndexOf("result", StringComparison.Ordinal);
        Assert.Equal("namespace", baselineColors[namespaceUse]);
        Assert.Equal("class", baselineColors[classDeclaration]);
        Assert.Equal("method", baselineColors[methodUse]);
        Assert.Equal("variable", baselineColors[localUse]);
        int[] healthySites = [namespaceUse, classDeclaration, methodUse, localUse];
        string[] hovers = healthySites.Select(site => RequiredHover(baselineModel, healthy, site)).ToArray();
        string[] definitions = healthySites.Select(site => RequiredDefinition(baselineModel, healthy, site)).ToArray();
        string localCompletion = CompletionItem(baselineModel, healthy, localUse, "result");

        int firstBrace = Editable.IndexOf('{');
        int finalBrace = Editable.LastIndexOf('}');
        int[] boundaries = Lexer.Tokenize(original)
            .Where(token => token.Kind != TokenKind.EndOfFile && token.Kind != TokenKind.StringLiteral
                && token.Kind != TokenKind.CharLiteral && token.Start > firstBrace && token.Start < finalBrace)
            .Select(token => token.Start).Distinct().ToArray();
        Assert.True(boundaries.Length >= 24);
        int[] points = Enumerable.Range(0, 24).Select(index => boundaries[index * boundaries.Length / 24]).ToArray();
        Assert.Equal(24, points.Distinct().Count());

        foreach (int offset in points)
        foreach (string inserted in new[] { " @ ", " ) " })
        {
            string context = $"healthyFirst={healthyFirst}, offset={offset}, inserted={inserted.Trim()}";
            try
            {
                var edited = new SourceFile(original.Path, Editable.Insert(offset, inserted));
                SourceFile[] files = Order(edited);
                var snapshot = new AnalysisSnapshot(files, new() { NoStdLib = true });
                Assert.Contains(snapshot.Diagnostics, error => error.Severity == DiagnosticSeverity.Error && error.FilePath == edited.Path);
                Assert.DoesNotContain(snapshot.Diagnostics, error => error.Severity == DiagnosticSeverity.Error && error.FilePath == healthy.Path);
                var model = new EditorModel(snapshot, files);
                Assert.Equal(baselineTokens, Json(model.SemanticTokens(healthy.Path)).GetRawText());
                for (int index = 0; index < healthySites.Length; index++)
                {
                    Assert.Equal(hovers[index], RequiredHover(model, healthy, healthySites[index]));
                    Assert.Equal(definitions[index], RequiredDefinition(model, healthy, healthySites[index]));
                }
                Assert.Equal(localCompletion, CompletionItem(model, healthy, localUse, "result"));
                ValidateTokenRanges(model, healthy);
                ValidateTokenRanges(model, edited);
                // Broken-file declarations need not survive; these queries must still be safe.
                model.Hover(edited.Path, offset + 1);
                model.Definition(edited.Path, offset + 1);
                model.Completion(edited.Path, offset + 1);
                model.SignatureHelp(edited.Path, offset + 1);
            }
            catch (Exception error)
            {
                throw new XunitException(context + Environment.NewLine + error);
            }
        }
    }

    private static string RequiredHover(EditorModel model, SourceFile source, int offset)
    {
        // Recovery intentionally suppresses layout details after any diagnostic error.
        object? hover = model.Hover(source.Path, offset, options: new HoverOptions(ShowTypeLayout: false));
        Assert.NotNull(hover);
        return Json(hover).GetRawText();
    }

    private static string RequiredDefinition(EditorModel model, SourceFile source, int offset)
    {
        object? definition = model.Definition(source.Path, offset);
        Assert.NotNull(definition);
        return Json(definition).GetRawText();
    }

    private static string CompletionItem(EditorModel model, SourceFile source, int offset, string label)
    {
        var items = Json(model.Completion(source.Path, offset)).GetProperty("items").EnumerateArray();
        return Assert.Single(items, item => item.GetProperty("label").GetString() == label).GetRawText();
    }

    private static Dictionary<int, string> Colors(EditorModel model, SourceFile source)
    {
        var result = new Dictionary<int, string>();
        foreach (var token in Tokens(model, source)) result[token.Start] = EditorModel.TokenTypes[token.Type];
        return result;
    }

    private static void ValidateTokenRanges(EditorModel model, SourceFile source)
    {
        int previousEnd = 0;
        foreach (var token in Tokens(model, source))
        {
            Assert.True(token.Length > 0);
            Assert.InRange(token.Start, previousEnd, source.Text.Length);
            Assert.InRange(token.Length, 1, source.Text.Length - token.Start);
            Assert.InRange(token.Type, 0, EditorModel.TokenTypes.Length - 1);
            Assert.DoesNotContain('\r', source.Text.Substring(token.Start, token.Length));
            Assert.DoesNotContain('\n', source.Text.Substring(token.Start, token.Length));
            previousEnd = token.Start + token.Length;
        }
    }

    private static IEnumerable<(int Start, int Length, int Type)> Tokens(EditorModel model, SourceFile source)
    {
        int[] data = Json(model.SemanticTokens(source.Path)).GetProperty("data").EnumerateArray()
            .Select(value => value.GetInt32()).ToArray();
        Assert.Equal(0, data.Length % 5);
        int[] lineStarts = [0, .. source.Text.Select((value, index) => (value, index))
            .Where(item => item.value == '\n').Select(item => item.index + 1)];
        int line = 0;
        int column = 0;
        for (int index = 0; index < data.Length; index += 5)
        {
            Assert.True(data[index] >= 0 && data[index + 1] >= 0 && data[index + 4] >= 0);
            line += data[index];
            column = data[index] == 0 ? column + data[index + 1] : data[index + 1];
            Assert.InRange(line, 0, lineStarts.Length - 1);
            int lineEnd = line + 1 < lineStarts.Length ? lineStarts[line + 1] : source.Text.Length;
            while (lineEnd > lineStarts[line] && (source.Text[lineEnd - 1] == '\r' || source.Text[lineEnd - 1] == '\n')) lineEnd--;
            Assert.InRange(column, 0, lineEnd - lineStarts[line]);
            Assert.InRange(data[index + 2], 1, lineEnd - lineStarts[line] - column);
            yield return (lineStarts[line] + column, data[index + 2], data[index + 3]);
        }
    }

    private static JsonElement Json(object? value)
        => JsonSerializer.SerializeToElement(value, Protocol.JsonOptions);
}
