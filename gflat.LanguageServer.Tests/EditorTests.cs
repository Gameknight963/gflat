using System.Text.Json;
using gflat.LanguageServer;

namespace gflat.LanguageServer.Tests;

public class EditorTests
{
    [Fact]
    public void InlineDeclarationsAndTypeMetadataHaveEditorServices()
    {
        var (model, path, offset, _) = Analyze("inline int Read() => 42; int main() => Re|ad();");
        Assert.Contains("inline int Read", Json(model.Hover(path, offset)).ToString());
        var (types, tp, to, _) = Analyze("int main() { return (int)typeof(int).|; }");
        var labels = Labels(types.Completion(tp, to));
        Assert.Contains("Size", labels);
        Assert.Contains("Alignment", labels);
        Assert.Contains("Name", labels);
        var (hover, hp, ho, _) = Analyze("int main() { return (int)typeof(int).Si|ze; }");
        Assert.NotNull(hover.Hover(hp, ho));
        Assert.NotNull(hover.Definition(hp, ho));
    }

    [Fact]
    public void NamespacePropertiesHaveHoverNavigationAndCompletion()
    {
        var (model, path, offset, _) = Analyze("int F() { return Settings::Va|lue; }", "namespace Settings; public int Value { get => 42; set {} }");
        string hover = Json(model.Hover(path, offset)).GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("int Settings::Value", hover);
        Assert.DoesNotContain("static", hover);
        Assert.Contains("library.gf", Json(model.Definition(path, offset)).ToString());
        var (completion, cp, co, _) = Analyze("int F() { return Settings::|; }", "namespace Settings; public int Value => 42;");
        Assert.Contains("Value", Labels(completion.Completion(cp, co)));
        Assert.DoesNotContain(Labels(completion.Completion(cp, co)), label => label.Contains("$get$"));
    }

    [Fact]
    public void NamespacePropertyPrivateAccessIsRespectedByCompletion()
    {
        var (model, path, offset, _) = Analyze("using Settings; int F() { | }", "namespace Settings; private int Secret => 42; public int Value => 1;");
        Assert.Contains("Value", Labels(model.Completion(path, offset)));
        Assert.DoesNotContain("Secret", Labels(model.Completion(path, offset)));
    }

    [Theory]
    [InlineData("FileHandle")]
    [InlineData("Handles::FileHandle")]
    public void AliasNamesSurviveInHoverAndSignatureHelp(string alias)
    {
        string path = Path.GetFullPath("alias-hover.gf");
        string text = "alias FileHandle = void*; namespace Handles { alias FileHandle = void*; } namespace Console { " + alias + " stdout() => null; void Use(" + alias + " handle) {} } void F() { Console::Use(Console::stdout()); }";
        var file = new SourceFile(path, text);
        var model = new EditorModel(new AnalysisSnapshot([file]), [file]);
        foreach (int offset in new[] { text.IndexOf("stdout", StringComparison.Ordinal), text.LastIndexOf("stdout", StringComparison.Ordinal), text.IndexOf("handle", StringComparison.Ordinal) })
        {
            string hover = Json(model.Hover(path, offset)).GetProperty("contents").GetProperty("value").GetString()!;
            Assert.Contains(alias, hover);
            Assert.DoesNotContain("void*", hover);
        }
        var signature = Json(model.SignatureHelp(path, text.LastIndexOf("Use(", StringComparison.Ordinal) + 4));
        Assert.Contains(alias + " handle", signature.GetProperty("signatures")[0].GetProperty("label").GetString());
    }

    [Fact]
    public void PrivateExternHoverAndCompletionRespectFileAccess()
    {
        string path = Path.GetFullPath("private-extern.gf");
        string other = Path.GetFullPath("caller.gf");
        string text = "namespace detail { private extern int abs(int value); } int F() => detail::abs(-42);";
        var files = new SourceFile[] { new(path, text), new(other, "using detail; int F2() { return 0; }") };
        var model = new EditorModel(new AnalysisSnapshot(files), files);
        int use = text.LastIndexOf("abs", StringComparison.Ordinal);
        var hover = Json(model.Hover(path, use));
        Assert.Contains("private extern int", hover.GetProperty("contents").GetProperty("value").GetString());
        Assert.NotNull(model.Definition(path, use));
        int body = files[1].Text.IndexOf("return", StringComparison.Ordinal);
        Assert.DoesNotContain("abs", Labels(model.Completion(other, body)));
    }

    [Fact]
    public void BodylessFreeFunctionHasAnEditorDiagnostic()
    {
        var snapshot = new AnalysisSnapshot([new SourceFile("missing.gf", "int SomeNonExistantFunction();")]);
        Assert.Contains(snapshot.Diagnostics, d => d.Severity == gflat.diagnostics.DiagnosticSeverity.Error && d.Message.Contains("body"));
    }

    [Fact]
    public void InterfaceCallsHaveHoverNavigationAndMethodColor()
    {
        var (model, path, offset, source) = Analyze("void Format(char value, ITextWriter* output) { output.Wr|ite(&value, 1); }");
        Assert.NotNull(model.Hover(path, offset));
        Assert.NotNull(model.Definition(path, offset));
        var data = Json(model.SemanticTokens(path)).GetProperty("data").EnumerateArray().Select(x => x.GetInt32()).ToArray();
        int column = 0;
        bool found = false;
        for (int i = 0; i < data.Length; i += 5)
        {
            column += data[i + 1];
            if (column != source.IndexOf(".Write", StringComparison.Ordinal) + 1) continue;
            Assert.Equal("method", EditorModel.TokenTypes[data[i + 3]]);
            found = true;
        }
        Assert.True(found);
    }

    [Fact]
    public void AccessorKeywordsHaveSemanticColors()
    {
        var (model, path, _, source) = Analyze("struct S { int n; public int Value { immutable get { return n; } set { n = value; } } } void |F() {}");
        var data = Json(model.SemanticTokens(path)).GetProperty("data").EnumerateArray().Select(x => x.GetInt32()).ToArray();
        int column = 0, count = 0;
        for (int i = 0; i < data.Length; i += 5)
        {
            column += data[i + 1];
            if (source.Substring(column, data[i + 2]) is not ("get" or "set")) continue;
            Assert.Equal("modifier", EditorModel.TokenTypes[data[i + 3]]);
            count++;
        }
        Assert.Equal(2, count);
    }

    [Fact]
    public void ReferenceParametersAndArgumentsHaveKeywordClassification()
    {
        var (model, path, offset, source) = Analyze("void Set(ref int value) { value = 42; } int main() { int n = 0; Set(ref |n); return n; }");
        Assert.NotNull(model.Definition(path, offset));
        var data = Json(model.SemanticTokens(path)).GetProperty("data").EnumerateArray().Select(x => x.GetInt32()).ToArray();
        int column = 0, count = 0;
        for (int i = 0; i < data.Length; i += 5)
        {
            column += data[i + 1];
            if (source.Substring(column, data[i + 2]) != "ref") continue;
            Assert.Equal("modifier", EditorModel.TokenTypes[data[i + 3]]);
            count++;
        }
        Assert.Equal(2, count);
    }

    [Fact]
    public void WorkspaceUsesUnsavedLibrarySnapshotsAndWatchesTheirFiles()
    {
        string app = Path.Combine(Path.GetTempPath(), "gflat-stdlib-editor", "main.gf");
        var library = StandardLibrary.Load("core/ITextWriter.gf");
        var documents = new Dictionary<string, OpenDocument> {
            [app] = new(app, "int main() => UnsavedLibraryAnswer();", 1),
            [library.Path] = new(library.Path, library.Text + "int UnsavedLibraryAnswer() => 42;", 2)
        };
        var result = Workspace.Analyze(null, documents);
        Assert.DoesNotContain(result.Diagnostics.Values.SelectMany(d => d), d => d.Severity == 1);
        Assert.Contains(library.Path, result.WatchedPaths);
    }

    [Theory]
    [InlineData("void F(|Exception* e) {}", "core/Exception.gf", "class Exception")]
    [InlineData("class Tag : |Attribute {}", "core/Attribute.gf", "abstract class Attribute")]
    [InlineData("void F(|ITextWriter* output) {}", "core/ITextWriter.gf", "interface ITextWriter")]
    [InlineData("void F(|IFormattable* value) {}", "core/IFormattable.gf", "interface IFormattable")]
    [InlineData("void F() { Memory::|Allocate(1); }", "libc/Memory/allocation.gf", "Allocate(nuint")]
    public void LibraryDefinitionsNavigateToShippedSource(string marked, string relative, string declaration)
    {
        var (model, path, offset, _) = Analyze(marked);
        var location = Json(model.Definition(path, offset));
        string target = new Uri(location.GetProperty("uri").GetString()!).LocalPath;
        Assert.True(StandardLibrary.IsSource(target, relative));
        string[] lines = File.ReadAllLines(target);
        int line = location.GetProperty("range").GetProperty("start").GetProperty("line").GetInt32();
        Assert.Contains(declaration, lines[line]);
    }

    [Fact]
    public void MoveAndReferenceAccessorsHaveContextualColorsAndNavigation()
    {
        var (model, path, offset, source) = Analyze("struct S { int n; public S() {} public copy S(immutable(S)* source) { n = source.n; } public ref int Value => n; } void F(S* ptr) { S value = new S(); move value at p|tr; }");
        Assert.NotNull(model.Definition(path, offset));
        var data = Json(model.SemanticTokens(path)).GetProperty("data").EnumerateArray().Select(x => x.GetInt32()).ToArray();
        var keywords = new List<string>();
        int column = 0;
        for (int i = 0; i < data.Length; i += 5)
        {
            column += data[i + 1];
            if (EditorModel.TokenTypes[data[i + 3]] == "modifier") keywords.Add(source.Substring(column, data[i + 2]));
        }
        Assert.Equal(new[] { "copy", "ref", "move", "at" }, keywords);
    }

    [Fact]
    public void ExplicitDestructorTypeKeepsTypeNavigationAndColor()
    {
        var (model, path, offset, source) = Analyze("struct S {} void F(S* p) { p.~|S(); }");
        var definition = Json(model.Definition(path, offset));
        Assert.Equal(source.IndexOf("S", StringComparison.Ordinal), definition.GetProperty("range").GetProperty("start").GetProperty("character").GetInt32());
        var data = Json(model.SemanticTokens(path)).GetProperty("data").EnumerateArray().Select(x => x.GetInt32()).ToArray();
        int column = 0;
        string? classification = null;
        for (int i = 0; i < data.Length; i += 5)
        {
            column += data[i + 1];
            if (column == source.LastIndexOf("S", StringComparison.Ordinal)) classification = EditorModel.TokenTypes[data[i + 3]];
        }
        Assert.Equal("struct", classification);
    }

    [Fact]
    public void CompletionUnderstandsNullSuppressedReceiver()
    {
        var (model, path, offset, _) = Analyze("struct S { public int Field; } void F(S*? p) { p!.| }");
        Assert.Contains("Field", Labels(model.Completion(path, offset)));
    }

    [Fact]
    public void PlacementAtIsContextualAndDestinationRetainsNavigation()
    {
        var (model, path, offset, source) = Analyze("struct S {} void F(S* ptr) { int at = 0; new* S() at p|tr; }");
        var definition = Json(model.Definition(path, offset));
        Assert.Equal(source.IndexOf("ptr", StringComparison.Ordinal), definition.GetProperty("range").GetProperty("start").GetProperty("character").GetInt32());
        var data = Json(model.SemanticTokens(path)).GetProperty("data").EnumerateArray().Select(x => x.GetInt32()).ToArray();
        var atKinds = new List<string>();
        int column = 0;
        for (int i = 0; i < data.Length; i += 5)
        {
            column += data[i + 1];
            if (source.Substring(column, data[i + 2]) == "at") atKinds.Add(EditorModel.TokenTypes[data[i + 3]]);
        }
        Assert.Equal(new[] { "variable", "modifier" }, atKinds);
        int keyword = source.IndexOf("at ptr", StringComparison.Ordinal);
        Assert.Null(model.Definition(path, keyword));
        Assert.Null(model.Hover(path, keyword));
    }

    [Fact]
    public void CompletionDetailsUseReadableGenericAndNestedTypes()
    {
        var (model, path, offset, _) = Analyze("class Outer { public class Inner {} } struct Box<T> { public T value; } Box<int> Read(Outer::Inner* value) => new Box<int>(); void Use() { Re| }");
        var item = Json(model.Completion(path, offset)).GetProperty("items").EnumerateArray().Single(i => i.GetProperty("label").GetString() == "Read");
        string detail = item.GetProperty("detail").GetString()!;
        Assert.Contains("Box<int>", detail);
        Assert.Contains("Outer::Inner*", detail);
        Assert.DoesNotContain("$", detail);
        Assert.DoesNotContain("Outer.Inner", detail);
    }

    [Fact]
    public void SignatureHelpUsesReadableTypesInBothSignatureAndParameters()
    {
        var (model, path, offset, _) = Analyze("class Outer { public class Inner {} } struct Box<T> { public T value; } Box<int> Read(Outer::Inner* value) => new Box<int>(); void Use() { Read(|new* Outer::Inner()); }");
        var signature = Json(model.SignatureHelp(path, offset)).GetProperty("signatures")[0];
        Assert.Contains("Box<int>", signature.GetProperty("label").GetString());
        Assert.Equal("Outer::Inner* value", signature.GetProperty("parameters")[0].GetProperty("label").GetString());
        Assert.False(signature.TryGetProperty("_vs_colorizedLabel", out _));
        var colored = Json(model.SignatureHelp(path, offset, visualStudio: true)).GetProperty("signatures")[0];
        var runs = colored.GetProperty("_vs_colorizedLabel").GetProperty("Runs").EnumerateArray().ToArray();
        Assert.Equal(signature.GetProperty("label").GetString(), string.Concat(runs.Select(r => r.GetProperty("Text").GetString())));
        Assert.Contains(runs, r => r.GetProperty("Text").GetString() == "Box" && r.GetProperty("ClassificationTypeName").GetString() == "struct name");
        Assert.Contains(runs, r => r.GetProperty("Text").GetString() == "Inner" && r.GetProperty("ClassificationTypeName").GetString() == "class name");
        Assert.Contains(runs, r => r.GetProperty("Text").GetString() == "int" && r.GetProperty("ClassificationTypeName").GetString() == "keyword");
    }

    [Fact]
    public void SignatureHelpClassifiesMemoryFreeWhileTyping()
    {
        var (model, path, offset, _) = Analyze("namespace Memory { public weak void Free(void* ptr) nothrow {} } void Use() { Memory::Free(|");
        var help = Json(model.SignatureHelp(path, offset, visualStudio: true));
        var signature = help.GetProperty("signatures")[0];
        Assert.Equal("public weak void Memory::Free(void* ptr) nothrow", signature.GetProperty("label").GetString());
        Assert.Equal("void* ptr", signature.GetProperty("parameters")[0].GetProperty("label").GetString());
        Assert.Equal(0, help.GetProperty("activeParameter").GetInt32());
        var runs = signature.GetProperty("_vs_colorizedLabel").GetProperty("Runs").EnumerateArray().ToArray();
        foreach (var (text, kind) in new[] { ("weak", "keyword"), ("void", "keyword"), ("Memory", "namespace name"), ("Free", "method name"), ("*", "punctuation"), ("ptr", "parameter name") })
            Assert.Contains(runs, r => r.GetProperty("Text").GetString() == text && r.GetProperty("ClassificationTypeName").GetString() == kind);
    }

    [Fact]
    public void BaseConstructorInitializerProvidesSignatureHelpAndParameterCompletion()
    {
        var (model, path, offset, _) = Analyze("class Parent { public Parent(int value, int count) {} } class Child : Parent { public Child(int value) : base(value, |) {} }");
        var help = Json(model.SignatureHelp(path, offset));
        Assert.Contains("Parent(int value, int count)", help.GetProperty("signatures")[0].GetProperty("label").GetString());
        Assert.Equal(1, help.GetProperty("activeParameter").GetInt32());
        Assert.Contains(Json(model.Completion(path, offset)).GetProperty("items").EnumerateArray(), item => item.GetProperty("label").GetString() == "value");
    }

    [Fact]
    public void ConstructorInitializerOffersBaseKeyword()
    {
        var (model, path, offset, _) = Analyze("class Parent { public Parent(int value) {} } class Child : Parent { public Child(int value) : |base(value) {} }");
        Assert.Contains(Json(model.Completion(path, offset)).GetProperty("items").EnumerateArray(), item => item.GetProperty("label").GetString() == "base");
    }

    [Fact]
    public void OptionalDefaultsAppearInCompletionAndColoredSignatureHelp()
    {
        var (model, path, offset, _) = Analyze("int Read(int count = 6 * 7) => count; void Use() { Read(|); }");
        var help = Json(model.SignatureHelp(path, offset, visualStudio: true));
        var signature = help.GetProperty("signatures")[0];
        Assert.Contains("int count = 6 * 7", signature.GetProperty("label").GetString());
        Assert.Equal("int count = 6 * 7", signature.GetProperty("parameters")[0].GetProperty("label").GetString());
        var runs = signature.GetProperty("_vs_colorizedLabel").GetProperty("Runs").EnumerateArray().ToArray();
        Assert.Equal(signature.GetProperty("label").GetString(), string.Concat(runs.Select(r => r.GetProperty("Text").GetString())));
        Assert.Contains(runs, r => r.GetProperty("Text").GetString() == "6" && r.GetProperty("ClassificationTypeName").GetString() == "number");
        var completion = Json(model.Completion(path, offset)).GetProperty("items").EnumerateArray().Single(i => i.GetProperty("label").GetString() == "Read");
        Assert.Contains("int count = 6 * 7", completion.GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData("class Child : | {}", true)]
    [InlineData("class Child : |", true)]
    [InlineData("struct Child : | {}", false)]
    [InlineData("class Child : Base, | {}", false)]
    [InlineData("class Child<T> : | {}", true)]
    public void InheritanceCompletionOnlyOffersValidKinds(string declaration, bool allowsBase)
    {
        var (model, path, offset, _) = Analyze("class Base {} interface I {} struct Value {} int Helper() => 1; " + declaration);
        var labels = Labels(model.Completion(path, offset));
        Assert.Contains("I", labels);
        Assert.Equal(allowsBase, labels.Contains("Base"));
        foreach (string invalid in new[] { "Child", "Value", "Helper", "int", "void", "class", "return", "readonly" })
            Assert.DoesNotContain(invalid, labels);
    }

    [Fact]
    public void QualifiedInheritanceFiltersNamespaceMembers()
    {
        var (model, path, offset, _) = Analyze("namespace Lib { public interface I {} public struct Value {} public int Helper() => 1; } struct Child : Lib::| {}");
        Assert.Equal(new[] { "I" }, Labels(model.Completion(path, offset)));
    }

    [Fact]
    public void InheritanceDoesNotSuggestAlreadyListedInterface()
    {
        var (model, path, offset, _) = Analyze("interface I {} interface J {} struct Child : I, | {}");
        var labels = Labels(model.Completion(path, offset));
        Assert.Contains("J", labels);
        Assert.DoesNotContain("I", labels);
    }

    [Fact]
    public void UsingCompletionOnlyOffersNamespaces()
    {
        var (model, path, offset, _) = Analyze("namespace Lib {} struct Value {} using |;");
        var items = Json(model.Completion(path, offset)).GetProperty("items").EnumerateArray().ToArray();
        Assert.Contains(items, item => item.GetProperty("label").GetString() == "Lib");
        Assert.All(items, item => Assert.Equal(9, item.GetProperty("kind").GetInt32()));
    }

    [Fact]
    public void CompletionReplacesTheWholeIdentifierWhenEditingItsMiddle()
    {
        var (model, path, offset, source) = Analyze("int helper() => 1; int main() => he|lper();");
        var item = Json(model.Completion(path, offset)).GetProperty("items").EnumerateArray().Single(i => i.GetProperty("label").GetString() == "helper");
        var range = item.GetProperty("textEdit").GetProperty("range");
        int start = source.LastIndexOf("helper", StringComparison.Ordinal);
        Assert.Equal(start, range.GetProperty("start").GetProperty("character").GetInt32());
        Assert.Equal(start + 6, range.GetProperty("end").GetProperty("character").GetInt32());
    }

    [Fact]
    public void UnclosedFunctionKeepsLocalsVisibleAtEndOfFile()
    {
        var (model, path, offset, _) = Analyze("int main(int parameter) { int local = 0; \n    |");
        var labels = Labels(model.Completion(path, offset));
        Assert.Contains("local", labels);
        Assert.Contains("parameter", labels);
        Assert.Contains("return", labels);
    }

    [Fact]
    public void SemanticRangeTokensUseAbsoluteUtf16Positions()
    {
        var (model, path, _, _) = Analyze("// 😀\r\nint first() => 1;\r\nint second() => first();|");
        var data = Json(model.SemanticTokens(path, new(new(2, 0), new(2, 24)))).GetProperty("data").EnumerateArray().Select(n => n.GetInt32()).ToArray();
        Assert.Equal(2, data[0]);
        Assert.Equal(4, data[1]);
        Assert.Equal(6, data[2]);
        for (int i = 5; i < data.Length; i += 5) Assert.Equal(0, data[i]);
    }

    [Fact]
    public void DefinitionUsesTheSelectedOverload()
    {
        var (model, path, offset, source) = Analyze("int Read(int x) => x; int Read(bool x) => 2; int main() => Re|ad(true);");
        var location = Json(model.Definition(path, offset));
        Assert.Equal(source.IndexOf("Read(bool", StringComparison.Ordinal), location.GetProperty("range").GetProperty("start").GetProperty("character").GetInt32());
    }

    [Theory]
    [InlineData("Make().|")]
    [InlineData("new Value().|")]
    public void IncompleteAccessRecoversUnambiguousReturnTypes(string expression)
    {
        var (model, path, offset, _) = Analyze("struct Value { public int Field; } Value Make() => new Value(); int main() { " + expression + "; return 0; }");
        Assert.Contains("Field", Labels(model.Completion(path, offset)));
    }

    [Fact]
    public void QualifiedNamespaceDeclarationsExposeEachSegment()
    {
        var (model, path, offset, _) = Analyze("namespace Lib::Tools { public int Run() => 1; } int main() { Lib::|; return 0; }");
        Assert.Contains("Tools", Labels(model.Completion(path, offset)));
        Assert.DoesNotContain("Run", Labels(model.Completion(path, offset)));
    }

    [Theory]
    [InlineData("int main() { // co|mment\n return 0; }")]
    [InlineData("int main() { /* co|mment */ return 0; }")]
    [InlineData("int main() { \"he|llo\"; return 0; }")]
    public void CompletionDoesNotOfferCodeInsideCommentsOrStrings(string text)
    {
        var (model, path, offset, _) = Analyze(text);
        Assert.Empty(Labels(model.Completion(path, offset)));
    }

    [Fact]
    public void PropertyHoverAndDefinitionUseTheSourceProperty()
    {
        var (model, path, offset, source) = Analyze("struct S { public int Count => 2; } int main() { S s = new S(); return s.Co|unt; }");
        Assert.Contains("int S.Count", Json(model.Hover(path, offset)).ToString());
        var location = Json(model.Definition(path, offset));
        Assert.Equal(source.IndexOf("Count", StringComparison.Ordinal), location.GetProperty("range").GetProperty("start").GetProperty("character").GetInt32());
    }

    [Fact]
    public void InheritedPublicMembersAreIncluded()
    {
        var (model, path, offset, _) = Analyze("class Base { public int Field; private int secret; } class Derived : Base {} int main() { Derived* d = new* Derived(); d.|; return 0; }");
        var labels = Labels(model.Completion(path, offset));
        Assert.Contains("Field", labels);
        Assert.DoesNotContain("secret", labels);
    }

    private static (EditorModel Model, string Path, int Offset, string Source) Analyze(string marked, string? library = null)
    {
        int offset = marked.IndexOf('|');
        string text = marked.Remove(offset, 1);
        string path = Path.GetFullPath("editor.gf");
        var files = new List<SourceFile> { new(path, text) };
        if (library != null) files.Add(new(Path.GetFullPath("library.gf"), library));
        return (new EditorModel(new AnalysisSnapshot(files), files), path, offset, text);
    }
    private static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value, Protocol.JsonOptions);
    private static string[] Labels(object completion) => Json(completion).GetProperty("items").EnumerateArray().Select(i => i.GetProperty("label").GetString()!).ToArray();

    [Fact]
    public void OrdinaryCompletionIncludesScopeAndExcludesOtherLocals()
    {
        var (model, path, offset, _) = Analyze("struct Item {} int helper(int arg) => arg; int main(int parameter) { int local = 0; { int hidden = 1; } | return 0; int later = 2; }");
        var labels = Labels(model.Completion(path, offset));
        Assert.Contains("local", labels);
        Assert.Contains("parameter", labels);
        Assert.Contains("helper", labels);
        Assert.Contains("Item", labels);
        Assert.Contains("return", labels);
        Assert.DoesNotContain("hidden", labels);
        Assert.DoesNotContain("later", labels);
        Assert.DoesNotContain("arg", labels);
    }

    [Theory]
    [InlineData("v.|")]
    [InlineData("v.Va|")]
    public void IncompleteMemberAccessOnlyOffersAccessibleInstanceMembers(string expression)
    {
        var (model, path, offset, _) = Analyze("struct Value { public int ValueField; private int secret; public static int Static() => 0; public int Read() => ValueField; } int main() { Value v = new Value(); " + expression + "; return 0; }");
        var labels = Labels(model.Completion(path, offset));
        Assert.Contains("ValueField", labels);
        Assert.Contains("Read", labels);
        Assert.DoesNotContain("secret", labels);
        Assert.DoesNotContain("Static", labels);
        Assert.DoesNotContain("main", labels);
        Assert.DoesNotContain("return", labels);
    }

    [Fact]
    public void NamespaceCompletionUsesImportsAndUnsavedSiblings()
    {
        var (model, path, offset, _) = Analyze("using Lib; int main() { Tools::|; return 0; }", "namespace Lib { namespace Tools { public int Run() => 1; public struct Item {} } } namespace Other { public int Hidden() => 0; }");
        var labels = Labels(model.Completion(path, offset));
        Assert.Contains("Run", labels);
        Assert.Contains("Item", labels);
        Assert.DoesNotContain("Hidden", labels);
    }

    [Fact]
    public void DefinitionRespectsLocalShadowing()
    {
        var (model, path, offset, source) = Analyze("int f(int value) { { int value = 2; return val|ue; } }");
        var location = Json(model.Definition(path, offset));
        Assert.Equal(source.IndexOf("value ="), location.GetProperty("range").GetProperty("start").GetProperty("character").GetInt32());
        Assert.Contains("int value", Json(model.Hover(path, offset)).ToString());
    }

    [Fact]
    public void CrossFileDefinitionTargetsDeclarationName()
    {
        var (model, path, offset, _) = Analyze("using Lib; int main() => R|un(2);", "namespace Lib; public int Run(int value) => value;");
        var location = Json(model.Definition(path, offset));
        Assert.EndsWith("library.gf", location.GetProperty("uri").GetString());
        Assert.Equal(26, location.GetProperty("range").GetProperty("start").GetProperty("character").GetInt32());
    }

    [Fact]
    public void SignatureHelpCountsOnlyDirectArguments()
    {
        var (model, path, offset, _) = Analyze("int add(int left, int right) => left + right; int main() => add(add(1, 2), |3);");
        var signature = Json(model.SignatureHelp(path, offset));
        Assert.Equal(1, signature.GetProperty("activeParameter").GetInt32());
        Assert.Contains("int left, int right", signature.GetProperty("signatures")[0].GetProperty("label").GetString());
    }

    [Fact]
    public void SemanticTokensDistinguishTypeMethodParameterAndLocal()
    {
        var (model, path, _, source) = Analyze("struct String { public static String From(int count) { String result = new String(); return result; } } |");
        var data = Json(model.SemanticTokens(path)).GetProperty("data").EnumerateArray().Select(x => x.GetInt32()).ToArray();
        var classified = new List<(string Text, string Kind)>();
        int line = 0, column = 0;
        for (int i = 0; i < data.Length; i += 5)
        {
            line += data[i]; column = data[i] == 0 ? column + data[i + 1] : data[i + 1];
            Assert.Equal(0, line);
            classified.Add((source.Substring(column, data[i + 2]), EditorModel.TokenTypes[data[i + 3]]));
        }
        Assert.Equal(4, classified.Count(x => x == ("String", "struct")));
        Assert.Contains(("From", "method"), classified);
        Assert.Contains(("count", "parameter"), classified);
        Assert.Equal(2, classified.Count(x => x == ("result", "variable")));
    }

    [Fact]
    public void CapacityInAnEarlyReturnConditionIsAField()
    {
        var (model, path, _, source) = Analyze("struct String { nuint capacity; public void Reserve(nuint requested) { if (requested <= capacity) return; } } |");
        var data = Json(model.SemanticTokens(path)).GetProperty("data").EnumerateArray().Select(x => x.GetInt32()).ToArray();
        int column = 0;
        var fields = new List<int>();
        for (int i = 0; i < data.Length; i += 5)
        {
            column += data[i + 1];
            if (source.Substring(column, data[i + 2]) == "capacity")
            {
                Assert.Equal("property", EditorModel.TokenTypes[data[i + 3]]);
                fields.Add(column);
            }
        }
        Assert.Equal(2, fields.Count);
    }

    [Fact]
    public void StaticContextDoesNotOfferInstanceFields()
    {
        var (model, path, offset, _) = Analyze("struct S { public int instance; public static int shared; public static int F() { | return 0; } }");
        var labels = Labels(model.Completion(path, offset));
        Assert.Contains("shared", labels);
        Assert.DoesNotContain("instance", labels);
    }
}
