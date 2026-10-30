using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ExtensionArgumentInferenceTests
{
    private static SourceFile[] Sources(string secondType) =>
    [
        new("types.gf", """
            namespace Containers { struct Box<T> { public T Value; } }
            namespace Unrelated { struct Box<T> { public T Value; } }
            """),
        new("extension.gf", """
            using Containers;
            namespace Algorithms {
                int Measure<A,B>(Box<A> first, Box<B> second) => sizeof(A)*10+sizeof(B);
                extension<A> Box<A> { public int Measure<B>(Box<B> second) => sizeof(A)*10+sizeof(B); }
            }
            """),
        new("main.gf", $$"""
            using Algorithms;
            using Unrelated;
            int main() {
                Containers::Box<int> first=new();
                {{secondType}} second=new();
                return first.Measure(second);
            }
            """)
    ];

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void LaterGenericArgumentsResolveInTheExtensionDeclarationScope(int optimization)
        => Assert.Equal(41, CompilerTestHelper.Run(Sources("Containers::Box<byte>"), optimization).ExitCode);

    [Fact]
    public void SameSpellingInAnotherNamespaceDoesNotInferACompatibleGenericArgument()
    {
        var error=Assert.Throws<TypeCheckException>(()=>Compiler.Emit(Sources("Unrelated::Box<byte>")));
        Assert.Contains("Could not infer type parameter 'B'",error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void LaterArgumentAliasesUseTheirDeclarationScopeWithoutCapturingCallerAliases(int optimization)
    {
        SourceFile[] sources =
        [
            new("types.gf", """
                namespace Models {
                    struct Tag { public int Value; }
                    struct Pair<A,B> { public A First; public B Second; }
                }
                namespace Other { struct Tag { public long Value; } }
                """),
            new("extension.gf", """
                using Models;
                namespace Helpers {
                    alias Selected=Models::Tag;
                    int Read<T>(int receiver, Pair<Selected,T> value) => sizeof(T);
                    extension int { public int Read<T>(Pair<Selected,T> value) => sizeof(T); }
                }
                """),
            new("main.gf", """
                using Helpers;
                alias Selected=Other::Tag;
                int main() {
                    Models::Pair<Models::Tag,Selected> pair=new();
                    int receiver=0;
                    return receiver.Read(pair);
                }
                """)
        ];
        Assert.Equal(8,CompilerTestHelper.Run(sources,optimization).ExitCode);
    }
}
