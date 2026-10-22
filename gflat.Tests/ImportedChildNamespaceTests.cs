using gflat.CompileExceptions;
using gflat.ast;

namespace gflat.Tests;

public sealed class ImportedChildNamespaceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ImportedChildSupportsTypesAndNestedCalls(int optimization)
    {
        SourceFile[] sources=[
            new("library.gf","namespace Library::Tools {struct Item{public int Value;public Item(int value){Value=value;}}int Read()=>21;int Add(int a,int b)=>a+b;}"),
            new("main.gf","using Library;int main(){Tools::Item item=new(Tools::Add(Tools::Read(),Tools::Read()));return item.Value;}")];
        Assert.Equal(42,CompilerTestHelper.Run(sources,optimization).ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void LexicalAndGlobalNamespacesKeepPrecedence(int optimization)
    {
        Assert.Equal(42,CompilerTestHelper.Run("""
            using Library;
            namespace Library::Tools {int Read()=>99;}
            namespace Tools {int Read()=>20;}
            namespace Local {namespace Tools {int Read()=>22;}int Read()=>Tools::Read();}
            int main()=>Tools::Read()+Local::Read();
            """,optimization).ExitCode);
    }

    [Fact]
    public void TwoImportedChildrenAreAmbiguous()
    {
        var error=Assert.Throws<TypeCheckException>(()=>Compiler.Check("""
            using First;using Second;
            namespace First::Tools{int Read()=>1;}
            namespace Second::Tools{int Read()=>2;}
            int main()=>Tools::Read();
            """));
        Assert.Contains("Namespace 'Tools' is ambiguous",error.Message);
        Assert.Equal("<input>",error.Span.Source?.Path);
        Assert.Equal(4,error.Span.Line);
        Assert.True(error.Span.Column>0);
    }

    [Fact]
    public void ImportDoesNotLeakToAnotherSourceFile()
    {
        SourceFile[] sources=[
            new("library.gf","namespace Library::Tools{int Read()=>42;}"),
            new("importer.gf","using Library;int Good()=>Tools::Read();"),
            new("main.gf","int main()=>Tools::Read();")];
        var error=Assert.Throws<TypeCheckException>(()=>Compiler.Check(sources));
        Assert.Equal("main.gf",error.Span.Source?.Path);
        Assert.Contains("Unknown namespace",error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DuplicateImportsAndLongerChildChainsResolve(int optimization)
        => Assert.Equal(42,CompilerTestHelper.Run("""
            using Library;using Library;
            namespace Library::Tools::Nested{int Read()=>42;}
            int main()=>Tools::Nested::Read();
            """,optimization).ExitCode);

    [Fact]
    public void ImportNamesDoNotResolveThroughOtherImports()
    {
        var error=Assert.Throws<TypeCheckException>(()=>Compiler.Check("""
            using Library;using Tools;
            namespace Library::Tools::Nested{int Read()=>42;}
            int main()=>Nested::Read();
            """));
        Assert.Contains("Unknown namespace",error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void GenericExtensionsUseDeclarationImportsForReceiverAndLaterParameters(int optimization)
    {
        SourceFile[] sources=[
            new("types.gf","namespace Library::Tools{struct Box<T>{public T Value;public Box(T value){Value=value;}}}namespace Other::Tools{struct Box<T>{public T Value;}}"),
            new("extensions.gf","using Library;namespace Helpers {T Add<T>(this Tools::Box<T> self,Tools::Box<T> other)=>self.Value+other.Value;}"),
            new("main.gf","using Other;using Helpers;int main(){Library::Tools::Box<int> first=new(20);Library::Tools::Box<int> second=new(22);return first.Add(second);}")];
        Assert.Equal(42,CompilerTestHelper.Run(sources,optimization).ExitCode);
    }

    [Fact]
    public void CallerImportsCannotSupplyMissingExtensionDeclarationNamespace()
    {
        SourceFile[] sources=[
            new("types.gf","namespace Library::Tools{struct Box<T>{public T Value;}}"),
            new("extensions.gf","namespace Helpers {T Read<T>(this Tools::Box<T> self)=>self.Value;}"),
            new("main.gf","using Library;using Helpers;int main(){Library::Tools::Box<int> first=new();return first.Read();}")];
        var error=Assert.Throws<TypeCheckException>(()=>Compiler.Check(sources));
        Assert.Contains("no field or method 'Read'",error.Message);
    }

    [Fact]
    public void AmbiguousExtensionSignatureDoesNotThrowFromPreview()
    {
        SourceFile[] sources=[
            new("types.gf","namespace First::Tools{struct Box<T>{public T Value;}}namespace Second::Tools{struct Box<T>{public T Value;}}"),
            new("extensions.gf","using First;using Second;namespace Helpers {T Read<T>(this Tools::Box<T> self)=>self.Value;}"),
            new("main.gf","int main(){First::Tools::Box<int> value=new();return 0;}")];
        var (ast,checker)=Compiler.Check(sources);
        int members=ast.Members.Count;
        string name=Assert.Single(checker.Symbols.Structs.Keys,name=>name.Contains("Box$int"));
        Assert.Empty(checker.GetVisibleExtensions("",["Helpers"],new NamedTypeExpression(name,null,1),true,false,"Read"));
        Assert.Equal(members,ast.Members.Count);
    }
}
