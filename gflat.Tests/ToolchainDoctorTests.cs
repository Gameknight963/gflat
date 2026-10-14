namespace gflat.Tests;

public sealed class ToolchainDoctorTests
{
    [Fact]
    public void NativeProbeExercisesInstalledCompilerAndExceptionRuntime()
        => Assert.Equal(0, Program.Main(["doctor", "--native"]));

    [Fact]
    public void MissingNativeDriverReportsSetupFailure()
    {
        string missing = Path.Combine(Path.GetTempPath(), "gflat-missing-driver-" + Guid.NewGuid().ToString("N"));
        Assert.Equal(2, Program.Main(["doctor", "--clang", missing]));
    }
}
