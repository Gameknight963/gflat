using gflat.CompileExceptions;

namespace gflat.Tests;

public class ExplicitDestructorTests
{
    [Fact]
    public void DestroysInteriorSlotsWithoutFreeingAndAllowsReconstruction()
    {
        var result = CompilerTestHelper.Run("""
            extern int printf(readonly(char)* text, ...);
            namespace Allocator {
                public replace void*? Allocate(nuint n) { printf(c"A"); return malloc(n); }
                public replace void Free(void* p) { printf(c"F"); free(p); }
            }
            struct Item {
                public int value;
                public Item(int n) { value = n; }
                ~Item() { printf(c"%d", value); }
            }
            void Destroy<T>(T* p) { p.~T(); }
            Item* Once(Item* p) { printf(c"P"); return p; }
            int main() {
                Item* data = (Item*)Allocator::Allocate((nuint)sizeof(Item) * (nuint)2);
                new* Item(1) at data;
                new* Item(2) at data + 1;
                Once(data + 1).~Item();
                new* Item(3) at data + 1;
                Destroy<Item>(data + 1);
                Destroy<Item>(data);
                Allocator::Free((void*)data);
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("AP231F", result.StandardOutput);
    }

    [Fact]
    public void ImplicitDestructorsCleanFieldsAndClassBases()
    {
        var result = CompilerTestHelper.Run("""
            extern int printf(readonly(char)* text, ...);
            struct Field { ~Field() { printf(c"F"); } }
            struct Outer { public Field field = new Field(); }
            class Base { ~Base() { printf(c"B"); } }
            class Derived : Base { public Outer field = new Outer(); }
            void Destroy<T>(T* p) { p.~T(); }
            int main() {
                Derived* p = new* Derived();
                Destroy<Derived>(p);
                Allocator::Free((void*)p);
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("FB", result.StandardOutput);
    }

    [Fact]
    public void TrivialAndPointerElementsWorkInGenericDestruction()
        => Assert.Equal(0, CompilerTestHelper.Run("""
            void Destroy<T>(T* p) { p.~T(); }
            struct Empty { public int n; }
            int main() {
                int n = 42;
                Destroy<int>(&n);
                int* p = &n;
                Destroy<int*>(&p);
                Empty* empty = new* Empty();
                Destroy<Empty>(empty);
                Allocator::Free((void*)empty);
                return 0;
            }
            """).ExitCode);

    [Theory]
    [InlineData("S*? p = null; p.~S();", "non-null")]
    [InlineData("readonly(S)* p = new* S(); p.~S();", "mutable")]
    [InlineData("S^? p = null; p!.~S();", "raw pointer")]
    [InlineData("S p = new S(); p.~S();", "raw pointer")]
    [InlineData("S* p = new* S(); p.~int();", "must match")]
    [InlineData("void* p = (void*)malloc(8); p.~void();", "concrete")]
    [InlineData("S* p; p.~S();", "unassigned")]
    [InlineData("S* p = new* S(); p.~S(1);", "no arguments")]
    [InlineData("S* p = new* S(); p->~S();", "Use '.'")]
    public void InvalidTargetsAreRejected(string body, string message)
    {
        var error = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(
            "struct S { public int n; } int main() { " + body + " return 0; }"));
        Assert.Contains(message, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NullSuppressionDoesNotBypassDestructionGuard()
    {
        var result = CompilerTestHelper.Run("""
            extern void exit(int code);
            struct S { ~S() { exit(77); } }
            int main() { S*? p = null; p!.~S(); return 0; }
            """);
        Assert.NotEqual(0, result.ExitCode);
        Assert.NotEqual(77, result.ExitCode);
    }

    [Fact]
    public void VirtualDestructionThroughBasePointerRunsCompleteDerivedCleanup()
    {
        var result = CompilerTestHelper.Run("""
            extern int printf(readonly(char)* text, ...);
            class Base { public virtual ~Base() { printf(c"B"); } }
            class Derived : Base { ~Derived() { printf(c"D"); } }
            int main() {
                Base* p = new* Derived();
                p.~Base();
                Allocator::Free((void*)p);
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("DB", result.StandardOutput);
    }

    [Fact]
    public void QualifiedGenericTypeAndGenericContainerCleanupWork()
    {
        var result = CompilerTestHelper.Run("""
            extern int printf(readonly(char)* text, ...);
            namespace Lib {
                public struct Box<T> { public T value; ~Box() { printf(c"D"); } }
            }
            struct Storage<T> {
                public T* data;
                public Storage(T* p) { data = p; }
                ~Storage() { data.~T(); Allocator::Free((void*)data); }
            }
            int main() {
                Lib::Box<int>* p = new* Lib::Box<int>();
                p.~Lib::Box<int>();
                new* Lib::Box<int>() at p;
                Storage<Lib::Box<int>> storage = new Storage<Lib::Box<int>>(p);
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("DD", result.StandardOutput);
    }

    [Fact]
    public void ExplicitDestructionCannotRunDuringConstantEvaluation()
    {
        var error = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check("""
            const int F() { int n = 1; (&n).~int(); return 0; }
            int main() { const int n = F(); return n; }
            """));
        Assert.Contains("Explicit destructor calls", error.Message);
    }

    [Fact]
    public void FixedArrayDestructionRunsElementsInReverseOrder()
    {
        var result = CompilerTestHelper.Run("""
            extern int printf(readonly(char)* text, ...);
            struct S {
                public int n;
                public S(int value) { n = value; }
                ~S() { printf(c"%d", n); }
            }
            void Destroy<T>(T* p) { p.~T(); }
            int main() {
                S[2]* p = (S[2]*)Allocator::Allocate((nuint)sizeof(S[2]));
                S* elements = (S*)p;
                new* S(1) at elements;
                new* S(2) at elements + 1;
                Destroy<S[2]>(p);
                Allocator::Free((void*)p);
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("21", result.StandardOutput);
    }
}
