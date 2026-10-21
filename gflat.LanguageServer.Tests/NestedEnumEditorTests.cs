using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class NestedEnumEditorTests
{
    [Theory]
    [InlineData("class")]
    [InlineData("static class")]
    [InlineData("struct")]
    public void NestedEnumUsesHaveHoverNavigationAndEnumClassification(string owner)
    {
        string text = $$"""
            using Library;
            namespace Library {
                public {{owner}} Codec {
                    public enum Status : byte { Done = 7 }
                    public static Status Read() => Status::Done;
                }
            }
            int main() {
                Codec::Status status = Codec::Read();
                if (status == Library::Codec::Status::Done) return 0;
                return 1;
            }
            """;
        var source = new SourceFile(Path.GetFullPath("nested-enum-editor.gf"), text);
        var snapshot = new AnalysisSnapshot([source]);
        Assert.DoesNotContain(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, [source]);
        var classifications = Classifications(model, source);
        int declaration = text.IndexOf("enum Status", StringComparison.Ordinal) + "enum ".Length;
        var definition = model.Definition(source.Path, declaration);
        Assert.NotNull(definition);
        foreach (var token in Lexer.Tokenize(source, new DiagnosticBag()).Where(t => t.Text == "Status"))
        {
            Assert.Equal("enum", classifications.GetValueOrDefault(token.Start));
            var hover = JsonSerializer.SerializeToElement(model.Hover(source.Path, token.Start), Protocol.JsonOptions);
            Assert.NotEqual(JsonValueKind.Null, hover.ValueKind);
            string description = hover.GetProperty("contents").GetProperty("value").GetString()!;
            Assert.Contains("Status", description);
            Assert.DoesNotContain("$", description);
            Assert.Equal(definition, model.Definition(source.Path, token.Start));
        }
    }

    private static Dictionary<int, string> Classifications(EditorModel model, SourceFile source)
    {
        var data = JsonSerializer.SerializeToElement(model.SemanticTokens(source.Path), Protocol.JsonOptions)
            .GetProperty("data").EnumerateArray().Select(value => value.GetInt32()).ToArray();
        var starts = new List<int> { 0 };
        for (int i = 0; i < source.Text.Length; i++) if (source.Text[i] == '\n') starts.Add(i + 1);
        var result = new Dictionary<int, string>();
        int line = 0, column = 0;
        for (int i = 0; i < data.Length; i += 5)
        {
            line += data[i];
            column = data[i] == 0 ? column + data[i + 1] : data[i + 1];
            result[starts[line] + column] = EditorModel.TokenTypes[data[i + 3]];
        }
        return result;
    }
}
