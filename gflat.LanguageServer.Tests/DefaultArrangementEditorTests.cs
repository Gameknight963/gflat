using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class DefaultArrangementEditorTests
{
    [Fact]
    public void DefaultAndExplicitCallsNavigateTheirOwnArityAndDefinitions()
    {
        const string text = """
            using Standard; using Standard::Memory; using Standard::Collections;
            int Compare(immutable(int)* first, immutable(int)* second) {
                if (*first < *second) return -1;
                if (*first > *second) return 1;
                return 0;
            }
            void Observe() {
                int[3] data = [3, 1, 2]; Span<int> values = new(data, 3);
                NthElement(values, 1);
                NthElement(values, 1, Compare);
                NextPermutation(values);
                NextPermutation(values, Compare);
                PreviousPermutation(values);
                PreviousPermutation(values, Compare);
            }
            """;
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "StandardLibrary"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path))).ToList();
        var source = new SourceFile(Path.GetFullPath("default-arrangement-editor.gf"), text);
        files.Add(source);
        var snapshot = new AnalysisSnapshot(files, new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, error => error.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, files);
        foreach (string name in new[] { "NthElement", "NextPermutation", "PreviousPermutation" })
        {
            bool selection = name == "NthElement";
            for (int explicitCall = 0; explicitCall < 2; explicitCall++)
            {
                string call = name + (selection ? "(values, 1" : "(values") + (explicitCall == 1 ? ", Compare)" : ")");
                int use = text.IndexOf(call, StringComparison.Ordinal);
                Assert.True(use >= 0);
                Assert.Single(Json(model.Completion(source.Path, use)).GetProperty("items").EnumerateArray(),
                    item => item.GetProperty("label").GetString() == name);
                string hover = Json(model.Hover(source.Path, use)).GetProperty("contents").GetProperty("value").GetString()!;
                Assert.Contains(name + "<int>", hover);
                Assert.Contains("Span<int>", hover);
                Assert.DoesNotContain('$', hover);
                string file = explicitCall == 0 ? "default_ordering.gf" : selection ? "nth_element.gf" : "span_permutations.gf";
                SourceFile library = files.Single(candidate => Path.GetFileName(candidate.Path) == file);
                string prefix = "public " + (selection ? "void " : "bool ");
                string anchor = prefix + name + (explicitCall == 0 ? "<T : IComparable<T>>" : "<T>") + "(";
                int declaration = library.Text.IndexOf(anchor, StringComparison.Ordinal);
                Assert.True(declaration >= 0);
                var expected = model.Definition(library.Path, declaration + prefix.Length);
                Assert.NotNull(expected);
                Assert.Equal(expected, model.Definition(source.Path, use));
                int argument = use + call.Length - 1;
                var help = Json(model.SignatureHelp(source.Path, argument));
                var signatures = help.GetProperty("signatures");
                Assert.Equal(2, signatures.GetArrayLength());
                var active = signatures[help.GetProperty("activeSignature").GetInt32()];
                int arity = (selection ? 2 : 1) + explicitCall;
                Assert.Equal(arity, active.GetProperty("parameters").GetArrayLength());
                Assert.Equal(arity - 1, help.GetProperty("activeParameter").GetInt32());
            }
        }
    }

    private static JsonElement Json(object? value)
        => JsonSerializer.SerializeToElement(value, Protocol.JsonOptions);
}
