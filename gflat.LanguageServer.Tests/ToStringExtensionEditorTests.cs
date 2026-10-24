using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class ToStringExtensionEditorTests
{
    [Fact]
    public void PrimitiveBorrowedAndInstanceConversionsNavigateToTheirSelectedDeclarations()
    {
        const string text = "using std;struct Label:IFormattable{public immutable void Format(ITextWriter* output){output.Write(\"label\",5);}}void Probe(){int number=42;String first=number.ToString();Label label=new();String second=label.ToString();String original=s\"x\";String third=original.ToString();}";
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "StandardLibrary"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path))).ToList();
        var source = new SourceFile(Path.GetFullPath("to-string-editor.gf"), text);
        files.Add(source);
        var snapshot = new AnalysisSnapshot(files, new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, error => error.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, files);
        foreach (var row in new[] {
            (Receiver: "number", File: "PrimitiveString.gf", Declaration: "public String ToString(this int value)"),
            (Receiver: "label", File: "FormattableString.gf", Declaration: "public String ToString<T"),
            (Receiver: "original", File: "String.gf", Declaration: "public immutable String ToString()") })
        {
            int use = text.IndexOf(row.Receiver + ".ToString", StringComparison.Ordinal) + row.Receiver.Length + 1;
            var library = files.Single(file => Path.GetFileName(file.Path) == row.File);
            int declaration = library.Text.IndexOf(row.Declaration, StringComparison.Ordinal) + row.Declaration.IndexOf("ToString", StringComparison.Ordinal);
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

    private static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value, Protocol.JsonOptions);
}
