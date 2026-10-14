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
