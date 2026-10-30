using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class FormattedPrefixEditorTests
{
    [Fact]
    public void BorrowedFormatterConstructorKeepsItsImmutableInterfaceSignatureAndDefinition()
    {
        const string text = "using Standard; using Standard::Text;struct Prefix:IFormattable{public immutable void Format(ITextWriter* output){output.Write(\">\",1);}}void Probe(){char[32] data;BufferWriter output=new(data,32);Prefix prefix=new();LinePrefixWriter lines=new(&output,&prefix);lines.Write(\"a\",1);}";
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "StandardLibrary"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path))).ToList();
        var library = files.Single(file => Path.GetFileName(file.Path) == "LinePrefixWriter.gf");
        var source = new SourceFile(Path.GetFullPath("formatted-prefix-editor.gf"), text);
        files.Add(source);
        var snapshot = new AnalysisSnapshot(files, new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, error => error.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, files);
        int creation = text.IndexOf("new(&output", StringComparison.Ordinal);
        string hover = Json(model.Hover(source.Path, creation)).GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("IFormattable", hover);
        Assert.Contains("immutable", hover);
        Assert.DoesNotContain('$', hover);
        int declaration = library.Text.IndexOf("public LinePrefixWriter(ITextWriter* destination, immutable", StringComparison.Ordinal) + "public ".Length;
        var expected = model.Definition(library.Path, declaration);
        Assert.NotNull(expected);
        Assert.Equal(expected, model.Definition(source.Path, creation));
        var help = Json(model.SignatureHelp(source.Path, creation + "new(&output,".Length));
        Assert.Equal(1, help.GetProperty("activeParameter").GetInt32());
        var signature = help.GetProperty("signatures")[help.GetProperty("activeSignature").GetInt32()];
        Assert.Equal(2, signature.GetProperty("parameters").GetArrayLength());
        Assert.Contains("IFormattable", signature.GetProperty("label").GetString()!);
    }

    private static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value, Protocol.JsonOptions);
}
