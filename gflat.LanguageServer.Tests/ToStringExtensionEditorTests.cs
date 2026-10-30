using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class ToStringExtensionEditorTests
{
    [Fact]
    public void PrimitiveBorrowedAndInstanceConversionsNavigateToTheirSelectedDeclarations()
    {
        const string text = "using Standard; using Standard::Text;struct Label:IFormattable{public immutable void Format(ITextWriter* output){output.Write(\"label\",5);}}String Borrow<T:IFormattable>(ref immutable(T) borrowed)=>borrowed.ToString();void Probe(){int number=42;String first=number.ToString();Label label=new();String second=label.ToString();String original=s\"x\";String third=original.ToString();String fourth=Borrow(ref original);}";
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "StandardLibrary"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path))).ToList();
        var source = new SourceFile(Path.GetFullPath("to-string-editor.gf"), text);
        files.Add(source);
        var snapshot = new AnalysisSnapshot(files, new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, error => error.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, files);
        foreach (var row in new[] {
            (Receiver: "number", File: "primitive_string.gf", Declaration: "public String ToString()"),
            (Receiver: "label", File: "formattable_string.gf", Declaration: "public immutable String ToString("),
            (Receiver: "borrowed", File: "formattable_string.gf", Declaration: "public immutable String ToString("),
            (Receiver: "original", File: "String.gf", Declaration: "public immutable String ToString()") })
        {
            int use = text.IndexOf(row.Receiver + ".ToString", StringComparison.Ordinal) + row.Receiver.Length + 1;
            var library = files.Single(file => Path.GetFileName(file.Path) == row.File);
            int start = row.Receiver == "number" ? library.Text.IndexOf("extension int", StringComparison.Ordinal) : 0;
            int declaration = library.Text.IndexOf(row.Declaration, start, StringComparison.Ordinal) + row.Declaration.IndexOf("ToString", StringComparison.Ordinal);
            var expected = model.Definition(library.Path, declaration);
            Assert.NotNull(expected);
            Assert.Equal(expected, model.Definition(source.Path, use));
            string hover = Json(model.Hover(source.Path, use)).GetProperty("contents").GetProperty("value").GetString()!;
            Assert.Contains("String", hover);
            Assert.Contains("ToString", hover);
            Assert.DoesNotContain('$', hover);
            var help = Json(model.SignatureHelp(source.Path, use + "ToString(".Length));
            var signature = help.GetProperty("signatures")[help.GetProperty("activeSignature").GetInt32()];
            Assert.Equal(0, signature.GetProperty("parameters").GetArrayLength());
        }
    }

    [Fact]
    public void SingleBorrowedExtensionUsesTheOpenParametersAdvertisedAliasContract()
    {
        const string text = "using Standard; using Standard::Text;namespace Views{alias Bound=IFormattable;String Describe<T:Bound>(ref immutable(T) value)=>value.ToString();}";
        string[] excluded = ["primitive_string.gf", "extra_long_string.gf", "floating_string.gf"];
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "StandardLibrary"), "*.gf", SearchOption.AllDirectories)
            .Where(path => !excluded.Contains(Path.GetFileName(path)))
            .Select(path => new SourceFile(path, File.ReadAllText(path))).ToList();
        var source = new SourceFile(Path.GetFullPath("open-single-conversion.gf"), text);
        files.Add(source);
        var snapshot = new AnalysisSnapshot(files, new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, error => error.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, files);
        int use = text.IndexOf("ToString", StringComparison.Ordinal);
        for (int query = 0; query < 2; query++)
        {
            var definition = Json(model.Definition(source.Path, use));
            Assert.EndsWith("formattable_string.gf", new Uri(definition.GetProperty("uri").GetString()!).LocalPath);
            string hover = Json(model.Hover(source.Path, use)).GetProperty("contents").GetProperty("value").GetString()!;
            Assert.Contains("ToString", hover);
            Assert.DoesNotContain('$', hover);
            var help = Json(model.SignatureHelp(source.Path, use + "ToString(".Length));
            Assert.Equal(0, Assert.Single(help.GetProperty("signatures").EnumerateArray()).GetProperty("parameters").GetArrayLength());
        }
    }

    private static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value, Protocol.JsonOptions);
}
