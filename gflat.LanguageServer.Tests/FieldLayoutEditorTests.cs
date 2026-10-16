using System.Text.Json;
using gflat.ast;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class FieldLayoutEditorTests
{
    [Fact]
    public void FieldLayoutBindingPreservesAliasesAndEditorNavigation()
    {
        const string library = """
            namespace Library {
                public alias Word = long;
                public struct Packet { public char prefix; public Word value; }
                public class Base { public Word value; }
            }
            namespace Other {
                public alias Word = byte;
                public class Derived : Library::Base { public Word tail; }
            }
            """;
        const string program = """
            int main() {
                alias Word = byte;
                Library::Packet packet = new Library::Packet();
                Other::Derived derived = new Other::Derived();
                packet.value = 42;
                derived.value = 42;
                return (int)sizeof(Library::Packet) + (int)alignof(Other::Derived);
            }
            """;
        var source = new SourceFile(Path.GetFullPath("layout-alias-library.gf"), library);
        var caller = new SourceFile(Path.GetFullPath("layout-alias-main.gf"), program);
        var snapshot = new AnalysisSnapshot([source, caller]);
        Assert.DoesNotContain(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, [source, caller]);

        var expectedAlias = model.Definition(source.Path, library.IndexOf("Word = long", StringComparison.Ordinal));
        Assert.NotNull(expectedAlias);
        foreach (int use in new[] {
            library.IndexOf("Word value", StringComparison.Ordinal),
            library.LastIndexOf("Word value", StringComparison.Ordinal)
        })
            Assert.Equal(expectedAlias, model.Definition(source.Path, use));
        Assert.NotEqual(expectedAlias, model.Definition(caller.Path, program.IndexOf("Word = byte", StringComparison.Ordinal)));

        foreach (string expression in new[] { "packet.value", "derived.value" })
        {
            int offset = program.IndexOf(expression, StringComparison.Ordinal) + expression.IndexOf('.') + 1;
            var payload = model.Hover(caller.Path, offset);
            Assert.True(payload != null, expression);
            var hover = JsonSerializer.SerializeToElement(payload, Protocol.JsonOptions);
            string signature = hover.GetProperty("contents").GetProperty("value").GetString()!;
            Assert.Contains("Word", signature);
            Assert.DoesNotContain("byte", signature);
            int fieldDeclaration = (expression.StartsWith("packet", StringComparison.Ordinal)
                ? library.IndexOf("Word value", StringComparison.Ordinal)
                : library.LastIndexOf("Word value", StringComparison.Ordinal)) + "Word ".Length;
            Assert.Equal(model.Definition(source.Path, fieldDeclaration), model.Definition(caller.Path, offset));

            var rich = JsonSerializer.SerializeToElement(model.Hover(caller.Path, offset,
                new VisualStudioHover(JsonSerializer.SerializeToElement(new { }))), Protocol.JsonOptions);
            var runs = rich.GetProperty("_vs_rawContent").GetProperty("Elements")[0].GetProperty("Elements")
                .EnumerateArray().Single(element => element.GetProperty("_vs_type").GetString() == "ClassifiedTextElement")
                .GetProperty("Runs").EnumerateArray();
            var aliasRun = runs.Single(run => run.GetProperty("Text").GetString() == "Word");
            Assert.Equal(JsonSerializer.SerializeToElement(expectedAlias, Protocol.JsonOptions).ToString(),
                aliasRun.GetProperty("_gflat_target").ToString());
        }

        var checker = snapshot.Checker!;
        foreach (var field in snapshot.Nodes.Select(n => n.Node).OfType<MemberAccessExpression>().Where(n => n.Member == "value"))
            Assert.Equal("long", TypeChecker.TypeName(checker.ResolveAlias(checker.GetType(field))));
        Assert.Equal(16, checker.GetStructSize(checker.GetStruct("Library::Packet")!));
        Assert.Equal(24, checker.GetClassSize(checker.GetClass("Other::Derived")!));
    }
}
