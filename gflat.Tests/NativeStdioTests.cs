namespace gflat.Tests;

public sealed class NativeStdioTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task DirectoryBridgeCreatesAndRemovesOneComponentWithTheCOnlyHostProfile(int optimization)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-native-directory-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string child = Path.Combine(directory, "child");
            string literal = "\"" + child.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            string ir = Compiler.Emit($$"""
                extern int __gflat_stdio_make_directory(readonly(char)* path) nothrow;
                extern int __gflat_stdio_remove_directory(readonly(char)* path) nothrow;
                extern int __gflat_stdio_path_kind(readonly(char)* path) nothrow;
                int main() nothrow {
                    if (__gflat_stdio_make_directory({{literal}}) != 0) return 2;
                    if (__gflat_stdio_path_kind({{literal}}) != 2) return 3;
                    if (__gflat_stdio_make_directory({{literal}}) == 0) return 4;
                    if (__gflat_stdio_remove_directory({{literal}}) != 0) return 5;
                    if (__gflat_stdio_path_kind({{literal}}) == 2) return 6;
                    if (__gflat_stdio_remove_directory({{literal}}) == 0) return 7;
                    return 0;
                }
                """, options: new() { NoStdLib = true, NoBuiltins = true, InternalizeFunctions = true });
            Assert.Equal((false, true), NativeToolchain.RequiredNativeRuntimes(ir));
            _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
            Assert.False(Directory.Exists(child));
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void LengthBridgeUsesTheCOnlyHostProfile(int optimization)
    {
        string ir = Compiler.Emit("""
            extern void*? tmpfile() nothrow;
            extern int fclose(void* stream) nothrow;
            extern int fflush(void* stream) nothrow;
            extern int fputs(readonly(char)* text, void* stream) nothrow;
            extern long __gflat_stdio_length(void* stream) nothrow;
            extern int __gflat_stdio_set_length(void* stream, long length) nothrow;
            int main() nothrow {
                void*? stream = tmpfile();
                if (stream == null) return 1;
                if (fputs("abc", stream!) < 0 || fflush(stream!) != 0) return 2;
                if (__gflat_stdio_length(stream!) != 3l) return 3;
                if (__gflat_stdio_set_length(stream!, 10l) != 0 || __gflat_stdio_length(stream!) != 10l) return 4;
                if (__gflat_stdio_set_length(stream!, 2l) != 0 || __gflat_stdio_length(stream!) != 2l) return 5;
                return fclose(stream!);
            }
            """, options: new() { NoStdLib = true, NoBuiltins = true, InternalizeFunctions = true });
        Assert.Equal((false, true), NativeToolchain.RequiredNativeRuntimes(ir));
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PositionBridgeUsesFixedWidthOffsetsWithTheCOnlyHostProfile(int optimization)
    {
        string ir = Compiler.Emit("""
            extern void*? tmpfile() nothrow;
            extern int fclose(void* stream) nothrow;
            extern int __gflat_stdio_seek(void* stream, long offset, int origin) nothrow;
            extern long __gflat_stdio_tell(void* stream) nothrow;
            int main() nothrow {
                void*? stream = tmpfile();
                if (stream == null) return 1;
                if (__gflat_stdio_seek(stream!, 4294967313l, 0) != 0) return 2;
                if (__gflat_stdio_tell(stream!) != 4294967313l) return 3;
                if (__gflat_stdio_seek(stream!, -1l, 1) != 0 || __gflat_stdio_tell(stream!) != 4294967312l) return 4;
                if (__gflat_stdio_seek(stream!, 0l, 2) != 0 || __gflat_stdio_tell(stream!) != 0l) return 5;
                return fclose(stream!);
            }
            """, options: new() { NoStdLib = true, NoBuiltins = true, InternalizeFunctions = true });
        Assert.Equal((false, true), NativeToolchain.RequiredNativeRuntimes(ir));
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    [InlineData(2, true)]
    public void StreamBridgeWorksWithCOnlyOrMatchedExceptionHostProfile(int optimization, bool exceptions)
    {
        string text = """
            extern void* __gflat_stdio_stdin() nothrow;
            extern void* __gflat_stdio_stdout() nothrow;
            extern void* __gflat_stdio_stderr() nothrow;
            extern int fputs(readonly(char)* text, void* stream) nothrow;
            extern int fflush(void* stream) nothrow;
            extern int ferror(void* stream) nothrow;
            double Remainder(double left, double right) nothrow => left % right;

            int main(int argc, char** argv) nothrow
            {
                void* input = __gflat_stdio_stdin();
                void* output = __gflat_stdio_stdout();
                void* error = __gflat_stdio_stderr();
                if (ferror(input) != 0) return 1;
                if (Remainder((double)argc + 4.5, 2.0) != 1.5) return 2;
                if (fputs("c-stdio\n", output) < 0 || fflush(output) != 0) return 3;
                if (fputs("c-stdio-error\n", error) < 0 || fflush(error) != 0) return 4;
                // MIXED_EXCEPTION_PROBE
                return 0;
            }
            """;
        if (exceptions)
        {
            text = text.Replace("// MIXED_EXCEPTION_PROBE", """
                try { throw new Exception("stdio-mixed", 7); }
                catch (ref Exception failure)
                {
                    if (failure.GetCode() != 7) return 5;
                }
                """, StringComparison.Ordinal);
        }

        var sources = new List<SourceFile>();
        if (exceptions)
        {
            foreach (string relative in new[] {
                "core/Exception.gf",
                "libc/Memory/allocation.gf",
                "libc/Exceptions/unhandled.gf"
            })
            {
                string path = Path.Combine(AppContext.BaseDirectory, "Standard", relative);
                sources.Add(new(path, File.ReadAllText(path)));
            }
        }
        sources.Add(new(Path.GetFullPath("native-stdio-c-profile.gf"), text));
        string ir = Compiler.Emit(sources, options: new() {
            NoStdLib = true,
            NoBuiltins = true,
            InternalizeFunctions = true
        });
        Assert.Equal((exceptions, true), NativeToolchain.RequiredNativeRuntimes(ir));

        var result = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
        Assert.Equal("c-stdio\n", result.StandardOutput.Replace("\r\n", "\n", StringComparison.Ordinal));
        Assert.Equal("c-stdio-error\n", result.StandardError.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    [Fact]
    public void CObjectLanguageDoesNotChangeWhenUsingTheExceptionHostProfile()
    {
        string[] host = ["-isystem", "matched header path", "-Xlinker", "/libpath:matched library path"];
        var c = NativeRuntime.ObjectArguments("Stdio.c", "stdio with spaces.o", false, [], host);
        Assert.Contains("--driver-mode=gcc", c);
        Assert.Contains("-std=c11", c);
        Assert.DoesNotContain(c, value => value.Contains("g++", StringComparison.Ordinal)
            || value.Contains("c++", StringComparison.Ordinal) || value == "-fexceptions");
        Assert.Equal(host, c.TakeLast(host.Length));
        Assert.Contains("stdio with spaces.o", c);

        var cpp = NativeRuntime.ObjectArguments("Exceptions.cpp", "eh.o", true,
            ["-DGFLAT_EXCEPTION_RESERVE_BYTES=65536"], host);
        Assert.Contains("--driver-mode=g++", cpp);
        Assert.Contains("-std=c++17", cpp);
        Assert.Contains("-fexceptions", cpp);
        Assert.Contains("-DGFLAT_EXCEPTION_RESERVE_BYTES=65536", cpp);
        Assert.Equal(host, cpp.TakeLast(host.Length));
    }

    [Fact]
    public void ObjectCacheSeparatesCAndCppHostProfilesAndSourceAndDriverIdentity()
    {
        string c = NativeRuntime.ObjectCacheKey("clang-18", "Stdio", "abc", false, false, []);
        Assert.Equal(c, NativeRuntime.ObjectCacheKey("clang-18", "Stdio", "abc", false, false, []));
        Assert.NotEqual(c, NativeRuntime.ObjectCacheKey("clang-18", "Stdio", "abc", false, true, []));
        Assert.NotEqual(c, NativeRuntime.ObjectCacheKey("clang-18", "Stdio", "abc", true, true, []));
        Assert.NotEqual(c, NativeRuntime.ObjectCacheKey("clang-19", "Stdio", "abc", false, false, []));
        Assert.NotEqual(c, NativeRuntime.ObjectCacheKey("clang-18", "Stdio", "def", false, false, []));
        Assert.NotEqual(c, NativeRuntime.ObjectCacheKey("clang-18", "Stdio", "abc", false, false, ["-DPROFILE=1"]));
    }
}
