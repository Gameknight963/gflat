using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class NestedEnumeratorEditorTests
{
    private const string Library = """
        namespace Library {
            public interface IEnumerable<T, Iterator> { Iterator GetEnumerator(); }
            public struct Span<T> {
                public struct Enumerator {
                    public Enumerator(T* data, nuint count) {}
                }
            }
        }
        """;
    private const string Program = """
        using Library;
        struct Vector<T> : IEnumerable<T, Span<T>::Enumerator> {
            private T* data;
            private nuint count;
            public Span<T>::Enumerator GetEnumerator() => new Span<T>::Enumerator(data, count);
            public ref T this[nuint index] {
                get {
                    if (index >= count) throw new Exception("Vector index out of range");
                    return data[index];
                }
            }
        }
        """;

    private static Dictionary<int, string> Classifications(EditorModel model, SourceFile source)
    {
        var data = JsonSerializer.SerializeToElement(model.SemanticTokens(source.Path), Protocol.JsonOptions)
            .GetProperty("data").EnumerateArray().Select(value => value.GetInt32()).ToArray();
        var lineStarts = new List<int> { 0 };
        for (int i = 0; i < source.Text.Length; i++) if (source.Text[i] == '\n') lineStarts.Add(i + 1);
        var result = new Dictionary<int, string>();
        int line = 0, column = 0;
        for (int i = 0; i < data.Length; i += 5)
        {
            line += data[i];
            column = data[i] == 0 ? column + data[i + 1] : data[i + 1];
            result[lineStarts[line] + column] = EditorModel.TokenTypes[data[i + 3]];
        }
        return result;
    }

    [Theory]
    [InlineData(false, "struct")]
    [InlineData(true, "struct")]
    [InlineData(false, "class")]
    [InlineData(true, "class")]
    public void NestedGenericNamesHaveConsistentColorsHoverAndNavigation(bool instantiate, string kind)
    {
        string libraryText = Library.Replace("public struct Enumerator", $"public {kind} Enumerator", StringComparison.Ordinal);
        var library = new SourceFile(Path.GetFullPath("enumerator-library.gf"), libraryText);
        var source = new SourceFile(Path.GetFullPath("enumerator-main.gf"), Program + "\nclass Enumerator {}" +
            (instantiate ? "\nvoid Use(Vector<int>* value) { value.GetEnumerator(); }" : ""));
        var snapshot = new AnalysisSnapshot([library, source]);
        Assert.DoesNotContain(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, [library, source]);
        var tokens = Lexer.Tokenize(source, new DiagnosticBag()).Where(t => t.Text == "Enumerator" && t.Start < Program.Length).ToArray();
        Assert.Equal(3, tokens.Length);
        var classifications = Classifications(model, source);
        var declaration = model.Definition(library.Path, libraryText.IndexOf($"{kind} Enumerator", StringComparison.Ordinal) + kind.Length + 1);
        Assert.NotNull(declaration);
        foreach (var token in tokens)
        {
            Assert.True(classifications.TryGetValue(token.Start, out string? classification), $"No classification at {token.Start}");
            Assert.Equal(kind, classification);
            var hover = JsonSerializer.SerializeToElement(model.Hover(source.Path, token.Start), Protocol.JsonOptions);
            Assert.NotEqual(JsonValueKind.Null, hover.ValueKind);
            Assert.Contains(kind, hover.GetProperty("contents").GetProperty("value").GetString());
            Assert.Equal(declaration, model.Definition(source.Path, token.Start));
        }
    }

    [Fact]
    public void ActualStdVectorNestedTypesAndIndexerAreClassifiedWithoutACaller()
    {
        string root = AppContext.BaseDirectory;
        while (!Directory.Exists(Path.Combine(root, "std", "core", "Collections"))) root = Directory.GetParent(root)!.FullName;
        var files = Directory.GetFiles(Path.Combine(root, "std", "core"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path))).ToArray();
        var source = Assert.Single(files, f => Path.GetFileName(f.Path) == "Vector.gf");
        var snapshot = new AnalysisSnapshot(files);
        Assert.DoesNotContain(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, files);
        var classifications = Classifications(model, source);
        var occurrences = Lexer.Tokenize(source, new DiagnosticBag()).Where(t => t.Text == "Enumerator").ToArray();
        Assert.Equal(5, occurrences.Length);
        foreach (var token in occurrences)
        {
            Assert.Equal("struct", classifications.GetValueOrDefault(token.Start));
            Assert.NotNull(model.Hover(source.Path, token.Start));
            Assert.NotNull(model.Definition(source.Path, token.Start));
        }
        Assert.Equal("modifier", classifications.GetValueOrDefault(source.Text.IndexOf("this[", StringComparison.Ordinal)));
    }

    [Fact]
    public void IndexerThisIsAKeyword()
    {
        var library = new SourceFile(Path.GetFullPath("indexer-library.gf"), Library);
        var source = new SourceFile(Path.GetFullPath("indexer-main.gf"), Program);
        var snapshot = new AnalysisSnapshot([library, source]);
        var model = new EditorModel(snapshot, [library, source]);
        int index = Program.IndexOf("this[", StringComparison.Ordinal);
        Assert.Equal("modifier", Classifications(model, source).GetValueOrDefault(index));
    }
}
