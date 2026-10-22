using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class GenericNestedIdentityTests
{
    [Theory]
    [InlineData("struct", "", "Item")]
    [InlineData("class", "", "Item")]
    [InlineData("struct", "Demo", "Item")]
    [InlineData("class", "Demo", "Middle::Item")]
    public void RegisteredNestedIdentityCanReenterItsParentBody(string kind, string ns, string member)
    {
        string prefix = ns.Length == 0 ? "" : ns + "::";
        string item = "public struct Item { public T number; }";
        if (member.Contains("::")) item = "public struct Middle {" + item + "}";
        string declarations = $$"""
            struct Owner { public {{prefix}}Family<int>::{{member}} value; }
            {{kind}} Family<T> {
                {{item}}
                public static Owner Make() => new Owner();
            }
            """;
        if (ns.Length != 0) declarations = "namespace " + ns + " {" + declarations + "}";
        Assert.Equal(42, CompilerTestHelper.Run(declarations + $$"""
            int main() {
                {{prefix}}Owner value = {{prefix}}Family<int>::Make();
                value.value.number = 42;
                return value.value.number;
            }
            """).ExitCode);
    }

    [Theory]
    [InlineData("immutable(Family<int>::Item)")]
    [InlineData("Family<int>::Item*?")]
    [InlineData("Family<int>::Item[2]")]
    public void NestedPublicationComposesWithExistingTypeWrappers(string fieldType)
        => Compiler.Emit($$"""
            struct Owner { public {{fieldType}} value; }
            struct Family<T> {
                public struct Item { public T number; }
                public static Owner* Make() => new* Owner();
            }
            int main() => sizeof(Owner);
            """);

    [Fact]
    public void FailureAfterNestedPublicationIsStillReported()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check("""
            struct Owner { public Family<int>::Item value; }
            struct Family<T> {
                public struct Item { public T number; }
                public static Owner Make() { Missing(); return new Owner(); }
            }
            int main() => 0;
            """));
        Assert.Contains("Missing", error.Description);
    }

    [Fact]
    public void ImportedParentAliasKeepsItsDeclarationContext()
        => Assert.Equal(42, CompilerTestHelper.Run(new[] {
            new SourceFile("family.gf", "namespace Library {public struct Family<T>{public struct Item{public T number;}public static App::Owner Make()=>new App::Owner();}}"),
            new SourceFile("owner.gf", "using Library; namespace App {alias Selected=Family<int>;public struct Owner{public Selected::Item value;}}"),
            new SourceFile("main.gf", "int main(){alias Family=byte;App::Owner owner=Library::Family<int>::Make();owner.value.number=42;return owner.value.number;}")
        }).ExitCode);

    [Fact]
    public void ContractAliasDoesNotExposeTheImplementationsNestedTypes()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check("""
            interface IFactory { static int Read(); }
            alias Selected : IFactory = Family<int>;
            struct Owner { public Selected::Item value; }
            struct Family<T> : IFactory {
                public struct Item { public T number; }
                public static int Read() => 42;
                public static Owner Make() => new Owner();
            }
            int main() => 0;
            """));
        Assert.Contains("not exposed by alias", error.Description);
    }

    [Fact]
    public void ReadonlyNestedIdentityStaysReadonlyAfterPublication()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check("""
            struct Owner { public immutable(Family<int>::Item)*? value; }
            struct Family<T> {
                public struct Item { public T number; }
                public static Owner Make() => new Owner();
            }
            void Mutate(Owner* owner) { owner.value!.number = 1; }
            int main() => 0;
            """));
        Assert.Contains("readonly", error.Description, StringComparison.OrdinalIgnoreCase);
    }
}
