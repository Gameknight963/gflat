using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class ReflectionTemplateEditorTests
{
    private const string Body = """
        public immutable bool Equals([:typeof(*this):] other)
        {
            static foreach (reflection field in typeof(*this).Fields)
            {
                if ((*this).[:field:] != other.[:field:]) return false;
            }
            return true;
        }
        public void Clear()
        {
            static foreach (reflection field in typeof(*this).Fields)
            {
                (*this).[:field:] = default([:field.Type:]);
            }
        }
        """;

    private static (EditorModel Model, SourceFile Header) Model(bool generic, bool standalone = false)
    {
        var header = new SourceFile(Path.GetFullPath("reflection-template/members.gfh"), Body);
        var source = new SourceFile(Path.GetFullPath("reflection-template/main.gf"),
            "struct Point" + (generic ? "<T>" : "") + " { public int X;\n#include \"members.gfh\"\n}");
        var snapshot = new AnalysisSnapshot([standalone ? header : source], new() { NoStdLib = true, IncludeLoader = _ => header });
        if (!standalone) Assert.DoesNotContain(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        return (new(snapshot, [header, source]), header);
    }

    private static JsonElement Hover(EditorModel model, SourceFile header, int offset)
        => JsonSerializer.SerializeToElement(model.Hover(header.Path, offset,
            new VisualStudioHover(JsonSerializer.SerializeToElement(new { }))), Protocol.JsonOptions);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TemplateBodyHasReflectionLocalsMembersAndBooleanHovers(bool generic)
    {
        var (model, header) = Model(generic);
        foreach (string needle in new[] { "[:field:]", "field.Type", "true", "false" })
        {
            int offset = Body.LastIndexOf(needle, StringComparison.Ordinal) + (needle.StartsWith("[:") ? 2 : 0);
            var hover = Hover(model, header, offset);
            Assert.NotEqual(JsonValueKind.Null, hover.ValueKind);
            Assert.Contains(needle is "true" or "false" ? "bool" : "reflection", hover.GetProperty("contents").GetProperty("value").GetString());
        }
        var property = Hover(model, header, Body.LastIndexOf("field.Type", StringComparison.Ordinal) + 6);
        Assert.Contains("reflection", property.GetProperty("contents").GetProperty("value").GetString());
        Assert.Contains("property name", property.GetProperty("_vs_rawContent").ToString());
        int[] data = JsonSerializer.SerializeToElement(model.SemanticTokens(header.Path), Protocol.JsonOptions)
            .GetProperty("data").EnumerateArray().Select(value => value.GetInt32()).ToArray();
        Assert.Contains(Array.IndexOf(EditorModel.TokenTypes, "property"), data.Where((_, index) => index % 5 == 3));
    }

    [Fact]
    public void UnresolvedParameterShowsSpliceSyntaxWithoutInventingATypeName()
    {
        var (model, header) = Model(true, standalone: true);
        var hover = Hover(model, header, Body.IndexOf("other", StringComparison.Ordinal));
        Assert.Contains("[:typeof(*this):] other", hover.GetProperty("contents").GetProperty("value").GetString());
        Assert.DoesNotContain("unknown", hover.ToString());
        var runs = hover.GetProperty("_vs_rawContent").GetProperty("Elements")[0].GetProperty("Elements")[0].GetProperty("Runs").EnumerateArray().ToArray();
        Assert.DoesNotContain(runs, run => run.GetProperty("ClassificationTypeName").GetString() is "type" or "class name" or "struct name");
        Assert.Contains(runs, run => run.GetProperty("Text").GetString() == "this" && run.GetProperty("ClassificationTypeName").GetString() == "keyword");
    }

    [Fact]
    public void QueriesReportTypeInformationWithoutIncludeSpecificExplanations()
    {
        var (model, header) = Model(false);
        int offset = Body.LastIndexOf("typeof", StringComparison.Ordinal);
        string text = Hover(model, header, offset).GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("Point", text);
        Assert.DoesNotContain("generic", text);
        var standalone = Model(false, standalone: true);
        string missing = Hover(standalone.Model, standalone.Header, offset).GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("(reflection)typeof(*this)", missing);
        Assert.DoesNotContain("Include this file", missing);
        Assert.DoesNotContain("Evaluated for each generic specialization", missing);
    }

    [Fact]
    public void SharedHeaderDoesNotPickOneOfItsEnclosingTypes()
    {
        var header = new SourceFile(Path.GetFullPath("shared-reflection/members.gfh"), Body);
        var source = new SourceFile(Path.GetFullPath("shared-reflection/main.gf"),
            "struct First { public int X;\n#include \"members.gfh\"\n}\nstruct Second { public int Y;\n#include \"members.gfh\"\n}");
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true, IncludeLoader = _ => header });
        Assert.DoesNotContain(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        string text = Hover(new(snapshot, [source, header]), header, Body.LastIndexOf("typeof", StringComparison.Ordinal))
            .GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("(reflection)typeof(*this)", text);
        Assert.DoesNotContain("file is included", text);
        Assert.DoesNotContain("**Name:** First", text);
        Assert.DoesNotContain("**Type:** First", text);
    }

    [Fact]
    public void EnclosingTypeRecoveryDoesNotSupplyARuntimeReceiverInStaticMethods()
    {
        const string text = "struct Box<T> { public int Value; public static int Invalid()=>this.Value; }";
        var source = new SourceFile(Path.GetFullPath("static-receiver.gf"), text);
        var model = new EditorModel(new AnalysisSnapshot([source], new() { NoStdLib = true }), [source]);
        int offset = text.IndexOf("this.", StringComparison.Ordinal) + 5;
        var items = JsonSerializer.SerializeToElement(model.Completion(source.Path, offset), Protocol.JsonOptions)
            .GetProperty("items").EnumerateArray();
        Assert.DoesNotContain(items, item => item.GetProperty("label").GetString() == "Value");
    }
}
