using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class VersionEditorTests
{
    [Fact]
    public void NumericVersionNavigationAndOptionalConstructionUseTheValueDeclaration()
    {
        const string text = "using std;void Probe(){Version original=Version::Parse(\"1.2\",3);Version extended=new(1,2,0);int major=original.Major;int order=original.CompareTo(&extended);}";
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "StandardLibrary"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path))).ToList();
        var library = files.Single(file => Path.GetFileName(file.Path) == "Version.gf");
        var source = new SourceFile(Path.GetFullPath("version-editor.gf"), text);
        files.Add(source);
        var snapshot = new AnalysisSnapshot(files, new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, error => error.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, files);
        foreach (string name in new[] { "Parse", "Major", "CompareTo" })
        {
            int use = text.IndexOf(name, StringComparison.Ordinal);
            string hover = Json(model.Hover(source.Path, use)).GetProperty("contents").GetProperty("value").GetString()!;
            Assert.Contains(name, hover);
            Assert.DoesNotContain('$', hover);
            if (name == "CompareTo") Assert.Contains("nothrow", hover);
            string marker = name == "Major" ? "int Major" : name == "Parse" ? "Version Parse(" : name + "(";
            int declaration = library.Text.IndexOf(marker, StringComparison.Ordinal);
            if (name == "Major") declaration += "int ".Length;
            if (name == "Parse") declaration += "Version ".Length;
            Assert.True(declaration >= 0);
            var expected = model.Definition(library.Path, declaration);
            Assert.NotNull(expected);
            Assert.Equal(expected, model.Definition(source.Path, use));
        }
        int argument = text.IndexOf("new(1,2,", StringComparison.Ordinal) + "new(1,2,".Length;
        var help = Json(model.SignatureHelp(source.Path, argument));
        Assert.Equal(2, help.GetProperty("activeParameter").GetInt32());
        var signature = help.GetProperty("signatures")[help.GetProperty("activeSignature").GetInt32()];
        Assert.Equal(4, signature.GetProperty("parameters").GetArrayLength());
        Assert.Contains("revision", signature.GetProperty("label").GetString()!);
    }

    private static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value, Protocol.JsonOptions);
}
