using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class RuntimeTypeDescriptionTests
{
    private const string Common = """
        using Standard;

        bool TextEquals(readonly(char)* first, readonly(char)* second) nothrow {
            nuint index = 0;
            while (first[index] != '\0' && first[index] == second[index]) index++;
            return first[index] == second[index];
        }
        struct Node {
            public int Id;
            private byte Tag;
            public Node*? Next;
            public static int Ignored = 9;
            public const int Constant = 7;
            public int Property { get; set; }
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DeclaredFieldsExposeSourceNamesLayoutAndSharedCyclicTypes(int optimization)
        => Run(Common + """
            alias NodeAlias = Node;
            const immutable(TypeInfo)* saved = Describe<Node>();
            int main() nothrow {
                immutable(TypeInfo)* type = Describe<Node>();
                if (type != saved || type != Describe<NodeAlias>() || type.Kind != TypeKind::Struct) return 1;
                if (!TextEquals(type.Name, "Node") || !type.HasLayout || type.Size != (nuint)sizeof(Node) || type.Alignment != (nuint)alignof(Node)) return 2;
                if (type.FieldCount != 4 || type.Fields == null) return 3;
                if (!type.Fields![3].IsPrivate || type.Fields![3].Type != Describe<int>()) return 12;
                if (!TextEquals(type.Fields![0].Name, "Id") || type.Fields![0].Type != Describe<int>() || !type.Fields![0].IsPublic) return 4;
                if (!TextEquals(type.Fields![1].Name, "Tag") || type.Fields![1].Type != Describe<byte>() || !type.Fields![1].IsPrivate) return 5;
                if (!TextEquals(type.Fields![2].Name, "Next") || type.Fields![2].Type != Describe<Node*?>()) return 6;
                immutable(TypeInfo)* next = type.Fields![2].Type;
                if (!next.IsPointer || !next.IsNullable || next.ElementType != type) return 7;
                Node node = new();
                nuint address = (&node).reinterpret<nuint>();
                if (type.Fields![0].Offset != (&node.Id).reinterpret<nuint>() - address) return 8;
                if (type.Fields![2].Offset != (&node.Next).reinterpret<nuint>() - address) return 9;
                if (type.Fields![1].Offset != (nuint)sizeof(int)) return 10;
                if (type.Fields![2].Type.ElementType!.Fields![2].Type != next) return 11;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PointerLayersQualifiersArraysAndFunctionSignaturesRemainRecursive(int optimization)
        => Run(Common + """
            alias Callback = int(readonly(Node)*, int)*;
            alias Signature = int(readonly(Node)*, int);
            int main() nothrow {
                immutable(TypeInfo)* pointer = Describe<int**>();
                if (pointer.Kind != TypeKind::Pointer || pointer.ElementType != Describe<int*>() || pointer.ElementType!.ElementType != Describe<int>()) return 20;
                immutable(TypeInfo)* fixedPointer = Describe<readonly(int*)>();
                immutable(TypeInfo)* protectedInteger = Describe<readonly(int)*>();
                if (fixedPointer == protectedInteger || !fixedPointer.IsReadOnly || protectedInteger.IsReadOnly) return 21;
                if (fixedPointer.ElementType!.IsReadOnly || !protectedInteger.ElementType!.IsReadOnly) return 22;
                immutable(TypeInfo)* deep = Describe<immutable(Node*)>();
                if (!deep.IsImmutable || !deep.ElementType!.IsImmutable || deep.ElementType != Describe<immutable(Node)>()) return 23;
                immutable(TypeInfo)* array = Describe<readonly(int)[3]>();
                if (array.Kind != TypeKind::Array || !array.HasFixedLength || array.Length != 3 || array.ElementType != Describe<readonly(int)>()) return 24;
                immutable(TypeInfo)* callback = Describe<Callback>();
                if (callback.Kind != TypeKind::FunctionPointer || !callback.IsPointer || callback.ReturnType != Describe<int>() || callback.ParameterCount != 2) return 25;
                if (callback.ParameterTypes == null || callback.ParameterTypes![0] != Describe<readonly(Node)*>() || callback.ParameterTypes![1] != Describe<int>()) return 26;
                immutable(TypeInfo)* signature = Describe<Signature>();
                if (signature.Kind != TypeKind::FunctionSignature || signature.HasLayout || signature.ReturnType != callback.ReturnType) return 27;
                if (signature.ParameterCount != 2 || signature.ParameterTypes![0] != callback.ParameterTypes![0]) return 28;
                immutable(TypeInfo)* noArguments = Describe<void()*?>();
                if (!noArguments.IsNullable || noArguments.ParameterCount != 0 || noArguments.ParameterTypes != null || noArguments.ReturnType != Describe<void>()) return 29;
                if (Describe<void>().HasLayout || Describe<void>().Size != 0 || Describe<void>().Alignment != 0) return 30;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ClassBasesInterfacesEnumsAndDescriptorRecordsUseOrdinaryLibraryGraphs(int optimization)
        => Run(Common + """
            interface ITag {}
            class Parent { private int Hidden; public int Number; }
            class Child : Parent { public Node*? Link; }
            enum Code : short { First = 7 }
            struct Resource { public int Value; public ~Resource() {} }
            int main() nothrow {
                immutable(TypeInfo)* child = Describe<Child>();
                immutable(TypeInfo)* parent = Describe<Parent>();
                if (child.Kind != TypeKind::Class || child.BaseType != parent || child.FieldCount != 1) return 40;
                if (parent.FieldCount != 2 || !parent.Fields![0].IsPrivate || !TextEquals(parent.Fields![0].Name, "Hidden")) return 41;
                Child value = new();
                nuint address = (&value).reinterpret<nuint>();
                if (child.Fields![0].Offset != (&value.Link).reinterpret<nuint>() - address) return 42;
                if (parent.Fields![1].Offset != (&value.Number).reinterpret<nuint>() - address) return 43;
                immutable(TypeInfo)* contract = Describe<ITag>();
                if (contract.Kind != TypeKind::Interface || contract.HasLayout || contract.FieldCount != 0 || contract.Fields != null) return 44;
                if (Describe<ITag*>().Size != (nuint)sizeof(ITag*) || Describe<ITag*>().ElementType != contract) return 45;
                if (Describe<Code>().Kind != TypeKind::Enum || Describe<Code>().Size != (nuint)sizeof(Code) || !Describe<Resource>().HasDestructor) return 46;
                immutable(TypeInfo)* metadata = Describe<TypeInfo>();
                if (metadata.Kind != TypeKind::Struct || metadata.FieldCount == 0 || metadata != Describe<TypeInfo>()) return 47;
                bool found = false;
                for (nuint index = 0; index < metadata.FieldCount; index++) {
                    if (TextEquals(metadata.Fields![index].Name, "ElementType")) {
                        found = true;
                        if (metadata.Fields![index].Type != Describe<immutable(TypeInfo)*?>()) return 48;
                    }
                }
                if (!found) return 49;
                return 0;
            }
            """, optimization);

    [Fact]
    public void DescriptorViewsPreventMutationOfStaticData()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Sources(Common + """
            void Change() { Describe<Node>().Size = 99; }
            """), options: new() { NoStdLib = true }));
        Assert.Contains("readonly", error.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ManagedDescriptorsPreserveCollectorIdentityAndElementType(int optimization)
        => Run(Common + """
            static class First {
                public static void* Allocate(nuint bytes, nuint alignment) => ((nuint)1).reinterpret<void*>();
                public static void ReferenceAdded(void* slot) nothrow {}
                public static void ReferenceRemoved(void* slot) nothrow {}
            }
            static class Second {
                public static void* Allocate(nuint bytes, nuint alignment) => ((nuint)1).reinterpret<void*>();
                public static void ReferenceAdded(void* slot) nothrow {}
                public static void ReferenceRemoved(void* slot) nothrow {}
            }
            alias GC = First;
            int main() nothrow {
                immutable(TypeInfo)* first = Describe<Node^>();
                immutable(TypeInfo)* explicitFirst = Describe<managed<Node, First>>();
                immutable(TypeInfo)* second = Describe<managed<Node, Second>?>();
                if (first != explicitFirst || first == second || !first.IsManaged || first.IsPointer) return 60;
                if (first.Kind != TypeKind::ManagedPointer || first.ElementType != Describe<Node>()) return 61;
                if (first.CollectorType != Describe<First>() || second.CollectorType != Describe<Second>()) return 62;
                if (!second.IsNullable || second.ElementType != first.ElementType) return 63;
                if (first.Size != (nuint)sizeof(Node*) || first.Alignment != (nuint)alignof(Node*)) return 64;
                if (Describe<First>().HasLayout || Describe<First>().Fields != null) return 65;
                return 0;
            }
            """, optimization);

    [Fact]
    public void StaticReflectionNeedsNoRuntimeDescriptorSources()
    {
        Compiler.Check("const nuint Width = reflect(int).Size; int main() => (int)Width;", options: new() { NoStdLib = true });
    }

    private static IEnumerable<SourceFile> Sources(string code)
        => new[] { "TypeInfo.gf", "FieldInfo.gf", "type_descriptions.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard/core", name))))
            .Append(new SourceFile("runtime-type-descriptions.gf", code));

    private static void Run(string code, int optimization)
    {
        string ir = Compiler.Emit(Sources(code), options: new() { NoStdLib = true, InternalizeFunctions = true });
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }
}
