namespace gflat.Tests;

public sealed class GarbageCollectorRuntimeTests
{
    private static void Succeeds(string body)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-gc-slots-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string source = Path.Combine(directory, "main.cpp");
            string executable = Path.Combine(directory, OperatingSystem.IsWindows() ? "main.exe" : "main");
            File.WriteAllText(source, """
                #include <cstddef>
                #include <cstdlib>
                #include <cstring>
                extern "C" {
                bool __gflat_gc_track_native(void*, std::size_t) noexcept;
                void __gflat_gc_forget_native(void*) noexcept;
                bool __gflat_gc_adopt(void*, std::size_t) noexcept;
                bool __gflat_gc_prepare_allocation(void*, std::size_t) noexcept;
                void __gflat_gc_reference_added(void*) noexcept;
                void __gflat_gc_reference_removed(void*) noexcept;
                std::size_t __gflat_gc_collect(void (*)(void*)) noexcept;
                std::size_t __gflat_gc_managed_count() noexcept;
                }
                static int released = 0;
                static void release(void* storage) {
                    __gflat_gc_forget_native(storage);
                    ++released;
                    std::free(storage);
                }
                static void* allocate(std::size_t bytes) {
                    void* result = std::malloc(bytes);
                    if (!result || !__gflat_gc_track_native(result, bytes) ||
                        !__gflat_gc_prepare_allocation(result, bytes)) std::abort();
                    return result;
                }
                int main() {
                """ + body + "\n}");
            string driver = NativeToolchain.FindClang();
            var compilation = NativeToolchain.CompileCpp(source, executable, [NativeRuntime.GetCollectorObject(driver)]);
            Assert.True(compilation.ExitCode == 0, compilation.StandardError);
            var result = NativeToolchain.Run(executable, []);
            Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void InitializedRootSlotsSurviveAndRepeatedNotificationsAreIdempotent() => Succeeds("""
        void* root = allocate(32);
        __gflat_gc_reference_added(&root);
        __gflat_gc_reference_added(&root);
        if (__gflat_gc_collect(release) != 0) return 1;
        __gflat_gc_reference_removed(&root);
        __gflat_gc_reference_removed(&root);
        if (__gflat_gc_collect(release) != 1 || released != 1) return 2;
        return 0;
        """);

    [Fact]
    public void RawNativeBytesDoNotBecomeManagedRoots() => Succeeds("""
        void* object = allocate(32);
        void* native = std::malloc(sizeof(void*));
        if (!__gflat_gc_track_native(native, sizeof(void*))) return 1;
        std::memcpy(native, &object, sizeof(object));
        if (__gflat_gc_collect(release) != 1) return 2;
        __gflat_gc_forget_native(native);
        std::free(native);
        return released == 1 ? 0 : 3;
        """);

    [Fact]
    public void ManagedSlotsTraceEdgesWithoutRootingUnreachableCycles() => Succeeds("""
        struct Node { Node* next; };
        Node* first = static_cast<Node*>(allocate(sizeof(Node)));
        Node* second = static_cast<Node*>(allocate(sizeof(Node)));
        if (first->next || second->next) return 1;
        first->next = second;
        second->next = first;
        __gflat_gc_reference_added(&first->next);
        __gflat_gc_reference_added(&second->next);
        __gflat_gc_reference_added(&first);
        if (__gflat_gc_collect(release) != 0) return 2;
        __gflat_gc_reference_removed(&first);
        if (__gflat_gc_collect(release) != 2 || released != 2) return 3;
        // Swept objects must not leave dangling field-slot registrations.
        allocate(sizeof(Node));
        return __gflat_gc_collect(release) == 1 ? 0 : 4;
        """);

    [Fact]
    public void AdoptionChangesNativeSlotsIntoOwnedEdgesAndNativeFreeRetiresSlots() => Succeeds("""
        void** native = static_cast<void**>(std::malloc(sizeof(void*)));
        if (!__gflat_gc_track_native(native, sizeof(void*))) return 1;
        *native = allocate(32);
        __gflat_gc_reference_added(native);
        if (__gflat_gc_collect(release) != 0) return 2;
        if (!__gflat_gc_adopt(native, sizeof(void*))) return 3;
        if (__gflat_gc_collect(release) != 2) return 4;

        native = static_cast<void**>(std::malloc(sizeof(void*)));
        if (!__gflat_gc_track_native(native, sizeof(void*))) return 5;
        *native = allocate(32);
        __gflat_gc_reference_added(native);
        __gflat_gc_forget_native(native);
        std::free(native);
        return __gflat_gc_collect(release) == 1 && __gflat_gc_managed_count() == 0 ? 0 : 6;
        """);
}
