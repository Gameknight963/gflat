using gflat.ast;

namespace gflat.Tests;

public sealed class ImportedGenericFieldStorageTests
{
    private const string Library = """
        namespace Library {
            public struct Box<T> { public T Value; public Box(T value) { Value=value; } }
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ImportedGenericPointersBindInGlobalNamespaceAndStaticStorage(int optimization)
    {
        var sources = new[] {
            new SourceFile("library.gf", Library),
            new SourceFile("consumer.gf", """
                using Library;
                Box<int>*? direct=null;
                alias IntBox=Box<int>;
                IntBox*? aliased;
                namespace State { public Box<long>*? namespaced=null; }
                static class Shared { public static Box<short>*? value=null; }
                int main(){
                    Box<int> first=new(42);Box<long> second=new(99l);Box<short> third=new((short)7);
                    direct=&first;aliased=&first;State::namespaced=&second;Shared::value=&third;
                    if(direct!.Value!=42 || aliased!.Value!=42 || State::namespaced!.Value!=99l || Shared::value!.Value!=7)return 1;
                    direct=null;aliased=null;State::namespaced=null;Shared::value=null;
                    return 0;
                }
                """)
        };
        Assert.Equal(0, CompilerTestHelper.Run(sources, optimization).ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ImportedOwningGenericGlobalInitializesAndCleansUpInItsSourceContext(int optimization)
    {
        var result = CompilerTestHelper.Run(new[] {
            new SourceFile("library.gf", """
                namespace Library {
                    private extern int putchar(int value);
                    public struct Owner<T>{public T Value;public Owner(T value){Value=value;}~Owner(){putchar((int)Value);}}
                }
                """),
            new SourceFile("consumer.gf", """
                using Library;
                alias CharacterOwner=Owner<char>;
                Owner<char> first=new Owner<char>('A');
                CharacterOwner second=new CharacterOwner('B');
                int main(){if(first.Value!='A' || second.Value!='B')return 1;return 0;}
                """)
        }, optimization);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("BA", result.StandardOutput);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ActualStdVectorPointerGlobalCanBorrowAndObserveLocalStorage(int optimization)
    {
        string[] names = ["Collections/IEnumerable.gf", "Queries/Enumerable.gf", "IStringConvertable.gf",
            "IHashable.gf", "IEquatable.gf", "IComparable.gf", "Hashing.gf", "String.gf", "ByteSearch.gf", "Collections/Vector.gf", "Span.gf"];
        var sources = names.Select(name => new SourceFile("std/" + name,
                File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", name))))
            .Append(new SourceFile("consumer.gf", """
                using std; using std::Queries; using std::Collections;
                Vector<int>*? watched=null;
                int Sum(){Span<readonly(int)> values=watched!.AsImmutable();int total=0;for(nuint i=0;i<values.Length;i++)total+=values[i];return total;}
                int main(){Vector<int> values=new();values.Add(17);values.Add(25);watched=&values;
                    if(Sum()!=42)return 1;values.Add(3);if(Sum()!=45)return 2;watched=null;return 0;}
                """));
        Assert.Equal(0, CompilerTestHelper.Run(sources, optimization).ExitCode);
    }

    [Fact]
    public void CanonicalFieldTypesKeepDeclaredAliasMetadataForEditorPresentation()
    {
        var consumer = new SourceFile("consumer.gf", """
            using Library;alias IntBox=Box<int>;IntBox*? watched=null;int main(){return 0;}
            """);
        var snapshot = new AnalysisSnapshot([new SourceFile("library.gf", Library), consumer]);
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Severity == diagnostics.DiagnosticSeverity.Error);
        var field = snapshot.Nodes.Select(node => node.Node).OfType<FieldDeclaration>().Single(node => node.Name == "watched");
        Assert.Equal("IntBox*?", TypeChecker.TypeName(snapshot.DeclaredTypes[field]));
        Assert.Equal(TypeChecker.TypeName(snapshot.Checker!.GetType(field)), TypeChecker.TypeName(field.Type));
    }
}
