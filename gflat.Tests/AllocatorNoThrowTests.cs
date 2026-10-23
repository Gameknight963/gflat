using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class AllocatorNoThrowTests
{
    [Theory]
    [InlineData("namespace Memory { public void Free(void* storage) {} }")]
    [InlineData("namespace Memory { public weak void Free(void* storage) {} }")]
    [InlineData("alias Address = void*; namespace Memory { public void Free(Address storage) {} }")]
    public void StandaloneAllocatorRequiresNonThrowingFree(string source)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(source,
            options: new() { NoStdLib = true }));
        Assert.Contains("Memory::Free must be declared nothrow", error.Message);
    }

    [Theory]
    [InlineData("namespace Memory { public replace void Free(void* storage) {} }")]
    [InlineData("public replace void Memory::Free(void* storage) {}")]
    public void ReplacementMustPreserveDefaultNonThrowingContract(string declaration)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(
            "namespace Memory { public replace void*? Allocate(nuint size) => null; } " + declaration));
        Assert.Contains("must match", error.Message);
    }

    [Fact]
    public void RequirementDoesNotApplyToUnrelatedFreeMethodsOrOverloads()
    {
        Compiler.Check("""
            namespace Other { public void Free(void* storage) {} }
            namespace Memory {
                public void Free(void* storage) nothrow {}
                public void Free(void* storage, int tag) {}
            }
            """, options: new() { NoStdLib = true });
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    [InlineData(2, true)]
    public void EscapingDeallocatorExceptionTerminatesBeforeCallerCatch(int optimization, bool indirect)
    {
        string call = indirect
            ? "void(void*)* release = Memory::Free; release(storage!);"
            : "Memory::Free(storage!);";
        var result = CompilerTestHelper.Run("""
            extern void*? malloc(nuint bytes);
            namespace Memory {
                public replace void*? Allocate(nuint bytes) => malloc(bytes);
                public replace void Free(void* storage) nothrow {
                    throw new Exception("deallocator failure", 42);
                }
            }
            int main() {
                void*? storage = Memory::Allocate(1);
                if (storage == null) return 90;
                try { CALL }
                catch { return 91; }
                return 92;
            }
            """.Replace("CALL", call), optimization);
        Assert.Equal(42, result.ExitCode);
        Assert.Empty(result.StandardOutput);
        Assert.Empty(result.StandardError);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DeallocatorCanCatchItsOwnExceptionAndReleaseNormally(int optimization)
    {
        var result = CompilerTestHelper.Run("""
            extern void*? malloc(nuint bytes);
            extern void free(void* storage);
            int released = 0;
            namespace Memory {
                public replace void*? Allocate(nuint bytes) => malloc(bytes);
                public replace void Free(void* storage) nothrow {
                    // Releasing the caught exception also calls this allocator.
                    if (released == 0) {
                        try { throw new Exception("handled internally", 42); }
                        catch (ref Exception error) { released = error.GetCode(); }
                    }
                    free(storage);
                }
            }
            int main() {
                void*? storage = Memory::Allocate(1);
                if (storage == null) return 90;
                Memory::Free(storage!);
                if (released != 42) return 91;
                return 0;
            }
            """, optimization);
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.StandardOutput);
        Assert.Empty(result.StandardError);
    }
}
