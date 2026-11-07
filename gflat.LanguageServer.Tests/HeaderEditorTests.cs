using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class HeaderEditorTests
{
    [Theory]
    [InlineData(false, ".gfh", false)]
    [InlineData(true, ".gfh", false)]
    [InlineData(false, ".gf", false)]
    [InlineData(true, ".gf", false)]
    [InlineData(false, ".gfh", true)]
    [InlineData(false, ".gf", true)]
    public void SelectedHeaderContextChangesEnclosingMemberCompletion(bool alternatePathCasing, string extension, bool relativeSourcePath)
    {
        if (alternatePathCasing && !OperatingSystem.IsWindows()) return;
        string directory = relativeSourcePath ? "header-context" : Path.GetFullPath("header-context");
        var header = new SourceFile(Path.Combine(directory, "members" + extension), "public int Read() { return Value; }");
        var root = new SourceFile(Path.Combine(directory, "main.gf"), """
            struct First { public int Value; public int FirstOnly;
            #include "members.gfh"
            }
            struct Second { public int Value; public int SecondOnly;
            #include "members.gfh"
            }
            """.Replace("members.gfh", "members" + extension, StringComparison.Ordinal));
        var snapshot = new AnalysisSnapshot([root], new() { NoStdLib = true, IncludeLoader = path => Workspace.Paths.Equals(path, Path.GetFullPath(header.Path)) ? header : null });
        Assert.DoesNotContain(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, [root, header]);
        string queryPath = alternatePathCasing ? header.Path.ToUpperInvariant() : header.Path;
        var contexts = model.HeaderContexts(queryPath);
        Assert.Equal(2, contexts.Length);
        foreach (string type in new[] { "First", "Second" })
        {
            var selected = model.WithHeaderContext(queryPath, Assert.Single(contexts, c => c.Label.EndsWith(": " + type)).Id);
            var items = JsonSerializer.SerializeToElement(selected.Completion(queryPath, header.Text.IndexOf("Value", StringComparison.Ordinal)), Protocol.JsonOptions)
                .GetProperty("items").EnumerateArray().Select(i => i.GetProperty("label").GetString()).ToArray();
            Assert.Contains(type + "Only", items);
            Assert.DoesNotContain((type == "First" ? "Second" : "First") + "Only", items);
        }
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public void IncludeCompletionListsFoldersAndFilesAndReplacesOnlyTheFilename(string newline)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-include-completion-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Directory.CreateDirectory(Path.Combine(directory, "headers"));
            File.WriteAllText(Path.Combine(directory, "headers", "value.gfh"), "");
            var source = new SourceFile(Path.Combine(directory, "main.gf"), "// preceding line" + newline + "#include \"headers/va\"");
            var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
            var model = new EditorModel(snapshot, [source]);
            var completion = JsonSerializer.SerializeToElement(model.Completion(source.Path, source.Text.LastIndexOf('"')), Protocol.JsonOptions);
            var item = Assert.Single(completion.GetProperty("items").EnumerateArray());
            Assert.Equal("value.gfh", item.GetProperty("label").GetString());
            Assert.Equal("value.gfh", item.GetProperty("textEdit").GetProperty("newText").GetString());
        }
        finally { Directory.Delete(directory, true); }
    }
}
