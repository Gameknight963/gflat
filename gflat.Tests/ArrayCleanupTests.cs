using gflat.CompileExceptions;

namespace gflat.Tests;

public class ArrayCleanupTests
{
    private const string Prelude = """
        extern int printf(readonly(char)* format, ...);
        struct Item { public int id; ~Item() { printf("%d", id); } }
        """;
    private const string Items = "Item[3] items = default(Item[3]); items[0].id = 1; items[1].id = 2; items[2].id = 3;";

    [Theory]
    [InlineData("", "321E")]
    [InlineData("return 0;", "321")]
    public void ScopeExitDestroysElementsInReverseOrder(string exit, string expected)
    {
        Check("int main() { { " + Items + exit + " } printf(\"E\"); return 0; }", expected);
    }

    [Theory]
    [InlineData("break;", "321E")]
    [InlineData("continue;", "321321E")]
    public void LoopExitsCleanOnce(string exit, string expected)
    {
        Check("int main() { for (int i = 0; i < 2; i++) { " + Items + exit + " } printf(\"E\"); return 0; }", expected);
    }

    [Fact]
    public void ThrowCleansArray()
    {
        Check("void fail() throws { " + Items + " throw new* Exception(); } int main() { try { fail(); } catch { printf(\"C\"); } return 0; }", "321C");
    }

    [Fact]
    public void DeletePointerToArrayDestroysElementsBeforeFree()
    {
        Check("""
            extern void* malloc(nuint size);
            extern void free(void* ptr);
            namespace Allocator { public replace void*? Allocate(nuint size) { return malloc(size); } }
            namespace Allocator { public replace void Free(void* ptr) { printf("F"); free(ptr); } }
            int main() {
                Item[2]* items = (Item[2]*)malloc((nuint)sizeof(Item[2]));
                (*items)[0].id = 1;
                (*items)[1].id = 2;
                delete items;
                return 0;
            }
            """, "21F");
    }

    [Fact]
    public void InferredArraySizeGetsCleanup()
    {
        Check("int main() { Item[] items = default(Item[2]); items[0].id = 1; items[1].id = 2; return 0; }", "21");
    }

    [Fact]
    public void AliasedArrayGetsCleanup()
    {
        Check("alias Items = Item[2]; int main() { Items items = default(Items); items[0].id = 1; items[1].id = 2; return 0; }", "21");
    }

    [Fact]
    public void ClassDestructionCleansArrayBeforeBase()
    {
        Check("class Base { ~Base() { printf(\"B\"); } } class Box : Base { public Item[2] items; ~Box() { printf(\"D\"); return; } } int main() { Box b = new Box(); b.items[0].id = 1; b.items[1].id = 2; return 0; }", "D21B");
    }

    [Fact]
    public void ReturningArrayTransfersCleanupToCaller()
    {
        Check("Item[3] make() { " + Items + " return items; } int main() { { Item[3] result = make(); printf(\"R\"); } return 0; }", "R321");
    }

    [Fact]
    public void FailedInitializerDoesNotDestroyDestination()
    {
        Check("Item[3] fail() throws { " + Items + " throw new* Exception(); } int main() { try { Item[3] result = fail(); } catch { printf(\"C\"); } return 0; }", "321C");
    }

    [Fact]
    public void DiscardedArrayReturnIsDestroyed()
    {
        Check("Item[3] make() { " + Items + " return items; } int main() { make(); return 0; }", "321");
    }

    [Fact]
    public void HeapObjectCleansArrayFields()
    {
        Check("struct Box { public Item[2] items; } int main() { Box* b = new* Box(); b.items[0].id = 1; b.items[1].id = 2; delete b; return 0; }", "21");
    }

    [Fact]
    public void ArrayCleanupComposesWithThrowingDefer()
    {
        Check("void fail() throws { throw new* Exception(); } int main() { try { " + Items + " defer fail(); } catch { printf(\"C\"); } return 0; }", "321C");
    }

    [Fact]
    public void DefaultClassArrayHasValidObjectsAndRunsDestructors()
    {
        Check("class C { ~C() { printf(\"X\"); } } int main() { C[2] a = default(C[2]); return 0; }", "XX");
    }

    [Fact]
    public void PointerArrayDoesNotOwnPointees()
    {
        Check("int main() { Item item = default(Item); item.id = 7; Item*[2] a; a[0] = &item; a[1] = &item; return 0; }", "7");
    }

    [Fact]
    public void NestedArrayFieldsReceiveSynthesizedCleanup()
    {
        Check("struct Box { public Item[2] items; } int main() { Box b = new Box(); b.items[0].id = 1; b.items[1].id = 2; return 0; }", "21");
    }

    [Fact]
    public void MultidimensionalArrayCleansInReverseOrder()
    {
        Check("int main() { Item[2][2] a = default(Item[2][2]); a[0][0].id = 1; a[0][1].id = 2; a[1][0].id = 3; a[1][1].id = 4; return 0; }", "4321");
    }

    [Theory]
    [InlineData("Item[2] a;")]
    [InlineData("Item[2] a = default(Item[2]); Item[2] b = a;")]
    [InlineData("Item[2] a = default(Item[2]); Item[2] b = default(Item[2]); b = a;")]
    [InlineData("Item[3] a = default(Item[2]);")]
    public void UnsafeArrayInitializationAndCopyAreRejected(string body)
    {
        Assert.Throws<TypeCheckException>(() => Compiler.Check(Prelude + "int main() { " + body + " return 0; }"));
    }

    private static void Check(string source, string expected)
    {
        var result = CompilerTestHelper.Run(Prelude + source);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expected, result.StandardOutput);
    }
}
