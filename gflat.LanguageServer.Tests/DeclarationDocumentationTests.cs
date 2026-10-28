using System.Text.Json;
using gflat.ast;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class DeclarationDocumentationTests
{
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void AdjacentProsePreservesParagraphsAndAppearsInPortableRichAndCompletion(string newline)
    {
        string library = string.Join(newline, ["/// First paragraph", "///", "/// Second paragraph with [literal] <tags> and `ticks`", "public int Read(int value) => value;"]);
        var fixture = Analyze(library, "int main() => Read(1);");
        int use = fixture.Client.Text.IndexOf("Read", StringComparison.Ordinal);
        string documentation = "First paragraph\n\nSecond paragraph with [literal] <tags> and `ticks`";
        var portable = Hover(fixture, use);
        string markdown = portable.GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("First paragraph\n\nSecond paragraph", markdown);
        Assert.Contains(@"\[literal\]", markdown); Assert.Contains(@"\<tags\>", markdown); Assert.Contains(@"\`ticks\`", markdown);
        Assert.False(portable.TryGetProperty("_vs_rawContent", out _));
        var rich = Hover(fixture, use, true);
        var rows = rich.GetProperty("_vs_rawContent").GetProperty("Elements").EnumerateArray().ToArray();
        Assert.Contains(rows.Skip(1), row => row.GetProperty("Runs").EnumerateArray().Any(run => run.GetProperty("Text").GetString() == documentation));
        var completion = Completion(fixture, use, "Read");
        Assert.Equal("plaintext", completion.GetProperty("documentation").GetProperty("kind").GetString());
        Assert.Equal(documentation, completion.GetProperty("documentation").GetProperty("value").GetString());
        Assert.Contains("Read(int value)", completion.GetProperty("detail").GetString());
        Assert.Equal(fixture.Model.Definition(fixture.Library.Path, library.IndexOf("Read", StringComparison.Ordinal)), fixture.Model.Definition(fixture.Client.Path, use));
        fixture.AssertUnchanged();
    }

    [Theory]
    [InlineData("/// detached\n\n")]
    [InlineData("/// detached\n// ordinary\n")]
    [InlineData("/// detached\n/* intervening */\n")]
    [InlineData("/*\n/// fake block documentation\n*/\n")]
    [InlineData("/*\n/// hidden */\n")]
    [InlineData("//// ordinary four-slash comment\n")]
    [InlineData("int Before() => 0; /// trailing\n")]
    [InlineData("readonly(char)* text = \"/// literal\";\n")]
    public void DetachedOrdinaryTrailingAndLiteralTextDoesNotBecomeDocumentation(string prefix)
    {
        var fixture = Analyze(prefix + "public int Read(int value) => value;", "int main() => Read(1);");
        int use = fixture.Client.Text.IndexOf("Read", StringComparison.Ordinal);
        Assert.False(Completion(fixture, use, "Read").TryGetProperty("documentation", out _));
        string hover = Hover(fixture, use).GetProperty("contents").GetProperty("value").GetString()!;
        Assert.DoesNotContain("detached", hover); Assert.DoesNotContain("fake block", hover); Assert.DoesNotContain("trailing", hover);
        fixture.AssertUnchanged();
    }

    [Fact]
    public void DocumentationBeforeAttributesUsesTheWholeDeclarationStart()
    {
        var fixture = Analyze("class Note : Attribute {}\n/// Exact attribute owner\n[Note]\npublic int Read(int value) => value;", "int main() => Read(1);");
        int use = fixture.Client.Text.IndexOf("Read", StringComparison.Ordinal);
        Assert.Equal("Exact attribute owner", Completion(fixture, use, "Read").GetProperty("documentation").GetProperty("value").GetString());
        Assert.Contains("Exact attribute owner", Hover(fixture, use).GetProperty("contents").GetProperty("value").GetString());
        fixture.AssertUnchanged();
    }

    [Fact]
    public void SelectedOverloadUsesOnlyItsOwnDeclarationDocumentation()
    {
        var fixture = Analyze("/// Integer overload\nint Read(int value) => value;\n/// Boolean overload\nint Read(bool value) => 2;", "int main() => Read(true);");
        string hover = Hover(fixture, fixture.Client.Text.IndexOf("Read", StringComparison.Ordinal)).GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("Boolean overload", hover); Assert.DoesNotContain("Integer overload", hover);
        Assert.Contains("Read(bool value)", hover); Assert.Contains("+1 overload", hover);
        var completion = Completion(fixture, fixture.Client.Text.IndexOf("Read", StringComparison.Ordinal), "Read");
        Assert.Equal(completion.GetProperty("detail").GetString()!.Contains("bool value", StringComparison.Ordinal) ? "Boolean overload" : "Integer overload",
            completion.GetProperty("documentation").GetProperty("value").GetString());
        fixture.AssertUnchanged();
    }

    [Fact]
    public void AliasHasItsOwnProseAndAnUndocumentedAliasDoesNotBorrowTargetProse()
    {
        const string library = "/// Target prose\nstruct Target {}\n/// Alias prose\nalias Named = Target;\nalias Bare = Target;";
        var fixture = Analyze(library, "void Use(Named named, Bare bare) {} int main() => 0;");
        int named = fixture.Client.Text.IndexOf("Named", StringComparison.Ordinal), bare = fixture.Client.Text.IndexOf("Bare", StringComparison.Ordinal);
        string hover = Hover(fixture, named).GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("Alias prose", hover); Assert.DoesNotContain("Target prose", hover);
        Assert.DoesNotContain("Target prose", Hover(fixture, bare).GetProperty("contents").GetProperty("value").GetString());
        Assert.False(Completion(fixture, bare, "Bare").TryGetProperty("documentation", out _));
        fixture.AssertUnchanged();
    }

    [Fact]
    public void GenericMethodFieldAndPropertyUseSourceProseWithConcreteSignatures()
    {
        const string library = "struct Box<T> {\n/// Field prose\npublic T Value;\n/// Property prose\npublic T Item { get => Value; }\n/// Method prose\npublic T Read(T value) => value;\npublic Box(T value) { Value = value; } }";
        const string client = "int main() { Box<int> box = new(3); int field = box.Value; int item = box.Item; return box.Read(field + item); }";
        var fixture = Analyze(library, client);
        foreach (string name in new[] { "Value", "Item", "Read" })
        {
            int use = client.IndexOf("box." + name, StringComparison.Ordinal) + 4;
            string hover = Hover(fixture, use).GetProperty("contents").GetProperty("value").GetString()!;
            Assert.Contains(name == "Value" ? "Field prose" : name == "Item" ? "Property prose" : "Method prose", hover);
            Assert.Contains("int", hover); Assert.DoesNotContain(" T ", hover);
            Assert.Equal(fixture.Model.Definition(fixture.Library.Path, library.IndexOf(name, StringComparison.Ordinal)), fixture.Model.Definition(fixture.Client.Path, use));
        }
        fixture.AssertUnchanged();
    }

    [Fact]
    public void OpenGenericStaticGroupAndInheritedMethodRetainTheOriginalSource()
    {
        const string library = "class Bridge<T> {\n/// Open generic prose\npublic static int Compare(T left, T right) => 0; }\nclass Base {\n/// Base method prose\npublic int Read() => 1; } class Derived : Base {}";
        const string client = "void Consume<T>(int(T,T)* callback) {} void Template<U>() => Consume<U>(Bridge<U>::Compare); int main() { Derived value = new(); return value.Read(); }";
        var fixture = Analyze(library, client);
        int compare = client.IndexOf("Compare", StringComparison.Ordinal), read = client.IndexOf("Read", StringComparison.Ordinal);
        string hover = Hover(fixture, compare).GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("Open generic prose", hover); Assert.Contains("U left", hover);
        Assert.Equal("Open generic prose", Completion(fixture, compare, "Compare").GetProperty("documentation").GetProperty("value").GetString());
        Assert.Contains("Base method prose", Hover(fixture, read).GetProperty("contents").GetProperty("value").GetString());
        Assert.Equal(fixture.Model.Definition(fixture.Library.Path, library.IndexOf("Read", StringComparison.Ordinal)), fixture.Model.Definition(fixture.Client.Path, read));
        fixture.AssertUnchanged();
    }

    [Fact]
    public void LocalsAndTypeParametersDoNotBorrowSurroundingDeclarationProse()
    {
        var fixture = Analyze("/// Owner prose\nstruct Box<T> { public T Value; }", "int main() {\n/// Local prose\nint local = 2; return local; }");
        int use = fixture.Client.Text.LastIndexOf("local", StringComparison.Ordinal);
        Assert.DoesNotContain("Local prose", Hover(fixture, use).GetProperty("contents").GetProperty("value").GetString());
        Assert.False(Completion(fixture, use, "local").TryGetProperty("documentation", out _));
        int typeParameter = fixture.Library.Text.IndexOf("<T>", StringComparison.Ordinal) + 1;
        Assert.DoesNotContain("Owner prose", Json(fixture.Model.Hover(fixture.Library.Path, typeParameter)).GetProperty("contents").GetProperty("value").GetString());
        fixture.AssertUnchanged();
    }

    [Theory]
    [InlineData("class Named {}")]
    [InlineData("struct Named {}")]
    [InlineData("interface Named {}")]
    [InlineData("enum Named { Value }")]
    [InlineData("extern int Named(int value) nothrow;")]
    public void NamedTypesAndExternsUseTheirExactDeclarationProse(string declaration)
    {
        var fixture = Analyze("/// Named declaration prose\n" + declaration, "int main() => 0;");
        int use = fixture.Library.Text.IndexOf("Named", fixture.Library.Text.IndexOf('\n') + 1, StringComparison.Ordinal);
        string hover = Json(fixture.Model.Hover(fixture.Library.Path, use)).GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("Named declaration prose", hover);
        fixture.AssertUnchanged();
    }

    [Fact]
    public void ConstructorsAndEnumMembersUseTheirOwnProseWithoutBorrowingContainerProse()
    {
        const string library = "/// Container prose\nstruct Item {\n/// Constructor prose\npublic Item(int value) {} }\nenum Tone {\n/// Enum member prose\nBright }";
        var fixture = Analyze(library, "int main() { Item value = new(2); return 0; }");
        string constructor = Hover(fixture, fixture.Client.Text.IndexOf("new", StringComparison.Ordinal)).GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("Constructor prose", constructor); Assert.DoesNotContain("Container prose", constructor);
        string member = Json(fixture.Model.Hover(fixture.Library.Path, library.IndexOf("Bright", StringComparison.Ordinal))).GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("Enum member prose", member);
        fixture.AssertUnchanged();
    }

    [Fact]
    public void ARecoveredIncompleteDeclarationDoesNotInventDocumentation()
    {
        var source = new SourceFile(Path.GetFullPath("incomplete-doc.gf"), "/// Incomplete prose\npublic int Missing(");
        var snapshot = new AnalysisSnapshot([source]);
        Assert.Contains(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, [source]);
        Assert.DoesNotContain("Incomplete prose", Json(model.Completion(source.Path, source.Text.Length)).GetRawText());
        var hover = Json(model.Hover(source.Path, source.Text.IndexOf("Missing", StringComparison.Ordinal)));
        Assert.DoesNotContain("Incomplete prose", hover.GetRawText());
    }

    private static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value, Protocol.JsonOptions);
    private static JsonElement Hover(Fixture fixture, int offset, bool rich = false)
        => Json(fixture.Model.Hover(fixture.Client.Path, offset, rich ? new VisualStudioHover(Json(new Dictionary<string, object> {
            ["method.public"] = new { guid = "00000000-0000-0000-0000-000000000001", id = 1 } })) : null));
    private static JsonElement Completion(Fixture fixture, int offset, string label)
        => Json(fixture.Model.Completion(fixture.Client.Path, offset)).GetProperty("items").EnumerateArray().Single(item => item.GetProperty("label").GetString() == label);

    private static Fixture Analyze(string library, string client)
    {
        var declaration = new SourceFile(Path.GetFullPath("unsaved-doc-library.gf"), library);
        var caller = new SourceFile(Path.GetFullPath("unsaved-doc-caller.gf"), client);
        var snapshot = new AnalysisSnapshot([declaration, caller]);
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var types = snapshot.Nodes.Select(syntax => syntax.Node).OfType<TypeExpression>().Where(type => type.Span.Source != null).ToArray();
        var spans = types.Select(type => type.Span).ToArray(); int classes = snapshot.Checker!.GetAllClasses().Count();
        var model = new EditorModel(snapshot, [declaration, caller]);
        var result = new Fixture(declaration, caller, snapshot, model, types, spans, classes);
        result.AssertUnchanged(); return result;
    }

    private sealed record Fixture(SourceFile Library, SourceFile Client, AnalysisSnapshot Snapshot, EditorModel Model,
        TypeExpression[] Types, SourceSpan[] Spans, int Classes)
    {
        public void AssertUnchanged()
        {
            Assert.Equal(Classes, Snapshot.Checker!.GetAllClasses().Count());
            Assert.Equal(Spans, Types.Select(type => type.Span));
        }
    }
}
