using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ReflectionFieldAccessTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FieldSplicesSupportMutationBorrowingAndAddresses(int optimization)
    {
        Run("""
            struct Item { public int value; }
            void Add(ref int value) { value += 3; }
            int main()
            {
                Item item = new();
                const reflection field = reflect(Item::value);
                item.[:field:] = 38;
                Add(ref item.[:field:]);
                int* address = &item.[:field:];
                (*address)++;
                return item.value - 42;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AReflectedBaseFieldCanBeUsedOnADerivedValue(int optimization)
    {
        Run("""
            class Base { public int value; }
            class Derived : Base { public int extra; }
            int main()
            {
                Derived item = new();
                const reflection inherited = reflect(Base::value);
                const reflection own = reflect(Derived::extra);
                item.[:inherited:] = 12;
                item.[:own:] = 30;
                return item.[:inherited:] + item.[:own:] - 42;
            }
            """, optimization);
    }

    [Theory]
    [InlineData("struct Other { public int value; }", "Other item = new();", "item.[:field:] = 1;")]
    [InlineData("", "readonly Item item = new();", "item.[:field:] = 1;")]
    [InlineData("", "Item item = new();", "const reflection value = reflect(1); item.[:value:] = 1;")]
    public void InvalidReceiversAndWritesAreRejected(string declarations, string local, string operation)
    {
        Assert.Throws<TypeCheckException>(() => Compiler.Check($$"""
            struct Item { public int value; }
            {{declarations}}
            int main()
            {
                {{local}}
                const reflection field = reflect(Item::value);
                {{operation}}
                return 0;
            }
            """, options: new() { NoStdLib = true }));
    }

    private static void Run(string source, int optimization)
    {
        string ir = Compiler.Emit(source, options: new() { NoStdLib = true, InternalizeFunctions = true });
        CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }
}
