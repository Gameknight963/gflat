using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class RuntimeAbiBindingTests
{
    [Fact]
    public void LibraryFunctionNamesDoNotSelectTheRuntimeAllocator()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check("""
            namespace Memory {
                public void*? Allocate(nuint size) => null;
                public void Free(void* address) nothrow {}
            }
            struct Item {}
            int main() { Item* item = new* Item(); delete item; return 0; }
            """, options: new() { NoStdLib = true }));
        Assert.Contains("__gflat_runtime::Allocate", error.Message);
    }

    [Fact]
    public void StandaloneProgramsCanSupplyTheRuntimeAbiWithoutLibraryNames()
    {
        string ir = Compiler.Emit("""
            class Exception {}
            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            namespace __gflat_runtime {
                public class AllocationFailure : Exception {}
                public void*? Allocate(nuint bytes) => malloc(bytes);
                public void Free(void* storage) nothrow => free(storage);
            }
            struct Item { public int Value = 42; }
            int main() { Item* item = new* Item(); int result = item.Value; delete item; return result; }
            """, options: new() { NoStdLib = true, InternalizeFunctions = true });
        Assert.DoesNotContain("gflat$Memory$", ir);
        Assert.Equal(42, CompilerTestHelper.RunIr(ir).ExitCode);
    }
}
