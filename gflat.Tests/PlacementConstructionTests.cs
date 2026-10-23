using gflat.CompileExceptions;
namespace gflat.Tests;

public class PlacementConstructionTests
{
    [Fact]
    public void UsesDestinationOnceAndRunsNormalInitializationWithoutAllocation()
    {
        var result = CompilerTestHelper.Run("""
            extern void*? malloc(nuint size);
            extern void free(void* storage);
            extern int printf(readonly(char)* text, ...);
            namespace Memory {
                public replace void*? Allocate(nuint n) { printf("A"); return malloc(n); }
                public replace void Free(void* p) nothrow { printf("F"); free(p); }
            }
            struct Thing {
                public int field = 7;
                public int argument;
                public Thing(int n) { argument = n; printf("C"); }
                ~Thing() { printf("D"); }
            }
            int Argument() { printf("a"); return 42; }
            Thing* Destination(Thing* p) { printf("p"); return p; }
            int main() {
                Thing* storage = (Thing*)Memory::Allocate((nuint)sizeof(Thing));
                Thing* result = new* Thing(Argument()) at Destination(storage);
                if (result != storage || result.field != 7 || result.argument != 42) return 1;
                printf("S");
                delete result;
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("AapCSDF", result.StandardOutput);
    }

    [Fact]
    public void SupportsGenericSlotsPointerArithmeticAndContextualAtIdentifier()
        => Assert.Equal(0, CompilerTestHelper.Run("""
            struct Box<T> { public T value; public Box(T n) { value = n; } }
            T* Place<T>(T* storage) { return new* T() at storage; }
            struct Empty { public int value = 9; }
            int main() {
                Box<int>* storage = (Box<int>*)Memory::Allocate((nuint)sizeof(Box<int>) * (nuint)2);
                new* Box<int>(11) at storage;
                new* Box<int>(42) at storage + 1;
                int at = storage[0].value + storage[1].value;
                Memory::Free((void*)storage);
                Empty* empty = (Empty*)Memory::Allocate((nuint)sizeof(Empty));
                Empty* result = Place<Empty>(empty);
                if (result.value != 9 || at != 53) return 1;
                delete result;
                return 0;
            }
            """).ExitCode);

    [Fact]
    public void InitializesClassMetadataBaseAndFields()
    {
        var result = CompilerTestHelper.Run("""
            extern int printf(readonly(char)* text, ...);
            struct Field { public int n = 8; ~Field() { printf("F"); } }
            class Base { public virtual int Value() { return 1; } ~Base() { printf("B"); } }
            class Derived : Base {
                public Field field = new Field();
                public override int Value() { return field.n; }
                ~Derived() { printf("D"); }
            }
            int main() {
                Derived* storage = (Derived*)Memory::Allocate((nuint)sizeof(Derived));
                Derived* object = new* Derived() at storage;
                Base* view = object;
                if (view.Value() != 8) return 1;
                delete object;
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("DFB", result.StandardOutput);
    }

    [Fact]
    public void ArgumentFailureLeavesDestinationUntouched()
        => Assert.Equal(0, CompilerTestHelper.Run("""
            struct S { public int value; public S(int n) { value = n; } }
            int Fail() { throw new Exception(); }
            int main() {
                void* memory = (void*)Memory::Allocate((nuint)sizeof(S));
                int* marker = (int*)memory; *marker = 123;
                try { new* S(Fail()) at (S*)memory; return 1; } catch { }
                if (*marker != 123) return 2;
                Memory::Free(memory);
                return 0;
            }
            """).ExitCode);

    [Fact]
    public void ThrowingPlacementExceptionDoesNotGiveCatchOwnershipOfStorage()
    {
        var result = CompilerTestHelper.Run("""
            extern void*? malloc(nuint size);
            extern void free(void* storage);
            extern int printf(readonly(char)* text, ...);
            namespace Memory {
                public replace void*? Allocate(nuint n) { printf("A"); return malloc(n); }
                public replace void Free(void* p) nothrow { printf("F"); free(p); }
            }
            int main() {
                Exception* storage = (Exception*)Memory::Allocate((nuint)sizeof(Exception));
                try { new* Exception() at storage; throw *storage; } catch { printf("C"); }
                printf("S"); delete storage;
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("AACFSF", result.StandardOutput);
    }

    [Fact]
    public void PlacementIsRejectedDuringConstantEvaluation()
    {
        var error = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check("""
            struct S { public int value; }
            const S* Make(S* storage) { return new* S() at storage; }
            int main() {
                const S* p = Make(new* S());
                return 0;
            }
            """));
        Assert.Contains("Placement construction", error.Message);
    }

    [Theory]
    [InlineData("S* p = (S*)Memory::Allocate((nuint)sizeof(S)); new S() at p;")]
    [InlineData("S* p = (S*)Memory::Allocate((nuint)sizeof(S)); new^ S() at p;")]
    [InlineData("S*? p = null; new* S() at p;")]
    [InlineData("immutable(S)* p = (immutable(S)*)Memory::Allocate((nuint)sizeof(S)); new* S() at p;")]
    [InlineData("void* p = (void*)Memory::Allocate((nuint)sizeof(S)); new* S() at p;")]
    [InlineData("S* p; new* S() at p;")]
    [InlineData("S^? p = null; new* S() at p!;")]
    [InlineData("S* p = (S*)Memory::Allocate((nuint)sizeof(S)); new* S(1) at p;")]
    public void InvalidDestinationsAndConstructorsAreRejected(string body)
        => Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check("struct S { public int n; } int main() { " + body + " return 0; }"));

    [Theory]
    [InlineData("S*? p = null; new* S() at p!;")]
    [InlineData("void* memory = (void*)Memory::Allocate(32); S* p = (S*)((char*)memory + 1); new* S() at p;")]
    public void NullAndMisalignedStorageTrapBeforeConstructor(string body)
    {
        var result = CompilerTestHelper.Run("extern void exit(int code); struct S { public long n; public S() { exit(77); } } int main() { " + body + " return 0; }");
        Assert.NotEqual(0, result.ExitCode);
        Assert.NotEqual(77, result.ExitCode);
    }
}
