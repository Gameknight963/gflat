using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class ImportedNamespaceEditorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VisibleStaticTypesAndAliasesTakePrecedenceOverImportedNamespaces(bool alias)
    {
        var library = new SourceFile(Path.GetFullPath("namespace-shadow-library.gf"),
            "namespace Library::Tools { int NamespaceOnly() => 1; }");
        string declaration = alias
            ? "static class ActualType { public static int Actual() => 42; } alias Tools = ActualType;"
            : "static class Tools { public static int Actual() => 42; }";
        var source = new SourceFile(Path.GetFullPath("namespace-shadow-main.gf"), $$"""
            using Library;
            namespace App { {{declaration}} int Run() => Tools::Actual(); }
            int main() => App::Run();
            """);
        var sources = new[] { library, source };
        var snapshot = new AnalysisSnapshot(sources, new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, sources);
        int offset = source.Text.IndexOf("Tools::Actual", StringComparison.Ordinal) + "Tools::".Length;
        var completion = JsonSerializer.SerializeToElement(model.Completion(source.Path, offset), Protocol.JsonOptions);
        var items = completion.GetProperty("items").EnumerateArray().ToArray();
        Assert.Contains(items, item => item.GetProperty("label").GetString() == "Actual");
        Assert.DoesNotContain(items, item => item.GetProperty("label").GetString() == "NamespaceOnly");
        Assert.Contains("namespace-shadow-main.gf", JsonSerializer.Serialize(model.Definition(source.Path, offset), Protocol.JsonOptions));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NamespaceCompletionUsesLexicalPrecedenceAndDoesNotGuessAmbiguousImports(bool localNamespace)
    {
        var library = new SourceFile(Path.GetFullPath("namespace-choices.gf"), """
            namespace One::Tools { int Read() => 1; }
            namespace Two::Tools { int Read() => 2; }
            """);
        var source = new SourceFile(Path.GetFullPath("namespace-choice-main.gf"), """
            using One;
            using Two;
            int main() { return Tools::Read(); }
            """);
        string localPath = Path.GetFullPath("namespace-choice-local.gf");
        var sources = new List<SourceFile> { library, source };
        if (localNamespace) sources.Add(new(localPath, "namespace Tools { int Read() => 3; }"));
        var snapshot = new AnalysisSnapshot(sources, new() { NoStdLib = true });
        var errors = snapshot.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        if (localNamespace) Assert.Empty(errors);
        else { Assert.NotEmpty(errors); Assert.All(errors, d => Assert.Equal(source.Path, d.FilePath)); }
        var model = new EditorModel(snapshot, sources);
        int offset = source.Text.IndexOf("Tools::Read", StringComparison.Ordinal) + "Tools::".Length;
        var completion = JsonSerializer.SerializeToElement(model.Completion(source.Path, offset), Protocol.JsonOptions);
        var items = completion.GetProperty("items").EnumerateArray().ToArray();
        if (localNamespace) {
            Assert.Contains(items, item => item.GetProperty("label").GetString() == "Read");
            Assert.Contains("namespace-choice-local.gf", JsonSerializer.Serialize(model.Definition(source.Path, offset), Protocol.JsonOptions));
        }
        else Assert.DoesNotContain(items, item => item.GetProperty("label").GetString() == "Read");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImportedNamespaceCallsTypesAndExtensionsRemainUsableWithUnrelatedErrors(bool brokenFile)
    {
        var library = new SourceFile(Path.GetFullPath("imported-library.gf"), """
            namespace Library::Tools {
                struct Box<T> { public T Value; }
                int Read() => 7;
                int Add(int left, int right) => left + right;
                namespace More { int Read() => 3; }
            }
            """);
        var extensions = new SourceFile(Path.GetFullPath("imported-extensions.gf"), """
            using Library;
            namespace Extras { extension<T> Tools::Box<T>{public int Measure()=> sizeof(T);} }
            """);
        var source = new SourceFile(Path.GetFullPath("imported-main.gf"), """
            using Library;
            using Extras;
            int main() {
                Tools::Box<int> box = new();
                int value = Tools::Add(Tools::Read(), Tools::More::Read());
                return value + box.Measure();
            }
            """);
        var sources = new List<SourceFile> { library, extensions, source };
        string brokenPath = Path.GetFullPath("imported-broken.gf");
        if (brokenFile) sources.Add(new(brokenPath, """
            using Left;
            using Right;
            namespace Left::Shared { struct Box<T> { public T Value; } }
            namespace Right::Shared { struct Box<T> { public T Value; } }
            namespace Extras { extension<T> Shared::Box<T>{public int Broken()=> 0;} }
            void Problem() { missing(); }
            """));
        var snapshot = new AnalysisSnapshot(sources, new() { NoStdLib = true });
        var errors = snapshot.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        if (brokenFile) { Assert.NotEmpty(errors); Assert.All(errors, d => Assert.Equal(brokenPath, d.FilePath)); }
        else Assert.Empty(errors);
        var model = new EditorModel(snapshot, sources);
        AssertCompletion("Tools::", source.Text.IndexOf("Tools::Add", StringComparison.Ordinal), "Add");
        AssertCompletion("Tools::More::", source.Text.IndexOf("Tools::More::Read", StringComparison.Ordinal), "Read");
        int member = source.Text.IndexOf("box.Measure", StringComparison.Ordinal) + 4;
        var completion = JsonSerializer.SerializeToElement(model.Completion(source.Path, member), Protocol.JsonOptions);
        Assert.Contains(completion.GetProperty("items").EnumerateArray(), item => item.GetProperty("label").GetString() == "Measure");
        Assert.DoesNotContain(completion.GetProperty("items").EnumerateArray(), item => item.GetProperty("label").GetString() == "Broken");
        foreach (string name in new[] { "Measure", "Add", "Read" }) {
            int offset = source.Text.IndexOf(name + "(", StringComparison.Ordinal);
            var hover = JsonSerializer.SerializeToElement(model.Hover(source.Path, offset), Protocol.JsonOptions);
            Assert.Contains(name, hover.GetProperty("contents").GetProperty("value").GetString());
            Assert.Contains(name == "Measure" ? "imported-extensions.gf" : "imported-library.gf",
                JsonSerializer.Serialize(model.Definition(source.Path, offset), Protocol.JsonOptions));
        }

        void AssertCompletion(string prefix, int start, string name) {
            var result = JsonSerializer.SerializeToElement(model.Completion(source.Path, start + prefix.Length), Protocol.JsonOptions);
            Assert.Contains(result.GetProperty("items").EnumerateArray(), item => item.GetProperty("label").GetString() == name);
        }
    }
}
