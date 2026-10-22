using System.Text.Json;
using gflat.ast;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class BoundFieldDisplayTests
{
    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    public void FieldsPreserveAliasTypesUnderAConflictingCallerAlias(string kind)
    {
        string library = "namespace Lib{alias Word=int;" + kind + " Box<T>{public T value;public Word tag;public Box(T initial){value=initial;tag=1;}}}";
        const string usage = "alias Word=long;int main(){Lib::Box<int> box=new Lib::Box<int>(1);box.tag=42;return box.tag+box.value;}";
        var (model, _, source, declaration) = Analyze(usage, library);
        foreach (int position in Positions(usage, "tag"))
        {
            Assert.Contains("Word Lib::Box<int>.tag", Hover(model, source, position));
            var target = model.Definition(declaration.Path, library.IndexOf("tag;", StringComparison.Ordinal));
            Assert.NotNull(target);
            Assert.Equal(target, model.Definition(source.Path, position));
        }
        Assert.Contains("int Lib::Box<int>.value", Hover(model, source, usage.IndexOf("value", StringComparison.Ordinal)));
    }

    [Fact]
    public void ReceiverReadonlyDoesNotBecomeAFieldDeclarationQualifier()
    {
        const string library = "namespace Lib{struct Box<T>{public T value;public immutable(T) fixedValue;public Box(T initial){value=initial;fixedValue=initial;}}}";
        const string usage = "int main(){Lib::Box<int> box=new Lib::Box<int>(1);immutable(Lib::Box<int>)* view=&box;return view.value+view.fixedValue;}";
        var (model, _, source, _) = Analyze(usage, library);
        Assert.Contains("public int Lib::Box<int>.value", Hover(model, source, usage.IndexOf("value", StringComparison.Ordinal)));
        Assert.Contains("public readonly(int) Lib::Box<int>.fixedValue", Hover(model, source, usage.IndexOf("fixedValue", StringComparison.Ordinal)));
    }

    [Fact]
    public void QualifiedStaticFieldsUseTheirRegisteredBoundType()
    {
        const string library = "namespace Lib{struct Box<T>{public static T value;}}";
        const string usage = "int main(){Lib::Box<int> force=new Lib::Box<int>();return Lib::Box<int>::value;}";
        var (model, _, source, _) = Analyze(usage, library);
        foreach (int position in Positions(usage, "value")) Assert.Contains("public static int Lib::Box<int>::value", Hover(model, source, position));
    }

    [Fact]
    public void InheritedFieldsKeepTheirActualDeclaringOwner()
    {
        const string library = "namespace Lib{class Base{public int value;}class Box<T>:Lib::Base{public T extra;}}";
        const string usage = "int main(){Lib::Box<int> box=new Lib::Box<int>();box.value=42;return box.value;}";
        var (model, _, source, _) = Analyze(usage, library);
        foreach (int position in Positions(usage, "value")) Assert.Contains("int Base.value", Hover(model, source, position));
    }

    [Fact]
    public void NestedGenericOwnerFieldTypesAndTemplateUsesStaySeparate()
    {
        const string library = "namespace Lib{struct Box<T>{public struct Inner{public T value;public Inner(T initial){value=initial;}public T Read()=>this.value;}}}";
        const string usage = "int main(){Lib::Box<int>::Inner first=new Lib::Box<int>::Inner(1);Lib::Box<long>::Inner second=new Lib::Box<long>::Inner(2l);first.Read();second.Read();return first.value+(int)second.value;}";
        var (model, _, source, declaration) = Analyze(usage, library);
        var positions = Positions(usage, "value").ToArray();
        Assert.Contains("public int ", Hover(model, source, positions[0]));
        Assert.Contains("public long ", Hover(model, source, positions[1]));
        string template = Hover(model, declaration, library.LastIndexOf("value", StringComparison.Ordinal));
        Assert.Contains("public T ", template);
    }

    [Fact]
    public void ConstFieldReferencesKeepTheirModifiersAndInitializerLiteralHover()
    {
        const string library = "namespace Lib{const int literal=42;struct Box<T>{public const int answer=42;}}";
        const string usage = "int main(){Lib::Box<int> force=new Lib::Box<int>();return Lib::Box<int>::answer;}";
        var (model, _, source, declaration) = Analyze(usage, library);
        string field = Hover(model, source, usage.IndexOf("answer", StringComparison.Ordinal));
        Assert.Contains("const int Lib::Box<int>::answer", field);
        Assert.Contains("(int)42", Hover(model, declaration, library.IndexOf("42", StringComparison.Ordinal)));
    }

    [Fact]
    public void BoundFieldsKeepTheirClassificationAcrossRepeatedQueries()
    {
        const string library = "namespace Lib{struct Box<T>{public T value;public Box(T initial){value=initial;}}}";
        const string usage = "int main(){Lib::Box<int> box=new Lib::Box<int>(42);return box.value;}";
        var (model, _, source, _) = Analyze(usage, library);
        int position = usage.IndexOf("value", StringComparison.Ordinal);
        string expected = Hover(model, source, position);
        for (int i = 0; i < 3; i++) Assert.Equal(expected, Hover(model, source, position));
        var data = JsonSerializer.SerializeToElement(model.SemanticTokens(source.Path), Protocol.JsonOptions)
            .GetProperty("data").EnumerateArray().Select(value => value.GetInt32()).ToArray();
        int column = 0;
        string? classification = null;
        for (int i = 0; i < data.Length; i += 5)
        {
            column += data[i + 1];
            if (column == position) classification = EditorModel.TokenTypes[data[i + 3]];
        }
        Assert.Equal("property", classification);
    }

    [Fact]
    public void InaccessibleFieldsDoNotGainAReferenceBindingAfterAnError()
    {
        const string library = "namespace Lib{class Box<T>{private T value;public Box(T initial){value=initial;}}}";
        const string usage = "int main(){Lib::Box<int> box=new Lib::Box<int>(42);return box.value;}";
        var source = new SourceFile(Path.GetFullPath("bound-field-private-main.gf"), usage);
        var declaration = new SourceFile(Path.GetFullPath("bound-field-private-library.gf"), library);
        var snapshot = new AnalysisSnapshot([declaration, source], new() { NoStdLib=true });
        Assert.Contains(snapshot.Diagnostics, diagnostic => diagnostic.Message.Contains("private field", StringComparison.Ordinal));
        var model = new EditorModel(snapshot, [declaration, source]);
        Assert.Null(model.Hover(source.Path, usage.IndexOf("value", StringComparison.Ordinal)));
        Assert.Null(model.Definition(source.Path, usage.IndexOf("value", StringComparison.Ordinal)));
    }

    private static IEnumerable<int> Positions(string source, string text)
    {
        for (int offset = 0; (offset = source.IndexOf(text, offset, StringComparison.Ordinal)) >= 0; offset += text.Length) yield return offset;
    }
    private static (EditorModel Model, AnalysisSnapshot Snapshot, SourceFile Source, SourceFile Declaration) Analyze(string main, string library)
    {
        var source = new SourceFile(Path.GetFullPath("bound-field-main.gf"), main);
        var declaration = new SourceFile(Path.GetFullPath("bound-field-library.gf"), library);
        var snapshot = new AnalysisSnapshot([declaration, source], new() { NoStdLib=true });
        Assert.DoesNotContain(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        return (new(snapshot, [declaration, source]), snapshot, source, declaration);
    }
    private static string Hover(EditorModel model, SourceFile source, int offset)
        => JsonSerializer.SerializeToElement(model.Hover(source.Path, offset), Protocol.JsonOptions).GetProperty("contents").GetProperty("value").GetString()!;
}
