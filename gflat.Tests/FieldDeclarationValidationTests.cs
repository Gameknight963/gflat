using gflat;
using gflat.diagnostics;
using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class FieldDeclarationValidationTests
{
    [Theory]
    [InlineData("class", "virtual")]
    [InlineData("class", "override")]
    [InlineData("class", "abstract")]
    [InlineData("struct", "virtual")]
    [InlineData("struct", "override")]
    [InlineData("struct", "abstract")]
    [InlineData("static class", "virtual")]
    [InlineData("class Box<T>", "virtual")]
    public void FieldsRejectDiscardedMethodModifiers(string owner,string modifier)
    {
        string header=owner.StartsWith("class Box") ? owner : owner+" Box";
        string text=$$$"""
            {{{header}}} {
                public {{{modifier}}} int value;
            }
            """;
        var source=new SourceFile("field-modifier.gf",text);
        var diagnostic=Assert.Single(Compiler.Analyze([source],new(){NoStdLib=true}));
        Assert.Equal("Fields cannot be virtual, override, or abstract",diagnostic.Message);
        Assert.Equal(modifier,text.Substring(diagnostic.Span.Start,diagnostic.Span.Length));
        Assert.Equal(source.Path,diagnostic.Span.Source?.Path);
        var error=Assert.Throws<TypeCheckException>(()=>Compiler.Check(source,options:new(){NoStdLib=true}));
        Assert.Equal(diagnostic.Message,error.Description);
        Assert.Equal(diagnostic.Span,error.Span);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("namespace Library {", "}")]
    [InlineData("class Box {", "}")]
    [InlineData("struct Box {", "}")]
    [InlineData("static class Box {", "}")]
    [InlineData("struct Box<U> {", "}")]
    public void FieldsRejectGenericParameterLists(string prefix,string suffix)
    {
        string text=prefix+" public int value<T>; "+suffix;
        var source=new SourceFile("generic-field.gf",text);
        var diagnostic=Assert.Single(Compiler.Analyze([source],new(){NoStdLib=true}));
        Assert.Equal("Fields cannot declare generic parameters",diagnostic.Message);
        Assert.Equal("<T>",text.Substring(diagnostic.Span.Start,diagnostic.Span.Length));
        Assert.Throws<TypeCheckException>(()=>Compiler.Check(source,options:new(){NoStdLib=true}));
    }

    [Fact]
    public void EvenAnEmptyGenericFieldListIsNotAnOrdinaryField()
    {
        const string text="struct Box {public int value<>;}";
        var diagnostic=Assert.Single(Compiler.Analyze([new SourceFile("empty-list.gf",text)],new(){NoStdLib=true}));
        Assert.Equal("Fields cannot declare generic parameters",diagnostic.Message);
        Assert.Equal("<>",text.Substring(diagnostic.Span.Start,diagnostic.Span.Length));
    }

    [Fact]
    public void NamespaceFieldsRejectDiscardedAbstractModifier()
    {
        const string text="namespace Library {public abstract int value;}";
        var diagnostic=Assert.Single(Compiler.Analyze([new SourceFile("abstract-field.gf",text)],new(){NoStdLib=true}));
        Assert.Equal("Fields cannot be virtual, override, or abstract",diagnostic.Message);
        Assert.Equal("abstract",text.Substring(diagnostic.Span.Start,diagnostic.Span.Length));
    }

    [Fact]
    public void OrdinaryFieldsAndMethodGenericParametersKeepTheirMeaning()
    {
        const string source="""
            const int Global=1;
            static int storage;
            struct State {public static int count;public static const int Amount=2;}
            struct Box<T> {
                public T value;
                public readonly(int) fixedValue;
                public static int count;
                public const int Amount=2;
                public Box(T item){value=item;fixedValue=3;}
                public static U Identity<U>(U item)=>item;
                public int Property{get;set;}
            }
            int Identity<T>(T value)=>(int)value;
            int main(){
                Box<int> box=new Box<int>(4);
                box.Property=5;
                State::count=6;
                storage=7;
                return Global+State::Amount+box.fixedValue+box.value+box.Property+State::count+storage+Box<int>::Identity<int>(8)+Identity<int>(6);
            }
            """;
        Assert.DoesNotContain(Compiler.Analyze([new SourceFile("valid-fields.gf",source)],new(){NoStdLib=true}),d=>d.Severity==DiagnosticSeverity.Error);
        Assert.Equal(42,CompilerTestHelper.Run(source).ExitCode);
    }
}
