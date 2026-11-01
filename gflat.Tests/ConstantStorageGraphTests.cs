using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ConstantStorageGraphTests
{
    private const string Nodes = """
        struct Link {
            public int Value;
            public readonly(Link)*? Next;
            public const Link(int value, readonly(Link)*? next) nothrow { Value = value; Next = next; }
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SelfAndMutualReferencesUseImmutableStaticStorage(int optimization)
        => Run(Nodes + """
            const Link Self = new Link(7, &Self);
            const Link First = new Link(20, &Second);
            const Link Second = new Link(22, &First);
            const int Read(readonly(Link)* value) nothrow => value.Next!.Value;
            const int selfValue = Read(&Self);
            const int firstValue = Read(&First);
            const int secondValue = Read(&Second);
            int main() nothrow {
                readonly(Link)* self = &Self;
                readonly(Link)* first = &First;
                readonly(Link)* second = &Second;
                if (self.Next != self || first.Next != second || second.Next != first) return 1;
                if (selfValue != 7 || firstValue != 22 || secondValue != 20) return 2;
                if (first.Next!.Next != first || first.Next!.Value + second.Next!.Value != 42) return 3;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DirectReturnedAndNestedAddressesShareDeclarationIdentity(int optimization)
        => Run(Nodes + """
            namespace Data {
                public const Link Leaf = new Link(42, null);
                public const Link EqualLeaf = new Link(42, null);
                public const Link Left = new Link(1, &Leaf);
                public const Link Right = new Link(2, &Leaf);
            }
            const readonly(Link)* Address() nothrow => &Data::Leaf;
            const bool same = Address() == &Data::Leaf;
            const bool distinct = &Data::Leaf != &Data::EqualLeaf;
            const readonly(Link)* pointer = Address();
            const int throughSlot = (*(&pointer)).Value;
            int main() nothrow {
                if (!same || !distinct || throughSlot != 42) return 10;
                if (Address() != &Data::Leaf || pointer != &Data::Leaf) return 11;
                if (Data::Left.Next != Address() || Data::Right.Next != Address()) return 12;
                if (&Data::Leaf == &Data::EqualLeaf || &(*(&Data::Leaf)) != Address()) return 13;
                if ((*(&pointer)).Value != 42 || &pointer != &pointer) return 14;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void GenericConstHoldersCanReferenceTheirOwnPublishedStorage(int optimization)
        => Run(Nodes + """
            static class Holder<T> {
                public const Link Value = new Link(sizeof(T), &Holder<T>::Value);
            }
            const readonly(Link)* Describe<T>() nothrow => &Holder<T>::Value;
            alias Number = int;
            const bool same = Describe<int>() == Describe<Number>();
            const int size = Describe<long>().Next!.Value;
            int main() nothrow {
                if (!same || size != 8) return 20;
                if (Describe<int>() != &Holder<int>::Value || Describe<int>().Next != Describe<int>()) return 21;
                if (Describe<long>() != &Holder<long>::Value || Describe<long>().Next != Describe<long>()) return 22;
                if (Describe<int>() == Describe<long>() || Describe<int>().Value != 4) return 23;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void GenericHolderInitializerCanCallTheAddressFunctionBeingBound(int optimization)
        => Run(Nodes + """
            const readonly(Link)* Address<T>() nothrow => &Table<T>::Entry;
            static class Table<T> {
                public const Link Entry = new Link(sizeof(T), Address<T>());
            }
            const readonly(Link)* Saved = Address<int>();
            int main() nothrow {
                if (Saved != Address<int>() || Saved.Next != Saved || Saved.Value != 4) return 24;
                if (Address<long>().Next != Address<long>() || Address<long>().Value != 8) return 25;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ConstantArraysAndPointerSlotsRetainTheirDeclaredAddresses(int optimization)
        => Run(Nodes + """
            const Link Leaf = new Link(42, null);
            const Link[2] Values = [new Link(3, &Leaf), new Link(4, &Leaf)];
            const Link[2] Other = [new Link(3, &Leaf), new Link(4, &Leaf)];
            const readonly(Link)* Rows() nothrow => Values;
            const readonly(Link)* pointer = Rows();
            const bool same = Rows() == pointer;
            const int first = pointer[0].Value;
            const int second = (*(&pointer))[1].Value;
            const int nested = pointer[1].Next!.Value;
            int main() nothrow {
                if (!same || first != 3 || second != 4 || nested != 42) return 30;
                readonly(Link)* direct = Values;
                if (Rows() != direct || direct != &Values[0] || Rows() == &Other[0]) return 31;
                if ((*(&Values))[1].Next != &Leaf || &Values != &Values || &Values == &Other) return 32;
                if ((*(&pointer))[1].Value != 4 || pointer[0].Next != pointer[1].Next) return 33;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData("const int First = Second + 1; const int Second = First + 1;")]
    [InlineData("const int First = *(&First);")]
    public void ActualValueDependencyCyclesRemainErrors(string declarations)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(declarations + " int main() => First;"));
        Assert.Contains("Circular constant initializer", error.Description);
    }

    [Fact]
    public void ReadingAReinterpretedAddressIsNotFoldedAsTheOriginalScalarValue()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check("""
            const int Bits = 1065353216;
            const float value = *((&Bits).reinterpret<readonly(float)*>());
            int main() => 0;
            """));
        Assert.Contains("reinterpreted constant address", error.Description);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ReinterpretedAddressLoadsStillUseTheNativeRepresentationAtRuntime(int optimization)
        => Run("""
            const int Bits = 1065353216;
            float Read() nothrow => *((&Bits).reinterpret<readonly(float)*>());
            int main() nothrow { if (Read() != 1.0f) return 40; return 0; }
            """, optimization);

    private static void Run(string source, int optimization)
        => Assert.Equal(0, CompilerTestHelper.Run(source, optimization).ExitCode);
}
