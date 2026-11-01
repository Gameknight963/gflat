using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class CallableFamilyQueryOrderTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (bool hoverFirst in new[] { false, true })
        {
            yield return [hoverFirst,
                "T Echo<T>(T value) => value; int Echo(int value, int extra) => value;",
                "void Probe() { int result = Echo<int>(42); }", "Echo<int>(", "Echo<int>(int value)"];
            yield return [hoverFirst,
                """
                struct Box<T> { public T Value; }
                namespace Extras {
                    extension<T> Box<T> { public T Pick(int amount) => (*this).Value; }
                    extension<T> Box<T> { public T Pick<U>(U other, int marker) => (*this).Value; }
                }
                """,
                """
                using Extras;

                int Probe() { Box<int> value = new(); return value.Pick(1); }
                """, "Pick(", "int amount"];
            yield return [hoverFirst,
                """
                namespace First { public int Read(int value) => value; }
                namespace Second { public int Read(bool value) { if (value) return 1; return 0; } }
                namespace Hidden { public int Read(long value) => 0; }
                """,
                """
                using First;
                using Second;

                int Probe() => Read(true);
                """, "Read(", "Read(bool value)"];
            yield return [hoverFirst,
                "class Box<T> { public Box(int n) {} public Box(long n) {} }",
                "void Probe() { Box<int> value = new Box<int>(42l); }", "new Box<int>(", "long n"];
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void HoverAndSignatureQueriesKeepSelectedBindingsAndHighlighting(
        bool hoverFirst, string libraryText, string text, string call, string selectedLabel)
    {
        var library = new SourceFile(Path.GetFullPath("family-order-library.gf"), libraryText);
        var source = new SourceFile(Path.GetFullPath("family-order-main.gf"), text);
        SourceFile[] sources = [library, source];
        var snapshot = new AnalysisSnapshot(sources, new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, sources);
        string highlighting = Json(model.SemanticTokens(source.Path)).GetRawText();
        int use = text.IndexOf(call, StringComparison.Ordinal);
        Assert.True(use >= 0);
        var definition = model.Definition(source.Path, use);
        string firstHover = "", firstHelp = "";
        if (hoverFirst) { CheckHover(); CheckHelp(); }
        else { CheckHelp(); CheckHover(); }
        CheckHover();
        CheckHelp();
        Assert.Equal(highlighting, Json(model.SemanticTokens(source.Path)).GetRawText());
        Assert.Equal(definition, model.Definition(source.Path, use));
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        void CheckHover()
        {
            string hover = Json(model.Hover(source.Path, use)).GetProperty("contents").GetProperty("value").GetString()!;
            Assert.Contains(selectedLabel, hover);
            Assert.Contains("+1 overload", hover);
            if (firstHover.Length > 0) Assert.Equal(firstHover, hover);
            firstHover = hover;
        }

        void CheckHelp()
        {
            var help = Json(model.SignatureHelp(source.Path, use + call.Length));
            var signatures = help.GetProperty("signatures");
            Assert.Equal(2, signatures.GetArrayLength());
            string selected = signatures[help.GetProperty("activeSignature").GetInt32()].GetProperty("label").GetString()!;
            Assert.Contains(selectedLabel, selected);
            if (firstHelp.Length > 0) Assert.Equal(firstHelp, help.GetRawText());
            firstHelp = help.GetRawText();
        }
    }

    private static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value, Protocol.JsonOptions);
}
