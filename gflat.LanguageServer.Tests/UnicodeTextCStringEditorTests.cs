using System.Text.Json;

namespace gflat.LanguageServer.Tests;

public sealed class UnicodeTextCStringEditorTests
{
    [Fact]
    public void NativeStringConversionExposesItsOwningResultAndSourceDefinition()
    {
        const string text = """
            using Standard::Collections;
            using Standard::Text;

            void Use(readonly(char)* input, nuint bytes)
            {
                Vector<ushort> result = UnicodeText::ToUtf16CString(input, bytes);
            }
            """;
        var files = StandardTestSources.Text.Concat(StandardTestSources.Load("core/Binary/BinaryPrimitives.gf")).ToList();
        var library = files.Single(file => Path.GetFileName(file.Path) == "UnicodeText.gf");
        var source = new SourceFile(Path.GetFullPath("utf16-cstring-editor.gf"), text);
        files.Add(source);
        var snapshot = new AnalysisSnapshot(files, new() { NoStdLib = true });
        Assert.Empty(snapshot.Diagnostics);
        var model = new EditorModel(snapshot, files);
        int use = text.IndexOf("ToUtf16CString", StringComparison.Ordinal);
        var completion = JsonSerializer.SerializeToElement(model.Completion(source.Path, use), Protocol.JsonOptions);
        var items = completion.GetProperty("items").EnumerateArray().ToArray();
        Assert.Single(items, item => item.GetProperty("label").GetString() == "ToUtf16CString");
        Assert.DoesNotContain(items, item => item.GetProperty("label").GetString() == "ToUtf16Core");
        var signature = JsonSerializer.SerializeToElement(model.SignatureHelp(source.Path, use + "ToUtf16CString(".Length), Protocol.JsonOptions);
        Assert.Contains("Vector<ushort>", signature.GetProperty("signatures")[0].GetProperty("label").GetString());
        int declaration = library.Text.IndexOf("ToUtf16CString(", StringComparison.Ordinal);
        Assert.Equal(model.Definition(library.Path, declaration), model.Definition(source.Path, use));
        Assert.NotNull(model.Definition(source.Path, use));
    }
}
