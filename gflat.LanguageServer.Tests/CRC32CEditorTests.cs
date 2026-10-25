using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class CRC32CEditorTests
{
    [Fact]
    public void StreamingValueMethodsNavigateActualNoThrowDeclarations()
    {
        const string text = """
            using std; using std::Hashing;
            uint Observe(readonly(char)* data, nuint count) nothrow {
                CRC32C crc = new();
                crc.Append(data, count);
                uint value = crc.GetValue();
                crc.Reset();
                return value;
            }
            """;
        string path = Path.Combine(AppContext.BaseDirectory, "StandardLibrary/core/Hashing/CRC32C.gf");
        var library = new SourceFile(path, File.ReadAllText(path));
        var source = new SourceFile(Path.GetFullPath("crc32c-editor.gf"), text);
        SourceFile[] files = [library, source];
        var snapshot = new AnalysisSnapshot(files, new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, error => error.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, files);
        foreach (var method in new[] {
            (Name: "Append", Prefix: "public void ", Return: "void", Arity: 2),
            (Name: "GetValue", Prefix: "public immutable uint ", Return: "uint", Arity: 0),
            (Name: "Reset", Prefix: "public void ", Return: "void", Arity: 0) })
        {
            int use = text.IndexOf(method.Name + "(", StringComparison.Ordinal);
            Assert.True(use >= 0);
            Assert.Single(Json(model.Completion(source.Path, use)).GetProperty("items").EnumerateArray(),
                item => item.GetProperty("label").GetString() == method.Name);
            string hover = Json(model.Hover(source.Path, use)).GetProperty("contents").GetProperty("value").GetString()!;
            Assert.Contains(method.Name, hover);
            Assert.Contains(method.Return, hover);
            Assert.Contains("nothrow", hover);
            int declaration = library.Text.IndexOf(method.Prefix + method.Name + "(", StringComparison.Ordinal);
            Assert.True(declaration >= 0);
            var expected = model.Definition(library.Path, declaration + method.Prefix.Length);
            Assert.NotNull(expected);
            Assert.Equal(expected, model.Definition(source.Path, use));
            int argument = use + method.Name.Length + 1;
            if (method.Arity == 2) argument = text.IndexOf(',', argument) + 1;
            var help = Json(model.SignatureHelp(source.Path, argument));
            var signature = Assert.Single(help.GetProperty("signatures").EnumerateArray());
            Assert.Equal(method.Arity, signature.GetProperty("parameters").GetArrayLength());
            if (method.Arity == 2) Assert.Equal(1, help.GetProperty("activeParameter").GetInt32());
        }
    }

    private static JsonElement Json(object? value)
        => JsonSerializer.SerializeToElement(value, Protocol.JsonOptions);
}
