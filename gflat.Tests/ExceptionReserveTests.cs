using gflat.Projects;

namespace gflat.Tests;

public class ExceptionReserveTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(128)]
    [InlineData(4096)]
    public void NativeReserveReusesStorageAndSurvivesBookkeepingAllocationFailure(int bytes)
    {
        string root = Path.Combine(Path.GetTempPath(), "gflat-reserve-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string source = Path.Combine(root, "reserve.cpp"), output = Path.Combine(root, "reserve.exe");
            string runtime = Path.Combine(AppContext.BaseDirectory, "Runtime", "Exceptions.cpp").Replace('\\', '/');
            File.WriteAllText(source, $$"""
                #define GFLAT_EXCEPTION_RESERVE_BYTES {{bytes}}
                #define GFLAT_EH_TEST_FORCE_RESERVE
                #include "{{runtime}}"
                #include <thread>
                void* fail(std::size_t) { return nullptr; }
                int frees = 0;
                void free_normal(void* p) { ++frees; std::free(p); }
                void cleanup(void* p) { __gflat_eh_free_exception(p, free_normal); }
                int main() {
                    // Zero/tiny reserves do not manufacture usable storage.
                    if ({{bytes}} < 256) {
                        if (reserve_allocate({{bytes}} + 1) != nullptr) return 1;
                        if (reserve_allocate(std::numeric_limits<std::size_t>::max()) != nullptr) return 2;
                        if ({{bytes}} == 0 && reserve_allocate(1) != nullptr) return 3;
                        return 0;
                    }
                    // Fill the pool, free in a fragmented order, then coalesce it.
                    void* blocks[128]; int count = 0;
                    while (count < 128 && (blocks[count] = reserve_allocate(1))) ++count;
                    if (!count) return 4;
                    for (int i = 0; i < count; i += 2) reserve_free(blocks[i]);
                    for (int i = 1; i < count; i += 2) reserve_free(blocks[i]);
                    void* large = reserve_allocate({{bytes}} - sizeof(Block));
                    if (!large) return 5;
                    reserve_free(large);
                    for (int i = 0; i < 100; ++i) {
                        void* p = __gflat_eh_allocate_exception(sizeof(int), fail);
                        if (reinterpret_cast<std::size_t>(p) % alignof(std::max_align_t)) return 6;
                        new (p) int(42);
                        void* handle;
                        try { __gflat_eh_throw(p, cleanup); }
                        catch (...) { handle = __gflat_eh_capture(); }
                        if (*(int*)__gflat_eh_object(handle) != 42) return 7;
                        try { __gflat_eh_rethrow(handle); }
                        catch (...) { handle = __gflat_eh_capture(); }
                        // Release from another thread, including on MSVC where
                        // current_exception may have copied the transport object.
                        std::thread releaser([handle] { __gflat_eh_release(handle); });
                        releaser.join();
                    }
                    if (frees != 0) return 8; // User allocator must never see reserve storage.
                    void* normal = __gflat_eh_allocate_exception(8, std::malloc);
                    if (reserved(normal)) return 9;
                    __gflat_eh_free_exception(normal, free_normal);
                    return frees == 1 ? 0 : 10;
                }
                """);
            var args = new List<string> { "--driver-mode=g++", "-std=c++17", "-O2", "-fexceptions", source, "-o", output };
            args.AddRange(NativeRuntime.HostArguments());
            if (!OperatingSystem.IsWindows()) args.Add("-pthread");
            var compiled = NativeToolchain.Run(NativeToolchain.FindClang(), args, 60000);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            Assert.Equal(0, NativeToolchain.Run(output, []).ExitCode);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ExceptionPayloadUsesReserveWhenCustomAllocatorFails()
    {
        string source = """
            namespace Memory {
                public replace void*? Allocate(nuint size) => null;
                public replace void Free(void* ptr) nothrow { }
            }
            int main() {
                for (int i = 0; i < 100; i++) {
                    try { throw new Exception("test", 42); }
                    catch (ref Exception error) { if (error.GetCode() != 42) return 1; }
                }
                return 0;
            }
            """;
        Assert.Equal(0, CompilerTestHelper.Run(source).ExitCode);
    }

    [Fact]
    public void NewAllocationFailureThrowsBadAllocWithoutRunningConstructor()
    {
        var result = CompilerTestHelper.Run("""
            extern int printf(immutable(char*) format, ...);
            namespace Memory {
                public replace void*? Allocate(nuint size) => null;
                public replace void Free(void* ptr) nothrow { }
            }
            class Widget { public Widget() { printf("constructed"); } }
            int main() {
                try { Widget* p = new* Widget(); delete p; }
                catch (ref Standard::BadAllocException error) {
                    if (error.GetCode() != 1) return 1;
                    return 42;
                }
                return 2;
            }
            """);
        Assert.Equal(42, result.ExitCode);
        Assert.Equal("", result.StandardOutput);
    }

    [Fact]
    public void ConstructorFailureReleasesNormalAllocationWithMatchingAllocator()
    {
        var result = CompilerTestHelper.Run("""
            extern void*? malloc(nuint size);
            extern void free(void* storage);
            extern int printf(immutable(char*) format, ...);
            namespace Memory {
                public replace void*? Allocate(nuint size) { printf("A;"); return malloc(size); }
                public replace void Free(void* ptr) nothrow { printf("F;"); free(ptr); }
            }
            class Widget { public Widget() { throw new Exception(); } }
            int main() {
                try { Widget* p = new* Widget(); delete p; }
                catch (ref Exception error) { }
                return 42;
            }
            """);
        Assert.Equal(42, result.ExitCode);
        Assert.Equal("A;A;F;F;", result.StandardOutput);
    }

    [Fact]
    public void CustomRootCanSupplyAnEditedBadAllocDefinition()
    {
        var sources = new SourceFile[] {
            new("main.gf", """
                class Exception { public int GetCode() => 1; }
                namespace Memory {
                    public replace void*? Allocate(nuint size) => null;
                    public replace void Free(void* ptr) nothrow { }
                }
                struct Widget { int value; }
                int main() {
                    try { Widget* p = new* Widget(); delete p; }
                    catch (ref Standard::BadAllocException error) { return 42; }
                    return 1;
                }
                """),
            new("Standard/core/BadAllocException.gf", "namespace Standard; public class BadAllocException : Exception { public BadAllocException() {} }")
        };
        Assert.Equal(42, CompilerTestHelper.Run(sources).ExitCode);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("2147483648")]
    [InlineData("bad")]
    public void InvalidSizesAreRejected(string value)
    {
        Assert.Throws<ArgumentException>(() => ExceptionReserve.Parse(value));
        Assert.Equal(2, Program.Main(["--exception-reserve-bytes", value]));
    }

    [Fact]
    public void ProjectSettingAndCliOverrideReachNativeRuntime()
    {
        string root = Path.Combine(Path.GetTempPath(), "gflat-reserve-project-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string project = Path.Combine(root, "app.gfproj");
            string source = Path.Combine(root, "main.gf");
            File.WriteAllText(project, TestSupport.ProjectFileFixture.Xml(content:
                "<PropertyGroup><NoStdLib>false</NoStdLib><GflatExceptionReserveBytes>0</GflatExceptionReserveBytes></PropertyGroup>"));
            File.WriteAllText(source, """
                namespace Memory {
                    public replace void*? Allocate(nuint size) => null;
                    public replace void Free(void* ptr) nothrow { }
                }
                int main() {
                    try { throw new Exception(); }
                    catch (ref Exception error) { return 42; }
                }
                """);
            Assert.Equal(0, ProjectModel.Load(project).ExceptionReserveBytes);
            Assert.Equal(42, Program.Main([project, "--run", "--exception-reserve-bytes", "4096"]));
            Assert.NotEqual(42, Program.Main([project, "--run"]));
            Assert.Equal(42, Program.Main([source, "--run", "--exception-reserve-bytes", "4096"]));
            Assert.NotEqual(42, Program.Main([source, "--run", "--exception-reserve-bytes", "0"]));
            File.WriteAllText(project, TestSupport.ProjectFileFixture.Xml(content:
                "<PropertyGroup><NoStdLib>false</NoStdLib><GflatExceptionReserveBytes>bad</GflatExceptionReserveBytes></PropertyGroup>"));
            Assert.Throws<ProjectException>(() => ProjectModel.Load(project));
            Assert.Equal(42, Program.Main([project, "--run", "--exception-reserve-bytes", "4096"]));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void RuntimeCacheSeparatesSizes()
    {
        string clang = NativeToolchain.FindClang();
        string disabled = NativeRuntime.GetObject(clang, 0);
        string enabled = NativeRuntime.GetObject(clang, 1024);
        Assert.NotEqual(disabled, enabled);
        Assert.Equal(disabled, NativeRuntime.GetObject(clang, 0));
    }
}
