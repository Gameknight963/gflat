namespace gflat.Tests;

[Collection("Project SDK environment")]
public sealed class NativeToolchainDiscoveryTests
{
    [Fact]
    public void NamedEnvironmentCompilerResolvesToTheExecutableUsedForBuilds()
    {
        string driver = NativeToolchain.FindClang();
        string? previousPath = Environment.GetEnvironmentVariable("PATH");
        string? previousClang = Environment.GetEnvironmentVariable("GFLAT_CLANG");
        string projectDirectory = Path.Combine(Path.GetTempPath(), "gflat-named-driver-" + Guid.NewGuid().ToString("N"));
        try
        {
            Environment.SetEnvironmentVariable("PATH", Path.GetDirectoryName(driver) + Path.PathSeparator + previousPath);
            Environment.SetEnvironmentVariable("GFLAT_CLANG", Path.GetFileName(driver));
            string resolved = NativeToolchain.FindClang();
            Assert.Equal(Path.GetFullPath(driver), resolved);
            Assert.Equal(0, NativeToolchain.Run(resolved, ["--version"]).ExitCode);
            string project = ProjectCreation.Create(projectDirectory, "Probe", useStd: false);
            Assert.Equal(0, Program.Main(["build", project]));
            string output = Path.Combine(projectDirectory, "bin", "Debug", "Probe.exe");
            DateTime first = File.GetLastWriteTimeUtc(output);
            Assert.Equal(0, Program.Main(["build", project]));
            Assert.Equal(first, File.GetLastWriteTimeUtc(output));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", previousPath);
            Environment.SetEnvironmentVariable("GFLAT_CLANG", previousClang);
            if (Directory.Exists(projectDirectory)) Directory.Delete(projectDirectory, recursive: true);
        }
    }

    [Fact]
    public void MissingRequestedCompilerNeverFallsBackToTheDefault()
    {
        string name = "gflat-missing-clang-" + Guid.NewGuid().ToString("N");
        Assert.Equal(name, NativeToolchain.FindClang(name));
    }
}
