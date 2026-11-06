namespace gflat.Tests;

public sealed class ImplicitPointerViewTests
{
    [Fact]
    public void MutablePointersConvertToVoidAndProtectedViewsInAssignmentsAndCalls()
        => Compiler.Check("""
            struct Item { int Value; }
            void AcceptVoid(void* storage) {}
            void AcceptReadonly(readonly(Item)* item) {}
            void AcceptImmutable(immutable(Item)* item) {}
            int main()
            {
                Item item = new();
                Item* pointer = &item;
                void* storage = pointer;
                readonly(Item)* view = pointer;
                immutable(Item)* frozen = pointer;
                AcceptVoid(pointer);
                AcceptReadonly(pointer);
                AcceptImmutable(pointer);
                return 0;
            }
            """, options: new() { NoStdLib = true, NoBuiltins = true });
}
