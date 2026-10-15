using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace gflat.Tests;

internal static class NativeTestEnvironment
{
    [DllImport("kernel32.dll")]
    private static extern uint GetErrorMode();

    [DllImport("kernel32.dll")]
    private static extern uint SetErrorMode(uint mode);

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
        }

        // Expected traps must terminate without waiting for Windows error UI.
        // Child processes inherit this test host's mode. Set it once before
        // parallel tests start, preserving existing flags.
        if (OperatingSystem.IsWindows()) SetErrorMode(GetErrorMode() | 0x0001u | 0x0002u);
    }
}

public sealed class NativeTestEnvironmentTests
{
    [Fact]
    public void CompiledChildInheritsNoninteractiveWindowsFaultHandling()
    {
        if (!OperatingSystem.IsWindows()) return;
        var result = CompilerTestHelper.Run("extern uint GetErrorMode(); int main() { return (int)(GetErrorMode() & 3u); }");
        Assert.Equal(3, result.ExitCode);
    }
}
