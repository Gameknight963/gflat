using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class EditorPathCasingTests
{
    [Theory]
    [InlineData("hover")]
    [InlineData("completion")]
    [InlineData("definition")]
    [InlineData("tokens")]
    [InlineData("signature")]
    [InlineData("actions")]
    [InlineData("outline")]
    [InlineData("references")]
    [InlineData("prepare-rename")]
    [InlineData("rename")]
    [InlineData("hierarchy")]
    [InlineData("implementations")]
    public void WindowsQueriesUseTheSameSourceIdentityRegardlessOfPathCasing(string query)
    {
        if (!OperatingSystem.IsWindows()) return;
        const string text = "struct Box { private int field; public int Read(int number)=>number; } int F(int parameter){int local=1; Box box=new();return local+box.Read(parameter);}";
        var source = new SourceFile(Path.GetFullPath("editor-path-case.gf"), text);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        object? Run(EditorModel model, string path) => query switch {
            "hover" => model.Hover(path, text.IndexOf("local+", StringComparison.Ordinal)),
            "completion" => model.Completion(path, text.IndexOf("local+", StringComparison.Ordinal)),
            "definition" => model.Definition(path, text.IndexOf("local+", StringComparison.Ordinal)),
            "tokens" => model.SemanticTokens(path),
            "signature" => model.SignatureHelp(path, text.IndexOf("Read(parameter", StringComparison.Ordinal) + 5),
            "actions" => model.CodeActions(path, Workspace.ToRange(source.Span(text.IndexOf("Box", StringComparison.Ordinal)))),
            "outline" => model.DocumentSymbols(path),
            "references" => model.References(path, text.IndexOf("local+", StringComparison.Ordinal), true),
            "prepare-rename" => model.PrepareRename(path, text.IndexOf("local+", StringComparison.Ordinal)),
            "rename" => model.Rename(path, text.IndexOf("local+", StringComparison.Ordinal), "renamed"),
            "hierarchy" => model.PrepareTypeHierarchy(path, text.IndexOf("box.Read", StringComparison.Ordinal)),
            "implementations" => model.Implementations(path, text.IndexOf("Box", StringComparison.Ordinal)),
            _ => throw new ArgumentException(query)
        };
        var expected = Run(new(snapshot, [source]), source.Path);
        Assert.NotNull(expected);
        var actual = Run(new(snapshot, [source]), source.Path.ToUpperInvariant());
        Assert.Equal(JsonSerializer.Serialize(expected, Protocol.JsonOptions), JsonSerializer.Serialize(actual, Protocol.JsonOptions));
    }
}
