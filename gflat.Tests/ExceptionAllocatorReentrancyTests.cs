namespace gflat.Tests;

public sealed class ExceptionAllocatorReentrancyTests
{
    [Fact]
    public void ThrowingPayloadAllocatorUsesReserveWithoutReenteringTheCallback()
    {
        var result = CompilerTestHelper.Run("""
            extern int putchar(int value);
            int calls = 0;
            struct Guard { ~Guard() { putchar(68); } }
            namespace Memory {
                public replace void*? Allocate(nuint bytes) {
                    calls++;
                    // Bound the regression even on a runtime lacking its guard.
                    if (calls > 1) return null;
                    putchar(65);
                    throw new Exception("allocator failed", 42);
                }
                public replace void Free(void* storage) nothrow { putchar(70); }
            }
            int main() {
                try {
                    Guard guard = new Guard();
                    throw new Exception("original exception", 1);
                } catch (ref Exception error) {
                    if (calls != 1 || error.GetCode() != 42) return 2;
                    putchar(67);
                }
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("ADC", result.StandardOutput);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4096)]
    public void NativeCallbackGuardResetsAfterUnwindingAndUsesOnlyAvailableReserve(int reserveBytes)
    {
        string root = Path.Combine(Path.GetTempPath(), "gflat-reentrant-allocator-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string source = Path.Combine(root, "reentrant.cpp"), executable = Path.Combine(root, "reentrant.exe");
            string runtime = Path.Combine(AppContext.BaseDirectory, "Runtime", "Exceptions.cpp").Replace('\\', '/');
            File.WriteAllText(source, $$"""
                #define GFLAT_EXCEPTION_RESERVE_BYTES {{reserveBytes}}
                #include "{{runtime}}"
                int callback_calls = 0;
                int deallocator_calls = 0;
                void* recursive(std::size_t bytes) {
                    if (++callback_calls > 1) std::_Exit(31);
                    return __gflat_eh_allocate_exception(bytes, recursive);
                }
                void* foreign_failure(std::size_t) { throw 17; }
                void* counted_malloc(std::size_t bytes) { ++callback_calls; return std::malloc(bytes); }
                void counted_free(void* storage) { ++deallocator_calls; std::free(storage); }
                int main(int argc, char**) {
                    std::set_terminate([] { std::_Exit(42); });
                    if (argc > 1) {
                        void* value = __gflat_eh_allocate_exception(16, recursive);
                        if (!value || !reserved(value) || callback_calls != 1) return 1;
                        __gflat_eh_free_exception(value, counted_free);
                        if (deallocator_calls != 0) return 2;
                    }
                    // A callback exception must propagate unchanged and reset the
                    // thread's guard; normal allocation must still call malloc.
                    for (int i = 0; i < 100; ++i) {
                        try { __gflat_eh_allocate_exception(16, foreign_failure); return 3; }
                        catch (int value) { if (value != 17) return 4; }
                        callback_calls = 0;
                        void* value = __gflat_eh_allocate_exception(16, counted_malloc);
                        if (!value || reserved(value) || callback_calls != 1) return 5;
                        __gflat_eh_free_exception(value, counted_free);
                    }
                    return 0;
                }
                """);
            var arguments = new List<string> { "--driver-mode=g++", "-std=c++17", "-O2", "-fexceptions", source, "-o", executable };
            arguments.AddRange(NativeRuntime.HostArguments());
            var compiled = NativeToolchain.Run(NativeToolchain.FindClang(), arguments, 60000);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            Assert.Equal(0, NativeToolchain.Run(executable, []).ExitCode);
            Assert.Equal(reserveBytes == 0 ? 42 : 0, NativeToolchain.Run(executable, ["recursive"]).ExitCode);
        }
        finally { Directory.Delete(root, true); }
    }
}
