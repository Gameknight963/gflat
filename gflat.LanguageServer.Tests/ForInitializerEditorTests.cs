using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class ForInitializerEditorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExpressionInitializersKeepLocalAndLaterMethodBindings(bool unrelatedError)
    {
        const string text = """
            void Reset(ref int value) { value = 0; }
            int Run() {
                int index = 99;
                for (index = 0; index < 3; index++) {}
                for (Reset(ref index); index < 5; index++) {}
                return index;
            }
            int Later() => Run();
            """;
        var source = new SourceFile(Path.GetFullPath("for-initializers.gf"), text);
        var sources = new List<SourceFile> { source };
        if (unrelatedError) sources.Add(new SourceFile(Path.GetFullPath("unrelated.gf"), "void Broken() { absent(); }"));
        var snapshot = new AnalysisSnapshot(sources);
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Span.Source?.Path == source.Path);
        if (unrelatedError) Assert.Contains(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, sources);
        foreach (var token in Lexer.Tokenize(source).Where(token => token.Text is "index" or "Reset" or "Run"))
        {
            var hover = JsonSerializer.SerializeToElement(model.Hover(source.Path, token.Span.Start), Protocol.JsonOptions);
            Assert.Contains(token.Text, hover.GetProperty("contents").GetProperty("value").GetString());
            Assert.NotNull(model.Definition(source.Path, token.Span.Start));
        }
    }
}
