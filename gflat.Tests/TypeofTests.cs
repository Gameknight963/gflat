using gflat.CompileExceptions;

namespace gflat.Tests;

public class TypeofTests
{
    [Fact]
    public void TypeMetadataIsACompileTimeConstantAndWorksInGenericCode()
    {
        Assert.Equal(42, CompilerTestHelper.Run("""
            struct Player { public int score; public char tag; }
            const nuint width = typeof(Player).Size;
            const nuint alignment = typeof(Player).Alignment;
            const nuint Size<T>() => typeof(T).Size;
            const nuint intWidth = Size<int>();
            int main() {
                std::TypeInfo info = typeof(Player);
                if (info.Size != (nuint)sizeof(Player)) return 1;
                if (width != (nuint)sizeof(Player) || alignment != (nuint)alignof(Player)) return 2;
                if (intWidth != (nuint)4) return 3;
                return 42;
            }
            """).ExitCode);
    }

    [Fact]
    public void NamesResolveAliasesAndGenericNamespacesWithoutMangling()
    {
        var result = CompilerTestHelper.Run("""
            extern int printf(readonly(char*) text, ...);
            namespace Game { struct Box<T> { public T value; } }
            alias IntBox = Game::Box<int>;
            int main() {
                printf("%s\n", typeof(IntBox).Name);
                printf("%s\n", typeof(readonly(char*)).Name);
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("Game::Box<int>\nreadonly(char*)\n", result.StandardOutput.Replace("\r\n", "\n"));
    }

    [Fact]
    public void ReferenceTraitsDescribeTheQueriedTypeAndNotItsPointee()
    {
        Assert.Equal(0, CompilerTestHelper.Run("""
            struct Resource { ~Resource() {} }
            int main() {
                if (!typeof(Resource).HasDestructor) return 1;
                if (typeof(Resource*).HasDestructor) return 2;
                if (!typeof(int*?).IsPointer || !typeof(int*?).IsNullable) return 3;
                if (!typeof(int^).IsManaged || typeof(int^).IsPointer) return 4;
                if (typeof(readonly(int)*).IsReadOnly || !typeof(readonly(int)*).ElementType!.IsReadOnly) return 5;
                return 0;
            }
            """).ExitCode);
    }

    [Theory]
    [InlineData("int main() { std::TypeInfo t = typeof(int); t.Size = 8; return 0; }")]
    [InlineData("int main() { std::TypeInfo t = typeof(int); t.Name = \"changed\"; return 0; }")]
    [InlineData("int main() { std::TypeInfo t = typeof(MissingType); return 0; }")]
    public void MetadataCannotBeMutatedAndUnknownTypesAreDiagnosed(string source)
        => Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(source));

    [Theory]
    [InlineData("int**", false, false, false)]
    [InlineData("readonly(int)**", false, false, true)]
    [InlineData("readonly(int*)*", false, true, true)]
    [InlineData("readonly(int**)", true, true, true)]
    public void ReadonlyQualificationIsDescribedAtEachLayer(string type, bool outer, bool middle, bool leaf)
    {
        string Boolean(bool value) => value ? "true" : "false";
        Assert.Equal(0, CompilerTestHelper.Run($$"""
            int main() {
                std::TypeInfo type = typeof({{type}});
                if (type.Kind != std::TypeKind::Pointer || type.IsReadOnly != {{Boolean(outer)}}) return 1;
                if (type.ElementType!.Kind != std::TypeKind::Pointer || type.ElementType!.IsReadOnly != {{Boolean(middle)}}) return 2;
                if (type.ElementType!.ElementType!.Kind != std::TypeKind::Primitive || type.ElementType!.ElementType!.IsReadOnly != {{Boolean(leaf)}}) return 3;
                if (type.ElementType!.ElementType!.ElementType != null) return 4;
                return 0;
            }
            """).ExitCode);
    }

    [Fact]
    public void RecursiveElementsAreCompileTimeConstants()
    {
        Assert.Equal(0, CompilerTestHelper.Run("""
            const nuint leafSize = typeof(int**).ElementType!.ElementType!.Size;
            const bool leafReadonly = typeof(readonly(int)**).ElementType!.ElementType!.IsReadOnly;
            int main() { if (leafSize != (nuint)4 || !leafReadonly) return 1; return 0; }
            """).ExitCode);
    }

    [Fact]
    public void ArraysAndManagedReferencesDescribeTheirElementTypes()
    {
        Assert.Equal(0, CompilerTestHelper.Run("""
            int main() {
                std::TypeInfo array = typeof(readonly(int)[4]);
                if (array.Kind != std::TypeKind::Array || !array.HasFixedLength || array.Length != (nuint)4) return 1;
                if (array.Size != (nuint)16 || array.IsReadOnly || !array.ElementType!.IsReadOnly) return 2;
                std::TypeInfo managed = typeof(int^?);
                if (managed.Kind != std::TypeKind::ManagedPointer || !managed.IsNullable) return 3;
                if (managed.ElementType!.Kind != std::TypeKind::Primitive || managed.ElementType!.Size != (nuint)4) return 4;
                if (typeof(int*?).ElementType!.IsNullable) return 5;
                if (typeof(int[]).Alignment != (nuint)alignof(int*) || typeof(int[]).Size != (nuint)sizeof(int*)) return 6;
                return 0;
            }
            """).ExitCode);
    }

    [Fact]
    public void InterfaceAndVoidElementsDoNotInventObjectLayouts()
    {
        Assert.Equal(0, CompilerTestHelper.Run("""
            interface I { int Read(); }
            int main() {
                std::TypeInfo pointer = typeof(I*);
                if (!pointer.HasLayout || pointer.Size != (nuint)sizeof(I*)) return 1;
                if (pointer.ElementType!.Kind != std::TypeKind::Interface || pointer.ElementType!.HasLayout) return 2;
                if (pointer.ElementType!.Size != (nuint)0 || pointer.ElementType!.Alignment != (nuint)0) return 3;
                if (typeof(void*).ElementType!.HasLayout || typeof(void).HasLayout) return 4;
                if (typeof(int(int)*).Kind != std::TypeKind::FunctionPointer || typeof(int(int)*).ElementType != null) return 5;
                return 0;
            }
            """).ExitCode);
    }
}
