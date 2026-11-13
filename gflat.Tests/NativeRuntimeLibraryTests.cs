using gflat.Projects;

namespace gflat.Tests;

public sealed class NativeRuntimeLibraryTests
{
    [Theory]
    [InlineData("MultiThreaded", 0)]
    [InlineData("MultiThreadedDLL", 1)]
    [InlineData("MultiThreadedDebug", 2)]
    [InlineData("MultiThreadedDebugDLL", 3)]
    public void ForeignSourceAndExceptionRuntimeUseTheSelectedCrt(string runtime, int expected)
    {
        if (!OperatingSystem.IsWindows()) return;
        string directory = Path.Combine(Path.GetTempPath(), "gflat-crt-profile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string source = Path.Combine(directory, "source.gf"), executable = Path.Combine(directory, "program.exe");
            File.WriteAllText(source, $$"""
                #lang cpp
                #include <stdexcept>
                int NativeMode()
                {
                    std::runtime_error error("message");
                    if (error.what()[0] != 'm') return -1;
                    int mode = 0;
                #ifdef _DLL
                    mode += 1;
                #endif
                #ifdef _DEBUG
                    mode += 2;
                #endif
                    return mode;
                }
                #lang gflat
                int main()
                {
                    if (NativeMode() != {{expected}}) return 1;
                    return 42;
                }
                """);
            Assert.Equal(0, CompilerProgram.Main([source, "--no-stdlib", "--runtime-library", runtime, "-o", executable]));
            Assert.Equal(42, NativeToolchain.Run(executable, []).ExitCode);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void UnknownRuntimeProfilesAreRejected()
        => Assert.Throws<ArgumentException>(() => RuntimeLibrary.Parse("unknown"));
}
