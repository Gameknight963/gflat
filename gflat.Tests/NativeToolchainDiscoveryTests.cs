namespace gflat.Tests;

[Collection("Project SDK environment")]
public sealed class NativeToolchainDiscoveryTests
{
    [Theory]
    [InlineData("14.9.99999", "14.44.35207")]
    [InlineData("14.44.9", "14.44.35207")]
    public void MsvcLibrariesUseNumericVersionOrder(string older, string newer)
    {
        WithInstallations(root =>
        {
            string installation = Path.Combine(root, "Community");
            AddRuntime(installation, older);
            string expected = AddRuntime(installation, newer);
            Assert.Equal(expected, NativeToolchain.FindMsvcLibraryDirectory([installation]));
        });
    }

    [Fact]
    public void MsvcLibrariesChooseVersionAcrossEditionsAndSkipIncompleteToolsets()
    {
        WithInstallations(root =>
        {
            string enterprise = Path.Combine(root, "Enterprise");
            string community = Path.Combine(root, "Community");
            AddRuntime(enterprise, "14.30.30705");
            string expected = AddRuntime(community, "14.44.35207");
            AddRuntime(enterprise, "not-a-toolset-version");
            Directory.CreateDirectory(Path.Combine(enterprise, "VC", "Tools", "MSVC", "99.0.0", "lib", "x64"));
            Assert.Equal(expected, NativeToolchain.FindMsvcLibraryDirectory([enterprise, community]));
            Assert.Equal(expected, NativeToolchain.FindMsvcLibraryDirectory([community, enterprise]));
        });
    }

    [Fact]
    public void MsvcLibrariesReturnNoFallbackWhenNoRuntimeExists()
    {
        WithInstallations(root =>
        {
            Directory.CreateDirectory(Path.Combine(root, "VC", "Tools", "MSVC", "14.44.35207"));
            Assert.Null(NativeToolchain.FindMsvcLibraryDirectory([root, Path.Combine(root, "missing")]));
            Assert.Null(NativeToolchain.FindMsvcLibraryDirectory([]));
        });
    }

    private static string AddRuntime(string installation, string version)
    {
        string directory = Path.Combine(installation, "VC", "Tools", "MSVC", version, "lib", "x64");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "libcmt.lib"), "discovery fixture only");
        return directory;
    }

    private static void WithInstallations(Action<string> test)
    {
        string root = Path.Combine(Path.GetTempPath(), "gflat-msvc-discovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { test(root); }
        catch (Exception error)
        {
            error.Data["MsvcDiscoveryDirectory"] = root;
            throw;
        }
        Directory.Delete(root, recursive: true);
    }

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
