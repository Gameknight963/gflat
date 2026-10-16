namespace gflat.Tests;

public sealed class FieldLayoutContextTests
{
    private const string Library = """
        namespace Library {
            public alias Word = long;
            public struct Packet { public char prefix; public Word value; }
            public class Base { public Word value; }
        }
        namespace Other {
            public alias Word = byte;
            public class Derived : Library::Base { public Word tail; }
        }
        """;

    [Theory]
    [InlineData("")]
    [InlineData("alias Word = byte;")]
    public void ConstructedAndQueriedFieldsUseTheirDeclarationNamespaces(string callerAlias)
        => Assert.Equal(0, CompilerTestHelper.Run(Library + """
            int main() {
                CALLER_ALIAS
                Library::Packet packet = new Library::Packet();
                Other::Derived derived = new Other::Derived();
                if (sizeof(Library::Packet) != 16 || alignof(Library::Packet) != 8) return 1;
                if (sizeof(Other::Derived) != 24 || alignof(Other::Derived) != 8) return 2;
                return 0;
            }
            """.Replace("CALLER_ALIAS", callerAlias)).ExitCode);

    [Theory]
    [InlineData("sizeof(Library::Packet)", 16)]
    [InlineData("alignof(Library::Packet)", 8)]
    [InlineData("sizeof(Other::Derived)", 24)]
    [InlineData("alignof(Other::Derived)", 8)]
    public void LayoutQueriesResolveFieldsWithoutPriorConstruction(string expression, int expected)
        => Assert.Equal(expected, CompilerTestHelper.Run(Library +
            "int main(){ alias Word=byte; return (int)(" + expression + "); }").ExitCode);

    [Fact]
    public void FieldUsingDirectivesComeFromTheirOwnSourceFile()
        => Assert.Equal(0, CompilerTestHelper.Run(new[] {
            new SourceFile("types.gf", "namespace Types { public alias Word = long; }"),
            new SourceFile("packet.gf", "using Types; namespace Library { public struct Packet { public char prefix; public Word value; } }"),
            new SourceFile("main.gf", "int main(){ alias Word=byte; Library::Packet packet=new Library::Packet(); if(sizeof(Library::Packet)!=16 || alignof(Library::Packet)!=8) return 1; return 0; }")
        }).ExitCode);

    [Fact]
    public void GenericFieldsRetainTheirSpecializedDeclarationContext()
        => Assert.Equal(0, CompilerTestHelper.Run("""
            namespace Library {
                public alias Word = long;
                public struct Packet<T> { public Word prefix; public T value; }
            }
            int main() {
                alias Word = byte;
                Library::Packet<int> packet = new Library::Packet<int>();
                if (sizeof(Library::Packet<int>) != 16 || alignof(Library::Packet<int>) != 8) return 1;
                return 0;
            }
            """).ExitCode);
}
