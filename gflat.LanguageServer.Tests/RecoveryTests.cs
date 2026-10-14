using System.Text.Json;
using gflat.ast;
using gflat.LanguageServer;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public class RecoveryTests
{
    private static readonly string[] BrokenPrefixes = [
        "@\n", "int Missing();\n", "int Broken = ;\n", "readonly(char*) Bad() => \"unterminated\n",
        "int Broken() { int bad = ; return 0; }\n", "int Broken() { unknown(); return 0; }\n",
        "int Broken() { int bad = \"wrong\"; return 0; }\n", "}\n",
        "int Broken = 1__2;\n", "int Broken() { missing; missingAgain; }\n",
        "class Broken { int member = ; }\n", "int Broken() { return ; }\n"
    ];
    public static IEnumerable<object[]> Prefixes() => BrokenPrefixes.Select(p => new object[] { p });
    private const string Healthy = """
        namespace Demo {
            public struct Item { public int value; public int Read() => value; }
            public int Good(int argument) { int local = argument; return local; }
        }
        int Use() { Demo::Item item = new Demo::Item(); return Demo::Good(item.Read()); }
        """;

    private static Dictionary<int, string> Colors(EditorModel model, SourceFile source)
    {
        var data = JsonSerializer.SerializeToElement(model.SemanticTokens(source.Path)).GetProperty("data").EnumerateArray().Select(v => v.GetInt32()).ToArray();
        var starts = new[] { 0 }.Concat(source.Text.Select((c, i) => (c, i)).Where(x => x.c == '\n').Select(x => x.i + 1)).ToArray();
        var result = new Dictionary<int, string>();
        int line = 0, column = 0;
        for (int i = 0; i < data.Length; i += 5) {
            line += data[i]; column = data[i] == 0 ? column + data[i + 1] : data[i + 1];
            result[starts[line] + column] = EditorModel.TokenTypes[data[i + 3]];
        }
        return result;
    }

    [Theory]
    [MemberData(nameof(Prefixes))]
    public void ErrorsAboveDoNotEraseDeclarationsOrLocalServices(string prefix)
    {
        var source = new SourceFile(Path.GetFullPath("recover.gf"), prefix + Healthy);
        var snapshot = new AnalysisSnapshot([source]);
        Assert.Contains(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, [source]);
        var colors = Colors(model, source);
        Assert.Equal("namespace", colors[source.Text.IndexOf("Demo", StringComparison.Ordinal)]);
        Assert.Equal("struct", colors[source.Text.IndexOf("Item", StringComparison.Ordinal)]);
        int function = source.Text.IndexOf("Good", StringComparison.Ordinal);
        Assert.Equal("method", colors[function]);
        int local = source.Text.LastIndexOf("return local", StringComparison.Ordinal) + 7;
        Assert.Equal("variable", colors[local]);
        Assert.NotNull(model.Hover(source.Path, local));
        Assert.NotNull(model.Definition(source.Path, local));
        Assert.NotNull(model.Hover(source.Path, function));
        var completion = JsonSerializer.SerializeToElement(model.Completion(source.Path, local));
        Assert.Contains(completion.GetProperty("items").EnumerateArray(), i => i.GetProperty("label").GetString() == "local");
    }

    [Theory]
    [MemberData(nameof(Prefixes))]
    public void ErrorsInOneFileDoNotEraseOtherFiles(string prefix)
    {
        var bad = new SourceFile(Path.GetFullPath("a-broken.gf"), prefix);
        var healthy = new SourceFile(Path.GetFullPath("healthy.gf"), Healthy);
        var snapshot = new AnalysisSnapshot([bad, healthy]);
        Assert.Contains(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, [bad, healthy]);
        var colors = Colors(model, healthy);
        int use = healthy.Text.LastIndexOf("Good", StringComparison.Ordinal);
        Assert.Equal("method", colors[use]);
        Assert.NotNull(model.Hover(healthy.Path, use));
        Assert.NotNull(model.Definition(healthy.Path, use));
        var literal = snapshot.Nodes.Where(n => n.Span.SourceId == healthy.Id).Select(n => n.Node).OfType<VariableDeclaration>().Single(n => n.Name == "item");
        Assert.NotEqual("<error>", TypeChecker.TypeName(snapshot.Checker!.GetType(literal)));
    }

    [Fact]
    public void QualifiedExceptionTypesHaveNamespaceColors()
    {
        var source = new SourceFile(Path.GetFullPath("exceptions.gf"), """
            namespace std { public class IOException : Exception { } public class EndOfStreamException : Exception { } }
            void Flush() { throw new std::IOException(); }
            nuint ReadLine() { throw new std::EndOfStreamException(); }
            """);
        var snapshot = new AnalysisSnapshot([source]);
        var model = new EditorModel(snapshot, [source]);
        var colors = Colors(model, source);
        foreach (string name in new[] { "std::IOException", "std::EndOfStreamException" }) {
            int start = source.Text.LastIndexOf(name, StringComparison.Ordinal);
            Assert.Equal("namespace", colors[start]);
            Assert.Equal("class", colors[start + 5]);
            Assert.NotNull(model.Hover(source.Path, start));
            Assert.NotNull(model.Definition(source.Path, start + 5));
        }
    }

    [Fact]
    public void SemanticErrorDoesNotPreventLaterStatementsFromBeingChecked()
    {
        var source = new SourceFile("statements.gf", "int F() { unknown(); int answer = 42; return answer; }");
        var snapshot = new AnalysisSnapshot([source]);
        Assert.Contains(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var answer = snapshot.Nodes.Select(n => n.Node).OfType<VariableDeclaration>().Single(n => n.Name == "answer");
        Assert.Equal("int", TypeChecker.TypeName(snapshot.Checker!.GetType(answer)));
    }
}
