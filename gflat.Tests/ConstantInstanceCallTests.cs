using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ConstantInstanceCallTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ClassCallsRetainTheirReceiverAndMutations(int optimization)
        => AssertParity("""
            class Item
            {
                private int value;
                public const Item(int input) { value = input; }
                public const int Read() => value;
                public const int Next() => ++value;
                public const int Twice() => Read() + Read();
            }
            const int Fold(int input)
            {
                Item item = new Item(input);
                Item* pointer = &item;
                int result = item.Read() + pointer.Next();
                return result + item.Twice();
            }
            """, 10, 43, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ImplicitStructCallsKeepTheCurrentReceiver(int optimization)
        => AssertParity("""
            struct Item
            {
                private int value;
                public const Item(int input) { value = input; }
                public const int Read() => value;
                public const int Twice() => Read() + Read();
            }
            const int Fold(int input) { Item item = new Item(input); return item.Twice(); }
            """, 21, 42, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BasePointersDispatchToTheActualOverride(int optimization)
        => AssertParity("""
            class Base
            {
                public const Base() {}
                public virtual const int Read() => 1;
            }
            class Item : Base
            {
                private int value;
                public const Item(int input) { value = input; }
                public override const int Read() => value + 1;
            }
            const int Fold(int input) { Item item = new Item(input); Base* pointer = &item; return pointer.Read(); }
            """, 41, 42, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ConstructorDispatchMatchesTheActiveConstructionClass(int optimization)
        => AssertParity("""
            class Base
            {
                public int Seen;
                public const Base() { Seen = Read(); }
                public virtual const int Read() => 1;
            }
            class Item : Base
            {
                public const Item() {}
                public override const int Read() => 42;
            }
            const int Fold(int input) { Item item = new Item(); Base* pointer = &item; return item.Seen + pointer.Read() + input; }
            """, 0, 43, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ConstructionDoesNotChangeAnotherObjectsDispatch(int optimization)
        => AssertParity("""
            class Base
            {
                public int Seen;
                public const Base() { Seen = Read(); }
                public const Base(Base* other) { Seen = Read() + other.Read(); }
                public virtual const int Read() => 1;
            }
            class Other : Base
            {
                public const Other() {}
                public override const int Read() => 7;
            }
            class Item : Base
            {
                public const Item(Base* other) : base(other) {}
                public override const int Read() => 42;
            }
            const int Fold(int input)
            {
                Other other = new Other();
                Item item = new Item(&other);
                Base* pointer = &item;
                return item.Seen + pointer.Read() + input;
            }
            """, 0, 50, optimization);

    [Fact]
    public void AConstantBaseDeclarationCannotHideANonconstantOverride()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check("""
            class Base { public const Base() {} public virtual const int Read() => 1; }
            class Item : Base { public const Item() {} public override int Read() => 42; }
            const int Fold() { Item item = new Item(); Base* pointer = &item; return pointer.Read(); }
            const int value = Fold();
            """));
        Assert.Contains("not marked 'const'", error.Description);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AliasOwnerProofsDoNotReplaceTheDynamicOverride(int optimization)
        => AssertParity("""
            interface I {}
            struct Implementation : I {}
            constrained alias A : I = Implementation;
            class Base<T>
            {
                public T Value;
                public const Base() {}
                public virtual const int Read() => 1;
            }
            class Item : Base<A>
            {
                public const Item() {}
                public override const int Read() => 42;
            }
            const int Fold(int input) { Item item = new Item(); Base<A>* pointer = &item; return pointer.Read() + input; }
            """, 0, 42, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void StaticCallsPreserveTheCallersInstanceAfterReturning(int optimization)
        => AssertParity("""
            struct Item
            {
                private int value;
                public const Item(int input) { value = input; }
                private static const int Shared() => 1;
                public const int Read() => Shared() + value;
            }
            const int Fold(int input) { Item item = new Item(input); return item.Read(); }
            """, 42, 43, optimization);

    [Fact]
    public void UnsupportedInterfaceCallsHaveASpecificDiagnostic()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check("""
            interface I { const int Read(); }
            struct Item : I { public const int Read() => 42; }
            const int Fold() { Item item = new Item(); I* pointer = &item; return pointer.Read(); }
            const int value = Fold();
            """));
        Assert.Contains("Interface calls are not supported during constant evaluation", error.Description);
    }

    private static void AssertParity(string declarations, int input, int expected, int optimization)
        => Assert.Equal(0, CompilerTestHelper.Run(declarations + $$"""
            int Input() => {{input}};
            int main()
            {
                const int folded = Fold({{input}});
                int runtime = Fold(Input());
                if (folded != {{expected}} || runtime != {{expected}}) return 1;
                return 0;
            }
            """, optimization).ExitCode);
}
