using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class LexicalNestedTypeLookupTests
{
    [Theory]
    [InlineData("class", "alias Word=int;", "")]
    [InlineData("struct", "alias Word=int;", "")]
    [InlineData("class", "struct Word{public byte value;}", "")]
    [InlineData("struct", "struct Word{public byte value;}", "")]
    [InlineData("class", "", "alias Word=int;")]
    [InlineData("struct", "", "struct Word{public byte value;}")]
    [InlineData("class", "using Imported;namespace Imported{public struct Word{public byte value;}}", "")]
    [InlineData("struct", "using Imported;namespace Imported{public alias Word=int;}", "")]
    public void EnclosingNestedTypesWinOverNamespaceAndImportedBindings(string kind, string outside, string inside)
        => Assert.Equal(8, CompilerTestHelper.Run($$"""
            {{outside}}
            namespace Library {
                {{inside}}
                {{kind}} Owner {
                    public struct Word { public long value; }
                    public struct Box<T> {public static int Read()=>sizeof(Word);}
                }
            }
            int main()=>Library::Owner::Box<int>::Read();
            """).ExitCode);

    [Theory]
    [InlineData("alias Word=byte;return sizeof(Word);", 1)]
    [InlineData("enum Word{One}return sizeof(Word);", 4)]
    [InlineData("enum Word{One}{alias Word=byte;return sizeof(Word);}", 4)]
    public void ExistingLocalBindingsRetainPrecedence(string body, int size)
        => Assert.Equal(size, CompilerTestHelper.Run($$"""
            struct Word{public long value;}
            class Owner {
                public struct Word{public long value;}
                public static int Read(){ {{body}} }
            }
            int main()=>Owner::Read();
            """).ExitCode);

    [Fact]
    public void ExplicitQualificationsBypassNestedTypePreference()
        => Assert.Equal(4, CompilerTestHelper.Run("""
            struct Word{public byte value;}
            namespace Library{
                alias Word=int;
                class Owner{
                    public struct Word{public long value;}
                    public static int Read()=>sizeof(Library::Word);
                }
            }
            int main()=>Library::Owner::Read();
            """).ExitCode);

    [Fact]
    public void ALocalAliasDoesNotSilentlyDiscardSuppliedTypeArguments()
    {
        var error=Assert.Throws<TypeCheckException>(() => Compiler.Check("""
            class Owner{
                public struct Word{public long value;}
                public static int Read(){alias Selected=int;return sizeof(Selected<int>);}
            }
            int main()=>Owner::Read();
            """));
        Assert.Contains("Unknown generic type", error.Description);
    }

    [Fact]
    public void RegisteredNestedEnumKeepsItsUnderlyingLayout()
        => Assert.Equal(1, CompilerTestHelper.Run("""
            alias Word=long;
            struct Owner{
                public enum Word:byte{One}
                public struct Box<T>{public static int Read()=>sizeof(Word);}
            }
            int main()=>Owner::Box<int>::Read();
            """).ExitCode);

    [Fact]
    public void NoNestedMatchDoesNotPromoteAGlobalTypeOverANamespaceAlias()
        => Assert.Equal(4, CompilerTestHelper.Run("""
            struct Word{public byte value;}
            namespace Library{
                alias Word=int;
                class Owner{public static int Read()=>sizeof(Word);}
            }
            int main()=>Library::Owner::Read();
            """).ExitCode);
}
