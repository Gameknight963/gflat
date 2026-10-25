using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests.Namespaces;

public sealed class ImportedOverloadEditorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImportedCallsAndFunctionValuesRetainTheSelectedDeclaration(bool reverse)
    {
        const string firstText = "namespace First { public int Read(int value) => value; }";
        const string secondText = "namespace Second { public int Read(bool value) { if (value) return 1; return 0; } }";
        string imports = reverse ? "using Second; using First;" : "using First; using Second;";
        string text = imports + """

            int Test() {
                int number = Read(42);
                int flag = Read(true);
                int(int)* callback = Read;
                return number + flag + callback(0);
            }
            """;
        var first = new SourceFile(Path.GetFullPath("imported-first.gf"), firstText);
        var second = new SourceFile(Path.GetFullPath("imported-second.gf"), secondText);
        var caller = new SourceFile(Path.GetFullPath("imported-editor-main.gf"), text);
        var unrelated = new SourceFile(Path.GetFullPath("unimported-editor.gf"), """
            namespace Unimported { public int Read(long value) => 3; }
            struct Other { public int Read(char value) => 4; }
            """);
        SourceFile[] sources = [reverse ? second : first, reverse ? first : second, unrelated, caller];
        var snapshot = new AnalysisSnapshot(sources, new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, sources);

        foreach (bool boolean in new[] { false, true })
        {
            string argument = boolean ? "true" : "42";
            string parameter = boolean ? "bool value" : "int value";
            int use = text.IndexOf("Read(" + argument + ")", StringComparison.Ordinal);
            string hover = Hover(model, caller, use);
            Assert.Contains("Read(" + parameter + ")", hover);
            Assert.DoesNotContain('$', hover);
            SourceFile target = boolean ? second : first;
            AssertDefinition(model, caller, use, target, target.Text.IndexOf("Read(", StringComparison.Ordinal));

            var help = Json(model.SignatureHelp(caller.Path, use + "Read(".Length));
            var signatures = help.GetProperty("signatures");
            Assert.Equal(2, signatures.GetArrayLength());
            Assert.Equal(0, help.GetProperty("activeParameter").GetInt32());
            Assert.Contains(signatures.EnumerateArray(), item => item.GetProperty("label").GetString()!.Contains("Read(int value)", StringComparison.Ordinal));
            Assert.Contains(signatures.EnumerateArray(), item => item.GetProperty("label").GetString()!.Contains("Read(bool value)", StringComparison.Ordinal));
            string active = signatures[help.GetProperty("activeSignature").GetInt32()].GetProperty("label").GetString()!;
            Assert.Contains("Read(" + parameter + ")", active);
        }

        int address = text.IndexOf("= Read;", StringComparison.Ordinal) + "= ".Length;
        Assert.Contains("Read(int value)", Hover(model, caller, address));
        AssertDefinition(model, caller, address, first, firstText.IndexOf("Read(", StringComparison.Ordinal));
    }

    [Fact]
    public void OpeningParenthesisOffersOnlyTheVisibleImportedFamilies()
    {
        var library = new SourceFile(Path.GetFullPath("incomplete-imported-library.gf"), """
            namespace First { public int Read(int value) => value; }
            namespace Second { public int Read(bool value) { if (value) return 1; return 0; } }
            namespace Unimported { public int Read(long value) => 3; }
            struct Other { public int Read(char value) => 4; }
            """);
        const string text = "using First; using Second; void Test() { Read(";
        var caller = new SourceFile(Path.GetFullPath("incomplete-imported-main.gf"), text);
        SourceFile[] sources = [library, caller];
        var snapshot = new AnalysisSnapshot(sources, new() { NoStdLib = true });
        Assert.Contains(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, sources);
        var help = Json(model.SignatureHelp(caller.Path, text.Length));
        var signatures = help.GetProperty("signatures");
        Assert.Equal(2, signatures.GetArrayLength());
        Assert.Equal(0, help.GetProperty("activeParameter").GetInt32());
        Assert.Contains(signatures.EnumerateArray(), item => item.GetProperty("label").GetString()!.Contains("Read(int value)", StringComparison.Ordinal));
        Assert.Contains(signatures.EnumerateArray(), item => item.GetProperty("label").GetString()!.Contains("Read(bool value)", StringComparison.Ordinal));
    }

    [Fact]
    public void QualifiedLexicalAndInstanceCallsKeepTheirOwnDeclarationFamilies()
    {
        const string library = """
            namespace First { public int Read(int value) => value; }
            namespace Second { public int Read(bool value) { if (value) return 1; return 0; } }
            struct Other { public int Read(bool value) { if (value) return 2; return 0; } }
            """;
        const string text = """
            using First; using Second;
            namespace Scope {
                int Read(char value) => 3;
                int Test() {
                    int qualified = First::Read(42);
                    int lexical = Read('x');
                    Other other = new();
                    int member = other.Read(true);
                    return qualified + lexical + member;
                }
            }
            """;
        var definitions = new SourceFile(Path.GetFullPath("scoped-imported-library.gf"), library);
        var caller = new SourceFile(Path.GetFullPath("scoped-imported-main.gf"), text);
        SourceFile[] sources = [definitions, caller];
        var snapshot = new AnalysisSnapshot(sources, new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, sources);
        foreach (var (expression, parameter, target, declaration) in new[] {
            ("Read(42)", "int value", definitions, library.IndexOf("Read(int", StringComparison.Ordinal)),
            ("Read('x')", "char value", caller, text.IndexOf("Read(char", StringComparison.Ordinal)),
            ("Read(true)", "bool value", definitions, library.LastIndexOf("Read(bool", StringComparison.Ordinal)) })
        {
            int use = text.IndexOf(expression, StringComparison.Ordinal);
            Assert.Contains("Read(" + parameter + ")", Hover(model, caller, use));
            AssertDefinition(model, caller, use, target, declaration);
            var help = Json(model.SignatureHelp(caller.Path, use + "Read(".Length));
            string label = Assert.Single(help.GetProperty("signatures").EnumerateArray()).GetProperty("label").GetString()!;
            Assert.Contains("Read(" + parameter + ")", label);
            Assert.Equal(0, help.GetProperty("activeSignature").GetInt32());
        }
    }

    private static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value, Protocol.JsonOptions);
    private static string Hover(EditorModel model, SourceFile source, int offset)
        => Json(model.Hover(source.Path, offset)).GetProperty("contents").GetProperty("value").GetString()!;
    private static void AssertDefinition(EditorModel model, SourceFile caller, int use, SourceFile target, int offset)
    {
        var definition = Json(model.Definition(caller.Path, use));
        Assert.Equal(new Uri(target.Path).AbsoluteUri, definition.GetProperty("uri").GetString());
        string preceding = target.Text[..offset];
        var start = definition.GetProperty("range").GetProperty("start");
        Assert.Equal(preceding.Count(character => character == '\n'), start.GetProperty("line").GetInt32());
        Assert.Equal(offset - (preceding.LastIndexOf('\n') + 1), start.GetProperty("character").GetInt32());
    }
}
