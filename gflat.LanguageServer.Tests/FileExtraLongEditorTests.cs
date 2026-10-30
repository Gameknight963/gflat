using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class FileExtraLongEditorTests
{
    [Fact]
    public void FourStreamOperationsKeepTheirConcreteSignaturesAndDefinitions()
    {
        const string text = """
            using Standard; using Standard::IO;
            void Observe(File* stream) {
                extralong first = FileBinary::ReadExtraLongLittleEndian(stream);
                extralong second = FileBinary::ReadExtraLongBigEndian(stream);
                FileBinary::WriteExtraLongLittleEndian(stream, first);
                FileBinary::WriteExtraLongBigEndian(stream, second);
            }
            """;
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "StandardLibrary"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path))).ToList();
        var source = new SourceFile(Path.GetFullPath("file-extra-long-editor.gf"), text);
        files.Add(source);
        SourceFile library = files.Single(file => Path.GetFileName(file.Path) == "FileBinary.gf");
        var snapshot = new AnalysisSnapshot(files, new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, error => error.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, files);
        foreach (string name in new[] { "ReadExtraLongLittleEndian", "ReadExtraLongBigEndian", "WriteExtraLongLittleEndian", "WriteExtraLongBigEndian" })
        {
            bool writing = name.StartsWith("Write", StringComparison.Ordinal);
            int use = text.IndexOf(name + "(", StringComparison.Ordinal);
            Assert.True(use >= 0);
            Assert.Single(Json(model.Completion(source.Path, use)).GetProperty("items").EnumerateArray(),
                item => item.GetProperty("label").GetString() == name);
            string hover = Json(model.Hover(source.Path, use)).GetProperty("contents").GetProperty("value").GetString()!;
            Assert.Contains(name, hover);
            Assert.Contains(writing ? "void" : "extralong", hover);
            Assert.Contains(writing ? "File* output" : "File* input", hover);
            Assert.DoesNotContain("nothrow", hover);
            string prefix = "public static " + (writing ? "void " : "extralong ");
            int declaration = library.Text.IndexOf(prefix + name + "(", StringComparison.Ordinal);
            Assert.True(declaration >= 0);
            var expected = model.Definition(library.Path, declaration + prefix.Length);
            Assert.NotNull(expected);
            Assert.Equal(expected, model.Definition(source.Path, use));
            int signaturePosition = use + name.Length + 1;
            if (writing) signaturePosition = text.IndexOf(',', signaturePosition) + 1;
            var help = Json(model.SignatureHelp(source.Path, signaturePosition));
            Assert.Equal(writing ? 1 : 0, help.GetProperty("activeParameter").GetInt32());
            var signatures = help.GetProperty("signatures");
            Assert.Equal(1, signatures.GetArrayLength());
            Assert.Equal(writing ? 2 : 1, signatures[0].GetProperty("parameters").GetArrayLength());
            string label = signatures[0].GetProperty("label").GetString()!;
            Assert.Contains(writing ? "extralong value" : "extralong", label);
        }
    }

    private static JsonElement Json(object? value)
        => JsonSerializer.SerializeToElement(value, Protocol.JsonOptions);
}
