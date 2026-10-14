using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class GenericTypeParameterEditorTests
{
    private static (EditorModel Model, SourceFile Source) Analyze(string text)
    {
        var source = new SourceFile(Path.GetFullPath("generic-type-parameters.gf"), text);
        var snapshot = new AnalysisSnapshot([source]);
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        return (new EditorModel(snapshot, [source]), source);
    }

    private static Dictionary<int, string> Classifications(EditorModel model, string path)
    {
        var data = JsonSerializer.SerializeToElement(model.SemanticTokens(path), Protocol.JsonOptions)
            .GetProperty("data").EnumerateArray().Select(value => value.GetInt32()).ToArray();
        var result = new Dictionary<int, string>();
        int column = 0;
        for (int i = 0; i < data.Length; i += 5)
        {
            Assert.Equal(0, data[i]);
            column += data[i + 1];
            result[column] = EditorModel.TokenTypes[data[i + 3]];
        }
        return result;
    }

    [Fact]
    public void UnspecializedManagedAdoptTypeParameterHasOneSourceIdentity()
    {
        const string text = "namespace GC; public weak T^ Adopt<T>(T* storage) { nuint size = (nuint)sizeof(T); return (T^)storage; } " +
            "private void* EnterFrame() nothrow => (void*)1; private void AddRoot(void* frame, void* address, nuint bytes) nothrow {} " +
            "private void LeaveFrame(void* frame) nothrow {} private bool TrackNative(void* address, nuint bytes) nothrow => true; " +
            "private void ForgetNative(void* address) nothrow {} public nuint Collect() => 0;";
        var (model, source) = Analyze(text);
        var occurrences = Lexer.Tokenize(source, new DiagnosticBag()).Where(token => token.Text == "T").ToArray();
        Assert.Equal(5, occurrences.Length);
        var classifications = Classifications(model, source.Path);
        var declaration = model.Definition(source.Path, occurrences[1].Start);
        Assert.NotNull(declaration);
        foreach (var token in occurrences)
        {
            Assert.Equal("typeParameter", classifications[token.Start]);
            Assert.Equal(declaration, model.Definition(source.Path, token.Start));
            var hover = JsonSerializer.SerializeToElement(model.Hover(source.Path, token.Start), Protocol.JsonOptions).ToString();
            Assert.Contains("type parameter", hover);
            Assert.DoesNotContain("Adopt(", hover);
        }
        Assert.Equal(5, model.References(source.Path, occurrences[0].Start, true).Length);
        int kind = Array.IndexOf(EditorModel.TokenTypes, "typeParameter");
        Assert.Equal("type parameter name", EditorModel.VisualStudioTokenTypes[kind]);
        var native = JsonSerializer.SerializeToElement(model.Hover(source.Path, occurrences[0].Start,
            new VisualStudioHover(JsonSerializer.SerializeToElement(new { }))), Protocol.JsonOptions);
        var runs = native.GetProperty("_vs_rawContent").GetProperty("Elements")[0].GetProperty("Elements")
            .EnumerateArray().Single(element => element.GetProperty("_vs_type").GetString() == "ClassifiedTextElement")
            .GetProperty("Runs").EnumerateArray();
        Assert.Contains(runs, run => run.GetProperty("Text").GetString() == "T" &&
            run.GetProperty("ClassificationTypeName").GetString() == "type parameter name");
    }

    [Theory]
    [InlineData("struct Box<T> { public T* field; public T Read(T item) => item; }")]
    [InlineData("class Box<T> { public T* field; public T Read(T item) => item; }")]
    public void GenericOwnersAndInterfaceMethodsBindAllTypeOccurrences(string text)
    {
        var (model, source) = Analyze(text);
        var occurrences = Lexer.Tokenize(source, new DiagnosticBag()).Where(token => token.Text == "T").ToArray();
        var classifications = Classifications(model, source.Path);
        var definition = model.Definition(source.Path, occurrences[0].Start);
        Assert.NotNull(definition);
        foreach (var token in occurrences)
        {
            Assert.Equal("typeParameter", classifications[token.Start]);
            Assert.Equal(definition, model.Definition(source.Path, token.Start));
        }
    }

    [Fact]
    public void SeparateGenericDefinitionsDoNotShareTheirParameterIdentity()
    {
        const string text = "T First<T>(T item) => item; T Second<T>(T item) => item;";
        var (model, source) = Analyze(text);
        Assert.NotEqual(model.Definition(source.Path, 0), model.Definition(source.Path, text.IndexOf("T Second", StringComparison.Ordinal)));
        Assert.Equal(3, model.References(source.Path, 0, true).Length);
    }

    [Fact]
    public void UnsupportedInterfaceGenericMethodRetainsSourceTypeParameterBindings()
    {
        const string text = "interface IBox { T Read<T>(T item); }";
        var source = new SourceFile(Path.GetFullPath("generic-interface-recovery.gf"), text);
        var snapshot = new AnalysisSnapshot([source]);
        // Generic interface methods are parsed, but not supported by the checker.
        Assert.Contains(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, [source]);
        var occurrences = Lexer.Tokenize(source, new DiagnosticBag()).Where(token => token.Text == "T").ToArray();
        var classifications = Classifications(model, source.Path);
        foreach (var token in occurrences)
        {
            Assert.Equal("typeParameter", classifications[token.Start]);
            Assert.NotNull(model.Definition(source.Path, token.Start));
        }
        Assert.Equal(3, model.References(source.Path, occurrences[0].Start, true).Length);
    }

    [Fact]
    public void ValueNamedLikeTypeParameterKeepsItsValueIdentity()
    {
        const string text = "T Identity<T>(T item) { int T = 1; T = 2; return item; }";
        var (model, source) = Analyze(text);
        int declaration = text.IndexOf("T = 1", StringComparison.Ordinal);
        int assignment = text.IndexOf("T = 2", StringComparison.Ordinal);
        var classifications = Classifications(model, source.Path);
        Assert.Equal("variable", classifications[assignment]);
        Assert.Equal(model.Definition(source.Path, declaration), model.Definition(source.Path, assignment));
        Assert.NotEqual(model.Definition(source.Path, 0), model.Definition(source.Path, assignment));
    }

    [Fact]
    public void FieldNamedLikeTypeParameterKeepsItsValueIdentity()
    {
        const string text = "struct Box<T> { public int T; public int Read() => T; }";
        var (model, source) = Analyze(text);
        int declaration = text.IndexOf("T;", StringComparison.Ordinal);
        int use = text.LastIndexOf("T;", StringComparison.Ordinal);
        Assert.Equal("property", Classifications(model, source.Path)[use]);
        Assert.Equal(model.Definition(source.Path, declaration), model.Definition(source.Path, use));
        Assert.NotEqual(model.Definition(source.Path, text.IndexOf("<T", StringComparison.Ordinal) + 1), model.Definition(source.Path, use));
    }
}

