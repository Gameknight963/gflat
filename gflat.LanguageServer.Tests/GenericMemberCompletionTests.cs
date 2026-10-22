using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class GenericMemberCompletionTests
{
    [Fact]
    public void ConcreteReceiversAndAliasesHaveConsistentCompletionAndHoverSignatures()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "StandardLibrary");
        var sources = Directory.GetFiles(directory, "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path))).ToList();
        const string text = """
            using std;
            alias IntVector = std::Vector<int>;
            alias IntEquality = std::IEquatable<int>;
            class Box<T> { public T Echo(T value) => value; }
            void Append<T>(ref Vector<T> vector, T value) { vector.Add(value); }
            int main() {
                Vector<int> values = new(); values.Add(42);
                IntVector aliased = new(); aliased.Add(42);
                HashMap<int,long> map = new(); map.Add(42, 43l);
                Box<long> box = new(); box.Echo(42l);
                int other = 42; IntEquality* equality = &other; equality.Equals(&other);
                return 0;
            }
            """;
        var source = new SourceFile(Path.GetFullPath("std-editor/generic-member-completion.gf"), text);
        sources.Add(source);
        var snapshot = new AnalysisSnapshot(sources, new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, sources);
        foreach (var (marker, expected, owner) in new[] {
            ("values.Add", "int value", "Vector<int>"),
            ("aliased.Add", "int value", "Vector<int>"),
            ("map.Add", "int key, long value", "HashMap<int, long>"),
            ("box.Echo", "long value", "Box<long>"),
            ("equality.Equals", "readonly(int)* other", "IEquatable<int>"),
            ("vector.Add", "T value", "Vector")
        })
        {
            string method = marker[(marker.IndexOf('.') + 1)..];
            int offset = text.IndexOf(marker, StringComparison.Ordinal) + marker.IndexOf('.') + 1;
            var completion = JsonSerializer.SerializeToElement(model.Completion(source.Path, offset), Protocol.JsonOptions);
            var entry = Assert.Single(completion.GetProperty("items").EnumerateArray(), item => item.GetProperty("label").GetString() == method);
            string detail = entry.GetProperty("detail").GetString()!;
            Assert.Contains(expected, detail);
            Assert.Contains(owner, detail);
            Assert.DoesNotContain('$', detail);
            var hover = JsonSerializer.SerializeToElement(model.Hover(source.Path, offset), Protocol.JsonOptions);
            Assert.Contains(expected, hover.GetProperty("contents").GetProperty("value").GetString());
            Assert.NotNull(model.Definition(source.Path, offset));
        }
    }
}
