using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class ClosedGenericInterfaceDisplayTests
{
    [Fact]
    public void ActualStandardInterfaceViewsBindParametersAndReturnTypesWithoutChangingOpenContexts()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "StandardLibrary");
        var sources = Directory.GetFiles(directory, "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path))).ToList();
        const string text = """
            using std;
            nuint Hash<T : IHashable>(ref readonly(T) value) => value.GetHashCode();
            bool Equal<T : IEquatable<T>>(ref readonly(T) value, readonly(T)* other) => value.Equals(other);
            Iterator Enumerate<T, Iterator>(IEnumerable<T, Iterator>* sequence) => sequence.GetEnumerator();
            int main() {
                int value = 42; int other = 42;
                Hash(ref value); Equal(ref value, &other);
                IHashable* hashing = &value; hashing.GetHashCode();
                IEquatable<int>* equality = &value; equality.Equals(&other);
                int[1] buffer = [42]; Span<int> span = new(buffer, 1);
                IEnumerable<int, Span<int>::Enumerator>* sequence = &span;
                Span<int>::Enumerator iterator = sequence.GetEnumerator();
                return 0;
            }
            """;
        var source = new SourceFile(Path.GetFullPath("std-editor/closed-generic-interface.gf"), text);
        sources.Add(source);
        var snapshot = new AnalysisSnapshot(sources, new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, sources);
        foreach (var (marker, expected, file, owner) in new[] {
            ("value.GetHashCode", "GetHashCode()", "IHashable.gf", "IHashable"),
            ("hashing.GetHashCode", "GetHashCode()", "IHashable.gf", "IHashable"),
            ("value.Equals", "readonly(T)* other", "IEquatable.gf", "IEquatable"),
            ("equality.Equals", "readonly(int)* other", "IEquatable.gf", "IEquatable<int>"),
            ("sequence.GetEnumerator", "Iterator", "IEnumerable.gf", "IEnumerable")
        })
        {
            int offset = text.IndexOf(marker, StringComparison.Ordinal) + marker.IndexOf('.') + 1;
            AssertDisplay(model, source, offset, expected, file, owner);
        }
        int closedIterator = text.LastIndexOf("sequence.GetEnumerator", StringComparison.Ordinal) + "sequence.".Length;
        AssertDisplay(model, source, closedIterator, "Span<int>::Enumerator", "IEnumerable.gf", "IEnumerable<int,");
    }

    private static void AssertDisplay(EditorModel model, SourceFile source, int offset, string expected, string file, string owner)
    {
        var hover = JsonSerializer.SerializeToElement(model.Hover(source.Path, offset), Protocol.JsonOptions);
        string signature = hover.GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains(expected, signature);
        Assert.Contains(owner, signature);
        Assert.DoesNotContain('$', signature);
        var definition = JsonSerializer.SerializeToElement(model.Definition(source.Path, offset), Protocol.JsonOptions);
        Assert.EndsWith(file, new Uri(definition.GetProperty("uri").GetString()!).LocalPath);
        string method = source.Text[offset..].Split('(')[0];
        var completions = JsonSerializer.SerializeToElement(model.Completion(source.Path, offset), Protocol.JsonOptions);
        var completion = Assert.Single(completions.GetProperty("items").EnumerateArray(), item => item.GetProperty("label").GetString() == method);
        Assert.Contains(expected, completion.GetProperty("detail").GetString());
        Assert.Contains(owner, completion.GetProperty("detail").GetString());
        int arguments = source.Text.IndexOf('(', offset) + 1;
        var help = JsonSerializer.SerializeToElement(model.SignatureHelp(source.Path, arguments), Protocol.JsonOptions);
        var selected = Assert.Single(help.GetProperty("signatures").EnumerateArray());
        Assert.Contains(expected, selected.GetProperty("label").GetString());
        Assert.Contains(owner, selected.GetProperty("label").GetString());
    }
}
