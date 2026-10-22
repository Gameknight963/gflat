using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class GenericTypeBindingReentryTests
{
    [Theory]
    [InlineData("Box<int>", 4)]
    [InlineData("Box<int>*?", 8)]
    [InlineData("Box<int>[2]", 8)]
    [InlineData("Box<int>[Limits<int>::Count()]", 16)]
    [InlineData("BoxInt", 4)]
    [InlineData("immutable(BoxInt)", 4)]
    [InlineData("immutable(BoxInt)*?", 8)]
    [InlineData("Box<int>*?*?", 8)]
    public void SpecializedBodiesCanQueryTheRegisteredOwnerLayout(string fieldType, int size)
    {
        Assert.Equal(0, CompilerTestHelper.Run($$"""
            namespace Demo {
                alias BoxInt = Box<int>;
                struct Limits<T> { public const static int Count() { long number = 1; return (int)(number << 2); } }
                struct Owner { public {{fieldType}} value; }
                struct Box<T> { public T value; public static Owner Make() => new Owner(); }
            }
            int main() {
                Demo::Owner value = Demo::Box<int>::Make();
                if (sizeof(Demo::Owner) != {{size}}) return 1;
                return 0;
            }
            """).ExitCode);
    }

    [Fact]
    public void GenericClassBindingPublishesItsLayoutBeforeBodies()
        => Assert.Equal(0, CompilerTestHelper.Run("""
            namespace Demo {
                struct Owner { public Box<int> value; }
                class Box<T> { public T value; public static Owner Make() => new Owner(); }
            }
            int main() {
                Demo::Owner value = Demo::Box<int>::Make();
                if (sizeof(Demo::Owner) != sizeof(Demo::Box<int>)) return 1;
                return 0;
            }
            """).ExitCode);

    [Fact]
    public void GenericConstantArrayBoundsRemainTypedAndSynchronous()
        => Assert.Equal(0, CompilerTestHelper.Run("""
            struct Limits<T> {
                public const static int Count() { long number = 1; return (int)(number << 2); }
            }
            struct Values { public int[Limits<int>::Count()] data; }
            int main() { if (sizeof(Values) != 16) return 1; return 0; }
            """).ExitCode);

    private const string ContractDeclarations = """
        interface IFactory { static Owner Make(); }
        alias Selected : IFactory = Box<int>;
        struct Owner { public Selected value; }
        struct Box<T> : IFactory {
            public T value;
            public static Owner Make() => new Owner();
            public static int Extra() => 42;
        }
        """;

    [Fact]
    public void AliasPublicationKeepsItsContractViewSeparateFromImplementation()
        => Assert.Equal(0, CompilerTestHelper.Run(ContractDeclarations + """
            int main() {
                Owner owner = Selected::Make();
                if (Box<int>::Extra() != 42 || sizeof(Owner) != 4) return 1;
                return 0;
            }
            """).ExitCode);

    [Fact]
    public void ReentrantAliasBindingDoesNotExposeImplementationOnlyMembers()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(ContractDeclarations +
            " int main() => Selected::Extra();"));
        Assert.Contains("not exposed by alias", error.Description);
    }

    [Fact]
    public void ReadonlyAliasPointerKeepsItsPointeeReadonly()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check("""
            alias BoxInt = Box<int>;
            struct Owner { public immutable(BoxInt)*? value; }
            struct Box<T> { public T value; public static Owner Make() => new Owner(); }
            void Mutate(Owner* owner) { owner.value!.value = 1; }
            int main() => 0;
            """));
        Assert.Contains("readonly", error.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("alias A = B; alias B = A;")]
    [InlineData("alias A = A*;")]
    [InlineData("alias A = B*; alias B = A*;")]
    public void TrueAliasCyclesRemainSourceDiagnostics(string declaration)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(declaration + " int main()=>0;"));
        Assert.Contains("Cyclic type alias", error.Description);
    }
}
