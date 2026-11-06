using gflat.CompileExceptions;

namespace gflat.Tests;

public class TypeDescriptionShapeTests
{
    [Fact]
    public void BareFunctionSignaturesDescribeTheirShapeWithoutValueLayout()
    {
        Assert.Equal(42, Run("""
            alias Transform = int(immutable(char*), ref int);
            const bool NoLayout = !Standard::Describe<Transform>().HasLayout;
            int main() {
                immutable(Standard::TypeInfo)* info = Standard::Describe<Transform>();
                if (!NoLayout || info.IsPointer || info.Kind != Standard::TypeKind::FunctionSignature) return 1;
                if (info.Size != (nuint)0 || info.Alignment != (nuint)0) return 2;
                if (info.ReturnType!.Size != (nuint)4 || info.ParameterCount != (nuint)2) return 3;
                if (info.ParameterTypes![0].Kind != Standard::TypeKind::Pointer || info.ParameterTypes![1].Kind != Standard::TypeKind::Reference) return 4;
                if (!Standard::Describe<Transform*>().IsPointer || !Standard::Describe<Transform*>().HasLayout) return 5;
                return 42;
            }
            """).ExitCode);
    }

    [Theory]
    [InlineData("sizeof(int(int))")]
    [InlineData("alignof(int(int))")]
    public void BareFunctionSignaturesHaveNoSizeOrAlignment(string expression)
        => Assert.Throws<TypeCheckException>(() => Compiler.Check("int main() => " + expression + ";"));

    [Fact]
    public void FunctionSignaturesHaveRecursiveReturnAndParameterDescriptors()
    {
        Assert.Equal(0, Run("""
            int main() {
                immutable(Standard::TypeInfo)* signature = Standard::Describe<int(immutable(char*), ref readonly(int), int**)*>();
                if (signature.Kind != Standard::TypeKind::FunctionPointer || signature.ElementType != null) return 1;
                if (signature.ReturnType!.Size != (nuint)4 || signature.ParameterCount != (nuint)3) return 2;
                immutable(Standard::TypeInfo**) parameters = signature.ParameterTypes!;
                if (parameters[0].Kind != Standard::TypeKind::Pointer || !parameters[0].IsReadOnly || !parameters[0].ElementType!.IsReadOnly) return 3;
                if (parameters[1].Kind != Standard::TypeKind::Reference || parameters[1].IsReadOnly || !parameters[1].ElementType!.IsReadOnly) return 4;
                if (parameters[2].ElementType!.ElementType!.Size != (nuint)4) return 5;
                return 0;
            }
            """).ExitCode);
    }

    [Fact]
    public void FunctionParameterGraphWorksDuringConstantEvaluation()
    {
        Assert.Equal(42, Run("""
            const nuint Size = Standard::Describe<int(int**)*>().ParameterTypes![0].ElementType!.ElementType!.Size;
            const bool Borrow = Standard::Describe<int(ref readonly(int))*>().ParameterTypes![0].Kind == Standard::TypeKind::Reference;
            const immutable(Standard::TypeInfo)* Signature = Standard::Describe<int(int)*>();
            const bool SameArray = Signature.ParameterTypes == Signature.ParameterTypes;
            int main() { if (Size != (nuint)4 || !Borrow || !SameArray) return 1; return 42; }
            """).ExitCode);
    }

    [Fact]
    public void EmptyAndNonfunctionDescriptorsUseNullSignatureFields()
    {
        Assert.Equal(0, Run("""
            int main() {
                immutable(Standard::TypeInfo)* empty = Standard::Describe<void()*>();
                if (empty.ParameterCount != (nuint)0 || empty.ParameterTypes != null || empty.ReturnType == null) return 1;
                if (empty.ReturnType!.HasLayout || empty.ReturnType!.Size != (nuint)0) return 2;
                immutable(Standard::TypeInfo)* value = Standard::Describe<int*>();
                if (value.ReturnType != null || value.ParameterTypes != null || value.ParameterCount != (nuint)0) return 3;
                return 0;
            }
            """).ExitCode);
    }

    [Fact]
    public void NestedFunctionsArraysInterfacesAndManagedTypesKeepTheirOwnGraphs()
    {
        Assert.Equal(0, Run("""
            interface I { int Read(); }
            alias Inner = long(int)*;
            alias Outer = Inner(Inner, char[4], I*, int^?)*?;
            int main() {
                immutable(Standard::TypeInfo)* outer = Standard::Describe<Outer>();
                if (!outer.IsNullable || outer.ParameterCount != (nuint)4) return 1;
                if (outer.ReturnType!.Kind != Standard::TypeKind::FunctionPointer || outer.ReturnType!.ReturnType!.Size != (nuint)8) return 2;
                if (outer.ParameterTypes![0].ParameterTypes![0].Size != (nuint)4) return 3;
                if (!outer.ParameterTypes![1].HasFixedLength || outer.ParameterTypes![1].Length != (nuint)4) return 4;
                if (outer.ParameterTypes![2].ElementType!.Kind != Standard::TypeKind::Interface || outer.ParameterTypes![2].ElementType!.HasLayout) return 5;
                if (outer.ParameterTypes![3].Kind != Standard::TypeKind::ManagedPointer || !outer.ParameterTypes![3].IsNullable) return 6;
                return 0;
            }
            """).ExitCode);
    }

    [Fact]
    public void ParameterDescriptorStorageOutlivesTheProducingFunction()
    {
        Assert.Equal(42, Run("""
            immutable(Standard::TypeInfo**) Parameters() => Standard::Describe<int(int, long)*>().ParameterTypes!;
            int main() { immutable(Standard::TypeInfo**) types = Parameters(); if (types[0].Size != (nuint)4 || types[1].Size != (nuint)8) return 1; return 42; }
            """).ExitCode);
    }

    [Fact]
    public void ParameterDescriptorsCannotBeOverwritten()
    {
        var error = Assert.Throws<TypeCheckException>(() => Emit("int main() { immutable(Standard::TypeInfo)* info = Standard::Describe<int(int)*>(); info.ParameterTypes![0] = Standard::Describe<char>(); return 0; }"));
        Assert.Contains("readonly", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConstantParameterIndexReportsOutOfBounds()
    {
        var error = Assert.Throws<TypeCheckException>(() => Emit("const nuint Size = Standard::Describe<int(int)*>().ParameterTypes![1].Size; int main() => 0;"));
        Assert.True(error.Description.Contains("outside readable constant storage", StringComparison.OrdinalIgnoreCase), error.Description);
    }

    [Fact]
    public void TypeMetadataIsACompileTimeConstantAndWorksInGenericCode()
    {
        Assert.Equal(42, Run("""
            struct Player { public int score; public char tag; }
            const nuint width = Standard::Describe<Player>().Size;
            const nuint alignment = Standard::Describe<Player>().Alignment;
            const nuint Size<T>() => Standard::Describe<T>().Size;
            const nuint intWidth = Size<int>();
            int main() {
                immutable(Standard::TypeInfo)* info = Standard::Describe<Player>();
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
        var result = Run("""
            extern "C" int printf(immutable(char*) text, ...);
            namespace Game { struct Box<T> { public T value; } }
            alias IntBox = Game::Box<int>;
            int main() {
                printf("%s\n", Standard::Describe<IntBox>().Name);
                printf("%s\n", Standard::Describe<immutable(char*)>().Name);
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("Game::Box<int>\nimmutable(char*)\n", result.StandardOutput.Replace("\r\n", "\n"));
    }

    [Fact]
    public void ReferenceTraitsDescribeTheQueriedTypeAndNotItsPointee()
    {
        Assert.Equal(0, Run("""
            struct Resource { ~Resource() {} }
            int main() {
                if (!Standard::Describe<Resource>().HasDestructor) return 1;
                if (Standard::Describe<Resource*>().HasDestructor) return 2;
                if (!Standard::Describe<int*?>().IsPointer || !Standard::Describe<int*?>().IsNullable) return 3;
                if (!Standard::Describe<int^>().IsManaged || Standard::Describe<int^>().IsPointer) return 4;
                if (Standard::Describe<readonly(int)*>().IsReadOnly || !Standard::Describe<readonly(int)*>().ElementType!.IsReadOnly) return 5;
                return 0;
            }
            """).ExitCode);
    }

    [Theory]
    [InlineData("int main() { immutable(Standard::TypeInfo)* t = Standard::Describe<int>(); t.Size = 8; return 0; }")]
    [InlineData("int main() { immutable(Standard::TypeInfo)* t = Standard::Describe<int>(); t.Name = \"changed\"; return 0; }")]
    [InlineData("int main() { immutable(Standard::TypeInfo)* t = Standard::Describe<MissingType>(); return 0; }")]
    public void MetadataCannotBeMutatedAndUnknownTypesAreDiagnosed(string source)
        => Assert.Throws<TypeCheckException>(() => Check(source));

    [Theory]
    [InlineData("int**", false, false, false)]
    [InlineData("readonly(int)**", false, false, true)]
    [InlineData("immutable(int*)*", false, true, true)]
    [InlineData("immutable(int**)", true, true, true)]
    public void ReadonlyQualificationIsDescribedAtEachLayer(string type, bool outer, bool middle, bool leaf)
    {
        string Boolean(bool value) => value ? "true" : "false";
        Assert.Equal(0, Run($$"""
            int main() {
                immutable(Standard::TypeInfo)* type = Standard::Describe<{{type}}>();
                if (type.Kind != Standard::TypeKind::Pointer || type.IsReadOnly != {{Boolean(outer)}}) return 1;
                if (type.ElementType!.Kind != Standard::TypeKind::Pointer || type.ElementType!.IsReadOnly != {{Boolean(middle)}}) return 2;
                if (type.ElementType!.ElementType!.Kind != Standard::TypeKind::Primitive || type.ElementType!.ElementType!.IsReadOnly != {{Boolean(leaf)}}) return 3;
                if (type.ElementType!.ElementType!.ElementType != null) return 4;
                return 0;
            }
            """).ExitCode);
    }

    [Fact]
    public void RecursiveElementsAreCompileTimeConstants()
    {
        Assert.Equal(0, Run("""
            const nuint leafSize = Standard::Describe<int**>().ElementType!.ElementType!.Size;
            const bool leafReadonly = Standard::Describe<readonly(int)**>().ElementType!.ElementType!.IsReadOnly;
            int main() { if (leafSize != (nuint)4 || !leafReadonly) return 1; return 0; }
            """).ExitCode);
    }

    [Fact]
    public void ArraysAndManagedReferencesDescribeTheirElementTypes()
    {
        Assert.Equal(0, Run("""
            int main() {
                immutable(Standard::TypeInfo)* array = Standard::Describe<readonly(int)[4]>();
                if (array.Kind != Standard::TypeKind::Array || !array.HasFixedLength || array.Length != (nuint)4) return 1;
                if (array.Size != (nuint)16 || array.IsReadOnly || !array.ElementType!.IsReadOnly) return 2;
                immutable(Standard::TypeInfo)* managedType = Standard::Describe<int^?>();
                if (managedType.Kind != Standard::TypeKind::ManagedPointer || !managedType.IsNullable) return 3;
                if (managedType.ElementType!.Kind != Standard::TypeKind::Primitive || managedType.ElementType!.Size != (nuint)4) return 4;
                if (Standard::Describe<int*?>().ElementType!.IsNullable) return 5;
                if (Standard::Describe<int[]>().Alignment != (nuint)alignof(int*) || Standard::Describe<int[]>().Size != (nuint)sizeof(int*)) return 6;
                return 0;
            }
            """).ExitCode);
    }

    [Fact]
    public void InterfaceAndVoidElementsDoNotInventObjectLayouts()
    {
        Assert.Equal(0, Run("""
            interface I { int Read(); }
            int main() {
                immutable(Standard::TypeInfo)* pointer = Standard::Describe<I*>();
                if (!pointer.HasLayout || pointer.Size != (nuint)sizeof(I*)) return 1;
                if (pointer.ElementType!.Kind != Standard::TypeKind::Interface || pointer.ElementType!.HasLayout) return 2;
                if (pointer.ElementType!.Size != (nuint)0 || pointer.ElementType!.Alignment != (nuint)0) return 3;
                if (Standard::Describe<void*>().ElementType!.HasLayout || Standard::Describe<void>().HasLayout) return 4;
                if (Standard::Describe<int(int)*>().Kind != Standard::TypeKind::FunctionPointer || Standard::Describe<int(int)*>().ElementType != null) return 5;
                return 0;
            }
            """).ExitCode);
    }
    private static IEnumerable<SourceFile> Sources(string source)
        => TypeDescriptionTestSources.Include("""
            static class MetadataCollector
            {
                public static void* Allocate(nuint bytes, nuint alignment) => ((nuint)1).reinterpret<void*>();
                public static void ReferenceAdded(void* slot) nothrow { }
                public static void ReferenceRemoved(void* slot) nothrow { }
            }
            alias GC = MetadataCollector;
            """ + "\n" + source);

    private static ExecutionResult Run(string source) => CompilerTestHelper.Run(Sources(source));
    private static string Emit(string source) => Compiler.Emit(Sources(source));
    private static void Check(string source) => Compiler.Check(Sources(source));

}
