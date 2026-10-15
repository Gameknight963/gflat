using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class ManagedCollectorEditorTests
{
    private const string Declarations = """
        namespace Runtime {
            public static class Collector : std::IGarbageCollector {
                public static void* Allocate(nuint bytes, nuint alignment) => (void*)1;
                public static void ReferenceAdded(void* slot) nothrow {}
                public static void ReferenceRemoved(void* slot) nothrow {}
                public static nuint Collect() => 0;
            }
        }
        alias GC = Runtime::Collector;
        struct Item { public int Value; }
        """;

    private static (EditorModel Model, SourceFile Source) Analyze(string text)
    {
        var source = new SourceFile(Path.GetFullPath("managed-collector-editor.gf"), Declarations + "\n" + text);
        var snapshot = new AnalysisSnapshot([source]);
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        return (new EditorModel(snapshot, [source]), source);
    }

    private static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value, Protocol.JsonOptions);
    private static string Hover(EditorModel model, SourceFile source, string word)
        => Json(model.Hover(source.Path, source.Text.LastIndexOf(word, StringComparison.Ordinal))).GetProperty("contents").GetProperty("value").GetString()!;

    private static Dictionary<int, string> Classifications(EditorModel model, SourceFile source)
    {
        var data = Json(model.SemanticTokens(source.Path)).GetProperty("data").EnumerateArray().Select(item => item.GetInt32()).ToArray();
        var result = new Dictionary<int, string>();
        var lineStarts = new[] { 0 }.Concat(source.Text.Select((character, index) => (character, index))
            .Where(item => item.character == '\n').Select(item => item.index + 1)).ToArray();
        int line = 0, column = 0;
        for (int i = 0; i < data.Length; i += 5)
        {
            line += data[i];
            column = data[i] == 0 ? column + data[i + 1] : data[i + 1];
            result[lineStarts[line] + column] = EditorModel.TokenTypes[data[i + 3]];
        }
        return result;
    }

    [Fact]
    public void ExplicitAndDefaultReferenceSpellingsRemainVisibleInHovers()
    {
        var (model, source) = Analyze("""
            managed<Item, Runtime::Collector> Explicit(managed<Item, Runtime::Collector> argument) => argument;
            Item^ Default(Item^ argument) => argument;
            managed<Item, GC> Aliased(managed<Item, GC> argument) => argument;
            void Test() {
                managed<Item, Runtime::Collector> explicitValue = new^ Item() with Runtime::Collector;
                Item^ defaultValue = new^ Item();
                managed<Item, GC> aliasValue = new^ Item();
            }
            """);
        Assert.Contains("managed<Item, Runtime::Collector>", Hover(model, source, "explicitValue"));
        Assert.Contains("Item^", Hover(model, source, "defaultValue"));
        Assert.Contains("managed<Item, GC>", Hover(model, source, "aliasValue"));
        Assert.Contains("managed<Item, GC>", Hover(model, source, "Aliased"));
        var classifications = Classifications(model, source);
        int collector = source.Text.LastIndexOf("Runtime::Collector", StringComparison.Ordinal) + "Runtime::".Length;
        Assert.Equal("class", classifications[collector]);
        Assert.NotNull(model.Definition(source.Path, collector));
        int with = source.Text.LastIndexOf("with Runtime", StringComparison.Ordinal);
        Assert.Equal("modifier", classifications[with]);
        Assert.Equal("modifier", classifications[source.Text.LastIndexOf("managed<", StringComparison.Ordinal)]);
    }

    [Fact]
    public void OpenCollectorParametersRetainGenericIdentity()
    {
        var (model, source) = Analyze("""
            static class Factory<C : std::IGarbageCollector> {
                public static managed<int, C> Create() => new^ int(42) with C;
                public static managed<T, C> Identity<T>(managed<T, C> argument) => argument;
            }
            """);
        var classifications = Classifications(model, source);
        var collectorTokens = Lexer.Tokenize(source, new DiagnosticBag()).Where(token => token.Text == "C").ToArray();
        Assert.NotEmpty(collectorTokens);
        var definition = model.Definition(source.Path, collectorTokens[0].Start);
        Assert.NotNull(definition);
        foreach (var token in collectorTokens)
        {
            Assert.Equal("typeParameter", classifications[token.Start]);
            Assert.Equal(definition, model.Definition(source.Path, token.Start));
        }
        Assert.Contains("managed<T, C>", Hover(model, source, "argument"));
        Assert.DoesNotContain("Runtime::Collector", Hover(model, source, "argument"));
        var rich = Json(model.Hover(source.Path, source.Text.LastIndexOf("argument", StringComparison.Ordinal),
            new VisualStudioHover(JsonSerializer.SerializeToElement(new { }))));
        var runs = rich.GetProperty("_vs_rawContent").GetProperty("Elements")[0].GetProperty("Elements")
            .EnumerateArray().Single(element => element.GetProperty("_vs_type").GetString() == "ClassifiedTextElement")
            .GetProperty("Runs").EnumerateArray().ToArray();
        Assert.Contains(runs, run => run.GetProperty("Text").GetString() == "managed" && run.GetProperty("ClassificationTypeName").GetString() == "keyword");
        Assert.Contains(runs, run => run.GetProperty("Text").GetString() == "C" && run.GetProperty("ClassificationTypeName").GetString() == "type parameter name" && run.GetProperty("_gflat_target").ValueKind == JsonValueKind.Object);
    }

    [Fact]
    public void CollectorSelectionCompletionOffersTypesAndQualifiedMembers()
    {
        var (model, source) = Analyze("void Test() { managed<Item, Runtime::Collector> value = new^ Item() with Runtime::Collector; }");
        int collector = source.Text.LastIndexOf("Runtime::Collector", StringComparison.Ordinal) + "Runtime::".Length;
        var qualified = Json(model.Completion(source.Path, collector)).GetProperty("items").EnumerateArray().ToArray();
        Assert.Contains(qualified, item => item.GetProperty("label").GetString() == "Collector");
        int selection = source.Text.LastIndexOf("with Runtime", StringComparison.Ordinal) + "with ".Length;
        var names = Json(model.Completion(source.Path, selection)).GetProperty("items").EnumerateArray().Select(item => item.GetProperty("label").GetString()).ToArray();
        Assert.Contains("Runtime", names);
        Assert.Contains("GC", names);
        Assert.DoesNotContain("value", names);
        Assert.DoesNotContain("return", names);
    }
}
