using System.Runtime.CompilerServices;

namespace gflat.Tests;

internal static class NativeTestEnvironment
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        string? outputRoot = Environment.GetEnvironmentVariable("GFLAT_TEST_OUTPUT_ROOT");
        if (OperatingSystem.IsWindows() && string.IsNullOrWhiteSpace(outputRoot))
            outputRoot = Environment.GetEnvironmentVariable("GFLAT_TEST_OUTPUT_ROOT", EnvironmentVariableTarget.User);
        if (!string.IsNullOrWhiteSpace(outputRoot))
        {
            string path = Path.GetFullPath(outputRoot);
            Directory.CreateDirectory(path);
            Environment.SetEnvironmentVariable("TEMP", path);
            Environment.SetEnvironmentVariable("TMP", path);
            Environment.SetEnvironmentVariable("TMPDIR", path);
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GFLAT_RUNTIME_CACHE_DIRECTORY")))
                Environment.SetEnvironmentVariable("GFLAT_RUNTIME_CACHE_DIRECTORY", Path.Combine(path, "native-runtime-cache"));
        }

        NativeTestFaultHandling.InitializeHost();
    }
}

public sealed class NativeTestEnvironmentTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void UnhandledWindowsFaultPreservesItsStatusWithoutErrorReporting(int optimization)
    {
        if (!OperatingSystem.IsWindows()) return;
        var result = CompilerTestHelper.Run("""
            extern "C" bool __gflat_test_fault_handling_ready();
            extern "C" void RaiseException(uint code, uint flags, uint count, void*? arguments) nothrow;
            int main()
            {
                if (!__gflat_test_fault_handling_ready()) return 1;
                RaiseException(0xE0000042u, 1u, 0u, null);
                return 2;
            }
            """, optimization);
        Assert.Equal(unchecked((int)0xE0000042), result.ExitCode);
    }

    [Fact]
    public void GeneratedProgramInstallsFaultHandlingBeforeItsGlobalInitializers()
    {
        if (!OperatingSystem.IsWindows()) return;
        var result = CompilerTestHelper.Run("""
            extern "C" bool __gflat_test_fault_handling_ready();
            bool readyAtStartup = __gflat_test_fault_handling_ready();
            int main()
            {
                if (!readyAtStartup) return 1;
                if (!__gflat_test_fault_handling_ready()) return 2;
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void CompiledChildUsesNoninteractiveWindowsFaultHandling()
    {
        if (!OperatingSystem.IsWindows()) return;
        var result = CompilerTestHelper.Run("extern \"C\" uint GetErrorMode(); int main() { return (int)(GetErrorMode() & 3u); }");
        Assert.Equal(3, result.ExitCode);
    }
}
