using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class StringTextComparisonEditorTests
{
    [Fact]
    public void BorrowedTextComparisonSelectsEachActualImmutableOverload()
    {
        const string text = """
            using std;
            void Observe(immutable(String)* value, immutable(String)* other)
            {
                int owned = value.CompareTo(other);
                int counted = value.CompareTo("ab", 2);
                int terminated = value.CompareTo("ab");
            }
            """;
        string root = Path.Combine(AppContext.BaseDirectory, "StandardLibrary", "core");
        string[] names = ["IStringConvertable.gf", "IHashable.gf", "IEquatable.gf",
            "IComparable.gf", "Hashing/primitive_hashing.gf", "Text/ByteSearch.gf", "Text/String.gf"];
        var files = names.Select(name => Path.Combine(root, name))
            .Select(path => new SourceFile(path, File.ReadAllText(path))).ToList();
        var source = new SourceFile(Path.GetFullPath("string-text-comparison-editor.gf"), text);
        files.Add(source);
        SourceFile library = files.Single(file => Path.GetFileName(file.Path) == "String.gf");
        var snapshot = new AnalysisSnapshot(files);
        Assert.DoesNotContain(snapshot.Diagnostics, error => error.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, files);
        var rows = new[]
        {
            (Call: "CompareTo(other)", Declaration: "CompareTo(immutable(String)* other)", Arguments: 1, FirstType: "String"),
            (Call: "CompareTo(\"ab\", 2)", Declaration: "CompareTo(readonly(char)* text, nuint count)", Arguments: 2, FirstType: "char"),
            (Call: "CompareTo(\"ab\")", Declaration: "CompareTo(readonly(char)* text)", Arguments: 1, FirstType: "char")
        };
        var targets = new List<string>();
        foreach (var row in rows)
        {
            int use = text.IndexOf(row.Call, StringComparison.Ordinal);
            Assert.True(use >= 0, "Missing call " + row.Call);
            var completion = Json(model.Completion(source.Path, use)).GetProperty("items");
            Assert.Single(completion.EnumerateArray(), item => item.GetProperty("label").GetString() == "CompareTo");

            string hover = Json(model.Hover(source.Path, use)).GetProperty("contents").GetProperty("value").GetString()!;
            Assert.Contains("CompareTo", hover);
            Assert.Contains("int", hover);
            Assert.Contains("immutable", hover);
            Assert.Contains("nothrow", hover);
            Assert.DoesNotContain('$', hover);

            int declaration = library.Text.IndexOf(row.Declaration, StringComparison.Ordinal);
            Assert.True(declaration >= 0, "Missing declaration " + row.Declaration);
            object? expected = model.Definition(library.Path, declaration);
            Assert.NotNull(expected);
            Assert.Equal(expected, model.Definition(source.Path, use));
            targets.Add(JsonSerializer.Serialize(expected, Protocol.JsonOptions));

            int argument = use + row.Call.Length - 2;
            var help = Json(model.SignatureHelp(source.Path, argument));
            var signatures = help.GetProperty("signatures");
            Assert.Equal(3, signatures.GetArrayLength());
            Assert.Equal(row.Arguments - 1, help.GetProperty("activeParameter").GetInt32());
            var selected = signatures[help.GetProperty("activeSignature").GetInt32()];
            var parameters = selected.GetProperty("parameters");
            Assert.Equal(row.Arguments, parameters.GetArrayLength());
            Assert.Contains(row.FirstType, parameters[0].GetProperty("label").GetString()!);
            Assert.Contains("CompareTo", selected.GetProperty("label").GetString()!);
            Assert.DoesNotContain('$', selected.GetProperty("label").GetString()!);
        }
        Assert.Equal(3, targets.Distinct(StringComparer.Ordinal).Count());
    }

    private static JsonElement Json(object? value)
        => JsonSerializer.SerializeToElement(value, Protocol.JsonOptions);
}
