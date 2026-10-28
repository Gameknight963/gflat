using gflat.CompileExceptions;
using gflat.ast;

namespace gflat.Tests;

public sealed class NamespaceTypeVisibilityTests
{
    [Theory]
    [InlineData("class Container{public struct Hidden{}}", "Hidden")]
    [InlineData("struct Container{public struct Hidden{}}", "Hidden")]
    [InlineData("class Container{public class Hidden{}}", "Hidden")]
    [InlineData("struct Container{public class Hidden{}}", "Hidden")]
    [InlineData("struct Container{public enum Hidden{One}}", "Hidden")]
    [InlineData("class Container{public struct Hidden<T>{}}", "Hidden<int>")]
    [InlineData("struct Container{public class Hidden<T>{}}", "Hidden<int>")]
    public void NestedTypesAreNotNamespaceMembers(string declaration, string type)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(
            "namespace Library{" + declaration + "}void Use(Library::" + type + " value){}int main()=>0;"));
        Assert.Contains("Unknown", error.Description);
    }

    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    public void SpecializationDoesNotExposeItsNestedTypesInTheNamespace(string kind)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check($$"""
            namespace Library{ {{kind}} Container<T>{public struct Hidden{public T value;} } }
            Library::Container<int> First()=>new Library::Container<int>();
            void Invalid(Library::Hidden value){}
            int main()=>0;
            """));
        Assert.Contains("Unknown", error.Description);
    }

    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    public void NamespaceAndNestedTypesKeepSeparateLayouts(string kind)
        => Assert.Equal(0, CompilerTestHelper.Run($$"""
            namespace Library {
                struct Word{public int value;}
                {{kind}} Owner{public struct Word{public long value;} }
            }
            int main(){
                if(sizeof(Library::Word)!=4)return 1;
                if(sizeof(Library::Owner::Word)!=8)return 2;
                return 0;
            }
            """).ExitCode);

    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    public void GenericSiblingTypesRemainAvailableInTheirLexicalOwner(string kind)
        => Assert.Equal(4, CompilerTestHelper.Run($$"""
            {{kind}} Owner {
                public struct Item<U>{public U value;}
                public struct Box<T>{public static int Size()=>sizeof(Item<T>);}
            }
            int main()=>Owner::Box<int>::Size();
            """).ExitCode);

    [Fact]
    public void EarlySpecializationKeepsItsRegistrationProvenanceAcrossCollection()
    {
        // Weak signature selection resolves the nested parameter type
        // before the second symbol collection pass rebuilds the symbol table.
        var result = Compiler.Check("""
            struct Box<T>{public struct Item{public T value;}}
            weak int Read(Box<int>::Item value)=>1;
            int main()=>0;
            """, options: new() { NoStdLib = true });
        var definition = result.Ast.Members.OfType<StructDeclaration>().Single(s => s.IsGeneric);
        var generated = result.Ast.Members.OfType<StructDeclaration>().Single(s => !s.IsGeneric);
        var symbols = result.Checker.Symbols;
        var scope = symbols.GlobalScope;
        Assert.Contains(generated.Name, symbols.Structs.Keys);
        Assert.Same(definition, symbols.GenericStructs[definition.Name]);
        Assert.Equal(definition.Name, scope.TypeNames[definition.SourceName]);
        Assert.DoesNotContain(generated.Name, scope.TypeNames.Keys);
        Assert.DoesNotContain(generated.Name, scope.TypeNames.Values);
        Assert.DoesNotContain(generated.Name, scope.Structs.Keys);
        Assert.DoesNotContain(scope.Structs.Values, info => info.Name == generated.Name);
        Assert.DoesNotContain("Item", scope.TypeNames.Keys);
        Assert.DoesNotContain("Item", scope.Structs.Keys);
    }
}
