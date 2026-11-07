using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class StructuredDocumentationTests
{
    [Fact]
    public void AGenericReferenceTargetsItsOuterDeclarationName()
    {
        const string code = "struct Item<T> {}\n/// Returns @ref Item<Item>.\nint Read()=>0;";
        var (model, source) = Analyze(code);
        Assert.Empty(model.DocumentationIssues);
        var expected = model.Definition(source.Path, code.IndexOf("Item", StringComparison.Ordinal));
        Assert.NotNull(expected);
        Assert.Equal(expected, model.Definition(source.Path, code.IndexOf("@ref Item", StringComparison.Ordinal) + 5));
    }

    [Theory]
    [InlineData("throws")]
    [InlineData("throw")]
    [InlineData("exception")]
    public void RenamingQualifiedExceptionDocumentationPreservesItsNamespace(string tag)
    {
        string code = "namespace Types { public class Problem {} }\n/// @" + tag + " Types::Problem When input is invalid\nint Read()=>0;";
        var (model, source) = Analyze(code);
        Assert.Empty(model.DocumentationIssues);
        var edits = model.Rename(source.Path, code.IndexOf("Problem", StringComparison.Ordinal), "Failure");
        string changed = code;
        foreach (var edit in edits.OrderByDescending(edit => Workspace.Offset(code, JsonSerializer.SerializeToElement(edit.Range.Start, Protocol.JsonOptions))))
        {
            int start = Workspace.Offset(code, JsonSerializer.SerializeToElement(edit.Range.Start, Protocol.JsonOptions));
            int end = Workspace.Offset(code, JsonSerializer.SerializeToElement(edit.Range.End, Protocol.JsonOptions));
            changed = changed.Remove(start, end - start).Insert(start, edit.NewText);
        }
        Assert.Contains("@" + tag + " Types::Failure", changed);
        Assert.Empty(Analyze(changed).Model.DocumentationIssues);
    }

    private static (EditorModel Model, SourceFile Source) Analyze(string text)
    {
        var source = new SourceFile(Path.GetFullPath("structured-documentation.gf"), text);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        return (new(snapshot, [source]), source);
    }

    [Fact]
    public void SummaryAndCurrentParameterAreSeparateFromDetailedDocumentation()
    {
        const string code = """
            /// @brief Reads a value.
            ///
            /// @details Longer explanation that belongs in the manual.
            /// @param value The value to read
            /// @return The supplied value
            int Read(int value) => value;
            int F() => Read(42);
            """;
        var (model, source) = Analyze(code);
        var hover = JsonSerializer.SerializeToElement(model.Hover(source.Path, code.LastIndexOf("Read", StringComparison.Ordinal)), Protocol.JsonOptions);
        string text = hover.GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("Reads a value.", text);
        Assert.DoesNotContain("Longer explanation", text);
        var help = JsonSerializer.SerializeToElement(model.SignatureHelp(source.Path, code.LastIndexOf("42", StringComparison.Ordinal)), Protocol.JsonOptions);
        var signature = Assert.Single(help.GetProperty("signatures").EnumerateArray());
        Assert.Equal("Reads a value.", signature.GetProperty("documentation").GetProperty("value").GetString());
        Assert.Equal("The value to read", Assert.Single(signature.GetProperty("parameters").EnumerateArray()).GetProperty("documentation").GetProperty("value").GetString());
        Assert.Empty(model.DocumentationIssues);
    }

    [Fact]
    public void InlineReferencesNavigateAndParticipateInRename()
    {
        const string code = """
            struct Item {}
            /// Returns @ref Item or @c null.
            /// @param value Input item
            Item Read(Item value) => value;
            """;
        var (model, source) = Analyze(code);
        int reference = code.IndexOf("@ref Item", StringComparison.Ordinal) + 5;
        Assert.NotNull(model.Definition(source.Path, reference));
        Assert.NotNull(model.Hover(source.Path, reference));
        Assert.NotNull(model.PrepareRename(source.Path, reference));
        Assert.Equal(4, model.Rename(source.Path, reference, "Thing").Length);
        int parameter = code.IndexOf("@param value", StringComparison.Ordinal) + 7;
        Assert.Equal(3, model.Rename(source.Path, parameter, "input").Length);
        var rich = JsonSerializer.SerializeToElement(model.Hover(source.Path, code.IndexOf("Read", StringComparison.Ordinal), new VisualStudioHover(JsonSerializer.SerializeToElement(new { }))), Protocol.JsonOptions);
        var runs = rich.GetProperty("_vs_rawContent").GetProperty("Elements")[1].GetProperty("Runs").EnumerateArray().ToArray();
        Assert.Contains(runs, r => r.GetProperty("Text").GetString() == "Item" && r.TryGetProperty("_gflat_target", out _));
        Assert.Contains(runs, r => r.GetProperty("Text").GetString() == "null" && r.GetProperty("ClassificationTypeName").GetString() == "keyword");
    }

    [Fact]
    public void InvalidNamesCommandsAndUnclosedExamplesProduceDocumentationIssues()
    {
        const string code = """
            /// @brief Example @ref Missing
            /// @param missing Wrong name
            /// @unknown Unsupported command
            /// @code
            /// int example = 1;
            int Read(int value) => value;
            """;
        var (model, _) = Analyze(code);
        Assert.Contains(model.DocumentationIssues, i => i.Message.Contains("Missing"));
        Assert.Contains(model.DocumentationIssues, i => i.Message.Contains("No parameter"));
        Assert.Contains(model.DocumentationIssues, i => i.Message.Contains("Unknown documentation"));
        Assert.Contains(model.DocumentationIssues, i => i.Message.Contains("@endcode"));
    }

    [Fact]
    public void ExportRetainsDetailsExamplesAndReferencesWithoutInterpretingMarkdown()
    {
        const string code = """
            /// A value.
            struct Item {}
            /// @brief Reads @ref Item.
            /// @details **This is literal text.**
            /// @code
            /// int sample = 4;
            /// @endcode
            /// @param value Input value
            Item Read(Item value) => value;
            """;
        var (model, _) = Analyze(code);
        var entries = model.ExportDocumentation();
        var method = Assert.Single(entries, e => e.Name == "Read");
        Assert.Contains(method.Sections, s => s.Kind == "details" && string.Concat(s.Content.Select(p => p.Text)) == "**This is literal text.**");
        Assert.Contains(method.Sections, s => s.Kind == "code" && string.Concat(s.Content.Select(p => p.Text)) == "int sample = 4;");
        string html = DocumentationManual.Html(entries);
        Assert.Contains("**This is literal text.**", html);
        Assert.Contains("<a href=\"#symbol-", html);
        Assert.Contains("<pre><code>int sample = 4;", html);
        Assert.DoesNotContain("<strong>", html);
    }

    [Fact]
    public void QualifiedReferencesAndTypeParametersResolveInDeclarationScope()
    {
        const string code = """
            namespace Library { public struct Item {} }
            /// Uses @ref Library::Item.
            /// @tparam T Value type
            T Echo<T>(T value) => value;
            """;
        var (model, source) = Analyze(code);
        Assert.Empty(model.DocumentationIssues);
        Assert.NotNull(model.Definition(source.Path, code.IndexOf("Library::Item", StringComparison.Ordinal) + 9));
        Assert.NotNull(model.Definition(source.Path, code.IndexOf("@tparam T", StringComparison.Ordinal) + 8));
    }

    [Fact]
    public void InheritedParameterDescriptionsFollowTheParameterPosition()
    {
        const string code = """
            class Base {
                /// Reads a value.
                /// @param input The input value
                public virtual int Read(int input) => input;
            }
            class Child : Base { public override int Read(int value) => value; }
            int F(Child* child) => child.Read(42);
            """;
        var (model, source) = Analyze(code);
        var help = JsonSerializer.SerializeToElement(model.SignatureHelp(source.Path, code.IndexOf("42", StringComparison.Ordinal)), Protocol.JsonOptions);
        Assert.Equal("The input value", help.GetProperty("signatures")[0].GetProperty("parameters")[0].GetProperty("documentation").GetProperty("value").GetString());
    }

    [Fact]
    public void UndocumentedOverridesInheritTheBaseSummary()
    {
        const string code = "class Base {\n/// Reads a value.\npublic virtual int Read(int value) => value; }\nclass Child : Base { public override int Read(int value) => value; }";
        var (model, source) = Analyze(code);
        var hover = JsonSerializer.SerializeToElement(model.Hover(source.Path, code.LastIndexOf("Read", StringComparison.Ordinal)), Protocol.JsonOptions);
        Assert.Contains("Reads a value.", hover.GetProperty("contents").GetProperty("value").GetString());
    }
}
