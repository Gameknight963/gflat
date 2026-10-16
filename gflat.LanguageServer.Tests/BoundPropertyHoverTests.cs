using System.Text.Json;
using gflat.ast;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class BoundPropertyHoverTests
{
    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    public void GenericPropertyReadsAndWritesShowBoundTypesWithoutChangingNavigation(string kind)
    {
        string library = "namespace Lib { public " + kind + " Box<T>{public T Value{get;set;} public Box(T value){Value=value;}}}";
        const string usage = "int main(){Lib::Box<int> box=new Lib::Box<int>(1);box.Value=42;return box.Value;}";
        var (model, snapshot, source, declaration) = Analyze(usage, library);
        var expected = model.Definition(declaration.Path, library.IndexOf("Value{", StringComparison.Ordinal));
        Assert.NotNull(expected);
        foreach (int position in Positions(usage, "Value"))
        {
            string hover = Hover(model, source, position);
            Assert.Contains("int Lib::Box<int>.Value", hover);
            Assert.DoesNotContain("T ", hover);
            Assert.Equal(expected, model.Definition(source.Path, position));
        }
        Assert.Equal("int", TypeChecker.TypeName(snapshot.Checker!.GetType(snapshot.Nodes.Select(n => n.Node).OfType<MemberAccessExpression>().Last(n => n.Member == "Value" && n.Span.SourceId == source.Id))));
    }

    [Fact]
    public void GenericRefPropertyShowsValueTypeRatherThanLoweredPointer()
    {
        const string library = "namespace Lib{struct Box<T>{T storage;public Box(T value){storage=value;}public ref T Value{get=>storage;}}}";
        const string usage = "int main(){Lib::Box<int> box=new Lib::Box<int>(1);box.Value=42;return box.Value;}";
        var (model, _, source, _) = Analyze(usage, library);
        foreach (int position in Positions(usage, "Value"))
        {
            string hover = Hover(model, source, position);
            Assert.Contains("ref int Lib::Box<int>.Value", hover);
            Assert.DoesNotContain("int*", hover);
        }
    }

    [Fact]
    public void InheritedGenericPropertyUsesDeclaringSpecialization()
    {
        const string library = "namespace Lib{class Base{public int Value{get;set;}=42;}class Derived<T>:Lib::Base{}}";
        const string usage = "int main(){Lib::Derived<int> box=new Lib::Derived<int>();return box.Value;}";
        var (model, _, source, _) = Analyze(usage, library);
        Assert.Contains("int Base.Value", Hover(model, source, usage.IndexOf("Value", StringComparison.Ordinal)));
    }

    [Fact]
    public void BoundPropertyRetainsDeclaredAliasAndPropertyClassification()
    {
        const string library = "namespace Lib{alias Word=int;struct Box<T>{public Word Value{get;set;}public Box(Word value){Value=value;}}}";
        const string usage = "alias Word=long;int main(){Lib::Box<int> box=new Lib::Box<int>(42);box.Value=1;return box.Value;}";
        var (model, _, source, _) = Analyze(usage, library);
        foreach (int position in Positions(usage, "Value"))
        {
            string expected = Hover(model, source, position);
            Assert.Contains("Word Lib::Box<int>.Value", expected);
            Assert.DoesNotContain("long", expected);
            for (int i = 0; i < 3; i++) Assert.Equal(expected, Hover(model, source, position));
        }
        var data = JsonSerializer.SerializeToElement(model.SemanticTokens(source.Path), Protocol.JsonOptions)
            .GetProperty("data").EnumerateArray().Select(value => value.GetInt32()).ToArray();
        int column = 0;
        var properties = new List<int>();
        for (int i = 0; i < data.Length; i += 5)
        {
            column += data[i + 1]; // This fixture is deliberately one line.
            if (usage.Substring(column, data[i + 2]) == "Value")
            {
                Assert.Equal("property", EditorModel.TokenTypes[data[i + 3]]);
                properties.Add(column);
            }
        }
        Assert.Equal(Positions(usage, "Value"), properties);
    }

    [Fact]
    public void ReadonlyReferencePropertyKeepsItsRecordedValueProjection()
    {
        const string library = "namespace Lib{struct Box<T>{T storage;public Box(T value){storage=value;}public ref T Value{get=>storage;}}}";
        const string usage = "int main(){Lib::Box<int> box=new Lib::Box<int>(42);readonly(Lib::Box<int>)* view=&box;return view.Value;}";
        var (model, snapshot, source, _) = Analyze(usage, library);
        int position = usage.IndexOf("Value", StringComparison.Ordinal);
        var read = snapshot.Nodes.Select(n => n.Node).OfType<MemberAccessExpression>().Single(n => n.Member == "Value" && n.Span.SourceId == source.Id);
        Assert.Equal("readonly(int)", TypeChecker.TypeName(snapshot.Checker!.GetType(read)));
        string hover = Hover(model, source, position);
        Assert.Contains("ref readonly(int) Lib::Box<int>.Value", hover);
        Assert.DoesNotContain("int*", hover);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AliasToReadonlyTypeIsNotQualifiedAgain(bool reference)
    {
        string property = reference ? "public ref View Value{get=>storage;}" : "public View Value{get=>storage;}";
        string library = "namespace Lib{alias View=readonly(int*);struct Box<T>{View storage;public Box(View value){storage=value;}" + property + "}}";
        string usage = "int main(){int number=42;Lib::Box<int> box=new Lib::Box<int>(&number);" +
            (reference ? "readonly(Lib::Box<int>)* view=&box;return *view.Value;}" : "return *box.Value;}");
        var (model, _, source, _) = Analyze(usage, library);
        string hover = Hover(model, source, usage.IndexOf("Value", StringComparison.Ordinal));
        Assert.Contains("View Lib::Box<int>.Value", hover);
        Assert.DoesNotContain("readonly(View)", hover);
    }

    [Fact]
    public void SourceGenericBodyDoesNotAcquireTheLastSpecializationsPropertyType()
    {
        const string library = "namespace Lib{struct Box<T>{public T Value{get;set;}public Box(T value){Value=value;}public T Read()=>this.Value;}}";
        const string usage = "int main(){Lib::Box<int> first=new Lib::Box<int>(1);Lib::Box<long> second=new Lib::Box<long>(2l);first.Read();second.Read();return first.Value+(int)second.Value;}";
        var (model, _, source, declaration) = Analyze(usage, library);
        int templateRead = library.LastIndexOf("Value", StringComparison.Ordinal);
        string templateHover = Hover(model, declaration, templateRead);
        Assert.Contains("T Box.Value", templateHover);
        var positions = Positions(usage, "Value").ToArray();
        Assert.Contains("int Lib::Box<int>.Value", Hover(model, source, positions[0]));
        Assert.Contains("long Lib::Box<long>.Value", Hover(model, source, positions[1]));
        Assert.Equal(templateHover, Hover(model, declaration, templateRead));
    }

    [Fact]
    public void ReadonlyReferencePointerResultRetainsPointeeProtection()
    {
        const string library = "namespace Lib{struct Box<T>{T storage;public Box(T value){storage=value;}public ref T Value{get=>storage;}}}";
        const string usage = "int main(){int value=42;Lib::Box<int*> box=new Lib::Box<int*>(&value);readonly(Lib::Box<int*>)* view=&box;return *view.Value;}";
        var (model, _, source, _) = Analyze(usage, library);
        string hover = Hover(model, source, usage.IndexOf("Value", StringComparison.Ordinal));
        Assert.Contains("ref readonly(int*) Lib::Box<int*>", hover);
    }

    private static IEnumerable<int> Positions(string source, string text)
    {
        for (int offset = 0; (offset = source.IndexOf(text, offset, StringComparison.Ordinal)) >= 0; offset += text.Length) yield return offset;
    }
    private static (EditorModel Model, AnalysisSnapshot Snapshot, SourceFile Source, SourceFile Declaration) Analyze(string main, string library)
    {
        var source = new SourceFile(Path.GetFullPath("bound-property-main.gf"), main);
        var declaration = new SourceFile(Path.GetFullPath("bound-property-library.gf"), library);
        var snapshot = new AnalysisSnapshot([declaration, source], new() { NoStdLib=true });
        Assert.DoesNotContain(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        return (new(snapshot, [declaration, source]), snapshot, source, declaration);
    }
    private static string Hover(EditorModel model, SourceFile source, int offset)
        => JsonSerializer.SerializeToElement(model.Hover(source.Path, offset), Protocol.JsonOptions).GetProperty("contents").GetProperty("value").GetString()!;
}
