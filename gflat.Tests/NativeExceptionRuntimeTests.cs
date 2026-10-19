namespace gflat.Tests;

public class NativeExceptionRuntimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RuntimeOwnsNativeObjectsAndPreservesForeignExceptions(bool debugging)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-native-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string source = Path.Combine(directory, "runtime.cpp"), executable = Path.Combine(directory, "runtime.exe");
        try
        {
            string code = """
                #include <cstdint>
                extern "C" {
                    void __gflat_eh_throw(void*, void(*)(void*));
                    void __gflat_eh_throw_debug(void*, void(*)(void*), const char*, std::int64_t, std::int64_t);
                    void* __gflat_eh_capture();
                    void* __gflat_eh_object(void*);
                    void __gflat_eh_release(void*);
                    void __gflat_eh_rethrow(void*);
                    void __gflat_eh_rethrow_debug(void*);
                }
                int destroyed = 0;
                void destroy(void* value) { if (*(int*)value == 42) ++destroyed; }
                struct Guard { int* count; ~Guard() { ++*count; } };
                int main() {
                    __gflat_eh_release(nullptr); // Cleanup after rethrow may own no handle.
                    int value = 42, cleanups = 0;
                    void* handle = nullptr;
                    try { Guard guard{&cleanups}; __gflat_eh_throw(&value, destroy); }
                    catch (...) { handle = __gflat_eh_capture(); }
                    if (cleanups != 1 || destroyed != 0 || __gflat_eh_object(handle) != &value) return 1;
                    try { __gflat_eh_rethrow(handle); }
                    catch (...) { handle = __gflat_eh_capture(); }
                    if (destroyed != 0) return 2;
                    __gflat_eh_release(handle);
                    if (destroyed != 1) return 3;
                    try { throw 17; }
                    catch (...) { handle = __gflat_eh_capture(); }
                    if (__gflat_eh_object(handle) != nullptr) return 4;
                    try { __gflat_eh_rethrow(handle); }
                    catch (int original) { return original == 17 ? 0 : 5; }
                    return 6;
                }
                """;
            if (debugging) code = code.Replace("__gflat_eh_throw(&value, destroy)", "__gflat_eh_throw_debug(&value, destroy, \"Problem\", -1, -1)")
                .Replace("__gflat_eh_rethrow(handle)", "__gflat_eh_rethrow_debug(handle)");
            File.WriteAllText(source, code);
            var compiled = NativeToolchain.CompileCpp(source, executable);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            Assert.Equal(0, NativeToolchain.Run(executable, []).ExitCode);
        }
        finally { File.Delete(source); if (File.Exists(executable)) File.Delete(executable); }
    }
}
