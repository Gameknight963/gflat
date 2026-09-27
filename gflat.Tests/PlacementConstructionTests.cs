using gflat.CompileExceptions;
namespace gflat.Tests;

public class PlacementConstructionTests
{
    [Fact]
    public void UsesDestinationOnceAndRunsNormalInitializationWithoutAllocation()
    {
        var result = CompilerTestHelper.Run("""
            extern int printf(readonly(char)* text, ...);
            namespace Allocator {
                public replace void*? Allocate(nuint n) { printf(c"A"); return malloc(n); }
                public replace void Free(void* p) { printf(c"F"); free(p); }
            }
            struct Thing {
                public int field = 7;
                public int argument;
                public Thing(int n) { argument = n; printf(c"C"); }
                ~Thing() { printf(c"D"); }
            }
            int Argument() { printf(c"a"); return 42; }
            Thing* Destination(Thing* p) { printf(c"p"); return p; }
            int main() {
                Thing* storage = (Thing*)Allocator::Allocate((nuint)sizeof(Thing));
                Thing* result = new* Thing(Argument()) at Destination(storage);
                if (result != storage || result.field != 7 || result.argument != 42) return 1;
                printf(c"S");
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
                Box<int>* storage = (Box<int>*)Allocator::Allocate((nuint)sizeof(Box<int>) * (nuint)2);
                new* Box<int>(11) at storage;
                new* Box<int>(42) at storage + 1;
                int at = storage[0].value + storage[1].value;
                Allocator::Free((void*)storage);
                Empty* empty = (Empty*)Allocator::Allocate((nuint)sizeof(Empty));
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
            struct Field { public int n = 8; ~Field() { printf(c"F"); } }
            class Base { public virtual int Value() { return 1; } ~Base() { printf(c"B"); } }
            class Derived : Base {
                public Field field = new Field();
                public override int Value() { return field.n; }
                ~Derived() { printf(c"D"); }
            }
            int main() {
                Derived* storage = (Derived*)Allocator::Allocate((nuint)sizeof(Derived));
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
            int Fail() throws { throw new* Exception(); }
            int main() {
                void* memory = (void*)Allocator::Allocate((nuint)sizeof(S));
                int* marker = (int*)memory; *marker = 123;
                try { new* S(Fail()) at (S*)memory; return 1; } catch { }
                if (*marker != 123) return 2;
                Allocator::Free(memory);
                return 0;
            }
            """).ExitCode);

    [Fact]
    public void ThrowingPlacementExceptionDoesNotGiveCatchOwnershipOfStorage()
    {
        var result = CompilerTestHelper.Run("""
            extern int printf(readonly(char)* text, ...);
            namespace Allocator {
                public replace void*? Allocate(nuint n) { printf(c"A"); return malloc(n); }
                public replace void Free(void* p) { printf(c"F"); free(p); }
            }
            int main() {
                Exception* storage = (Exception*)Allocator::Allocate((nuint)sizeof(Exception));
                try { throw new* Exception() at storage; } catch { printf(c"C"); }
                printf(c"S"); delete storage;
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("ACSF", result.StandardOutput);
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
    [InlineData("S* p = (S*)Allocator::Allocate((nuint)sizeof(S)); new S() at p;")]
    [InlineData("S* p = (S*)Allocator::Allocate((nuint)sizeof(S)); new^ S() at p;")]
    [InlineData("S*? p = null; new* S() at p;")]
    [InlineData("readonly(S)* p = (readonly(S)*)Allocator::Allocate((nuint)sizeof(S)); new* S() at p;")]
    [InlineData("void* p = (void*)Allocator::Allocate((nuint)sizeof(S)); new* S() at p;")]
    [InlineData("S* p; new* S() at p;")]
    [InlineData("S^? p = null; new* S() at p!;")]
    [InlineData("S* p = (S*)Allocator::Allocate((nuint)sizeof(S)); new* S(1) at p;")]
    public void InvalidDestinationsAndConstructorsAreRejected(string body)
        => Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check("struct S { public int n; } int main() { " + body + " return 0; }"));

    [Theory]
    [InlineData("S*? p = null; new* S() at p!;")]
    [InlineData("void* memory = (void*)Allocator::Allocate(32); S* p = (S*)((char*)memory + 1); new* S() at p;")]
    public void NullAndMisalignedStorageTrapBeforeConstructor(string body)
    {
        var result = CompilerTestHelper.Run("extern void exit(int code); struct S { public long n; public S() { exit(77); } } int main() { " + body + " return 0; }");
        Assert.NotEqual(0, result.ExitCode);
        Assert.NotEqual(77, result.ExitCode);
    }
}
