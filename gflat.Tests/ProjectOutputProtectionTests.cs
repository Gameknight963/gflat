using gflat.TestSupport;

namespace gflat.Tests;

public sealed class ProjectOutputProtectionTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "gflat_output_protection_" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CleanDeletesCustomIrOutputAndPreservesTheUnusedDefaultIrPath(bool imported)
    {
        Directory.CreateDirectory(Path.Combine(root, "bin/Debug"));
        string project = Path.Combine(root, "app.gfproj");
        string unusedDefault = Path.Combine(root, "bin/Debug/app.ll");
        string keep = imported ? "<Project><PropertyGroup><Keep>true</Keep></PropertyGroup></Project>" : "int Keep() => 1;";
        File.WriteAllText(unusedDefault, keep);
        File.WriteAllText(Path.Combine(root, "main.gf"), "int main() => 0;");
        File.WriteAllText(project, ProjectFileFixture.Xml(content: """
            <PropertyGroup><GflatIROutput>./generated/../custom.ll</GflatIROutput></PropertyGroup>
            """ + (imported ? "<Import Project=\"bin/Debug/app.ll\" />" : "<ItemGroup><Compile Include=\"bin/Debug/app.ll\" /></ItemGroup>")));
        string custom = Path.Combine(root, "custom.ll");
        foreach (var host in Hosts())
        {
            File.WriteAllText(custom, "generated IR");
            File.WriteAllText(Path.Combine(root, "bin/Debug/app.exe"), "generated executable");
            var clean = RunHost(host, project, "Clean");
            Assert.True(clean.ExitCode == 0, clean.StandardOutput + clean.StandardError);
            Assert.Equal(keep, File.ReadAllText(unusedDefault));
            Assert.False(File.Exists(custom));
            Assert.False(File.Exists(Path.Combine(root, "bin/Debug/app.exe")));
        }
        Assert.Equal(0, Program.Main(["clean", project]));
        Assert.Equal(keep, File.ReadAllText(unusedDefault));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CleanProtectsActualIrOutputEvenWhenConfiguredThroughAnAliasPath(bool imported)
    {
        Directory.CreateDirectory(root);
        string project = Path.Combine(root, "app.gfproj");
        string protectedFile = Path.Combine(root, "keep.gf");
        string keep = imported ? "<Project />" : "int Keep() => 1;";
        File.WriteAllText(protectedFile, keep);
        File.WriteAllText(Path.Combine(root, "main.gf"), "int main() => 0;");
        File.WriteAllText(project, ProjectFileFixture.Xml(content: """
            <PropertyGroup><GflatIROutput>./generated/../keep.gf</GflatIROutput></PropertyGroup>
            """ + (imported ? "<ItemGroup><Compile Remove=\"keep.gf\" /></ItemGroup><Import Project=\"keep.gf\" />" : "")));
        Assert.NotEqual(0, Program.Main(["clean", project]));
        Assert.Equal(keep, File.ReadAllText(protectedFile));
        foreach (var host in Hosts())
            foreach (string target in new[] { "Build", "Clean" })
                Assert.NotEqual(0, RunHost(host, project, target).ExitCode);
        Assert.Equal(keep, File.ReadAllText(protectedFile));
    }

    [Fact]
    public void DirectHostsProtectImportsOfUnopenedReferencedProjects()
    {
        Directory.CreateDirectory(Path.Combine(root, "library with spaces"));
        string imported = Path.Combine(root, "library with spaces/shared settings.props");
        const string keep = "<Project><PropertyGroup><SharedVersion>one</SharedVersion></PropertyGroup></Project>";
        File.WriteAllText(imported, keep);
        File.WriteAllText(Path.Combine(root, "library with spaces/library.gfproj"), ProjectFileFixture.Xml("SourceLibrary", "<Import Project=\"shared settings.props\" />"));
        string project = Path.Combine(root, "app.gfproj");
        File.WriteAllText(project, ProjectFileFixture.Xml(content: """
            <PropertyGroup><GflatIROutput>./library with spaces/../library with spaces/shared settings.props</GflatIROutput></PropertyGroup>
            <ItemGroup><ProjectReference Include="library with spaces/library.gfproj" /></ItemGroup>
            """));
        File.WriteAllText(Path.Combine(root, "main.gf"), "int main() => 0;");
        foreach (var host in Hosts())
        {
            var clean = RunHost(host, project, "Clean");
            Assert.NotEqual(0, clean.ExitCode);
            Assert.Contains("Output must differ from source, project, and imported build files", clean.StandardOutput + clean.StandardError);
            Assert.Equal(keep, File.ReadAllText(imported));
        }
    }

    [Fact]
    public void CliProtectsAnImportEnabledOnlyByTheIrBuildFlag()
    {
        Directory.CreateDirectory(Path.Combine(root, "bin/Debug"));
        string imported = Path.Combine(root, "bin/Debug/app.ll");
        const string keep = "<Project><PropertyGroup><Keep>true</Keep></PropertyGroup></Project>";
        File.WriteAllText(imported, keep);
        File.SetLastWriteTimeUtc(imported, DateTime.UtcNow.AddYears(-1));
        string project = Path.Combine(root, "app.gfproj");
        File.WriteAllText(project, ProjectFileFixture.Xml(content: """
            <Import Project="bin/Debug/app.ll" Condition="'$(GflatEmitIR)' == 'true'" />
            """));
        File.WriteAllText(Path.Combine(root, "main.gf"), "int main() => 0;");
        Assert.NotEqual(0, Program.Main(["build", project, "--emit-ir"]));
        Assert.Equal(keep, File.ReadAllText(imported));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConditionalReferenceImportsAreProtectedWithReferenceBuildProperties(bool cli)
    {
        Directory.CreateDirectory(Path.Combine(root, "library"));
        string imported = Path.Combine(root, "library/shared.props");
        const string keep = "<Project><PropertyGroup><Keep>true</Keep></PropertyGroup></Project>";
        File.WriteAllText(imported, keep);
        File.WriteAllText(Path.Combine(root, "library/library.gfproj"), ProjectFileFixture.Xml("SourceLibrary", """
            <Import Project="shared.props" Condition="'$(GflatAsReference)' == 'true'" />
            """));
        string project = Path.Combine(root, "app.gfproj");
        File.WriteAllText(project, ProjectFileFixture.Xml(content: """
            <PropertyGroup><GflatIROutput>library/shared.props</GflatIROutput></PropertyGroup>
            <ItemGroup><ProjectReference Include="library/library.gfproj" /></ItemGroup>
            """));
        File.WriteAllText(Path.Combine(root, "main.gf"), "int main() => 0;");
        if (cli) Assert.NotEqual(0, Program.Main(["clean", project]));
        else
            foreach (var host in Hosts()) Assert.NotEqual(0, RunHost(host, project, "Clean").ExitCode);
        Assert.Equal(keep, File.ReadAllText(imported));
    }

    private static IEnumerable<(string Driver, string[] Prefix)> Hosts()
    {
        yield return ("dotnet", ["msbuild"]);
        if (!OperatingSystem.IsWindows()) yield break;
        string installations = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft Visual Studio");
        if (!Directory.Exists(installations)) yield break;
        foreach (string version in Directory.GetDirectories(installations).OrderDescending())
            foreach (string edition in Directory.GetDirectories(version))
            {
                string native = Path.Combine(edition, "MSBuild/Current/Bin/MSBuild.exe");
                if (!File.Exists(native)) continue;
                yield return (native, []);
                yield break;
            }
    }

    private static ProcessResult RunHost((string Driver, string[] Prefix) host, string project, string target)
    {
        if (host.Driver == "dotnet")
            return NativeToolchain.Run(host.Driver, [.. host.Prefix, project, "-nologo", "-v:q", "-t:" + target], 30000);
        var info = new System.Diagnostics.ProcessStartInfo(host.Driver) {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
        };
        // Locator configures this .NET test host. Native VS MSBuild must use its
        // own framework assemblies/resolvers, retaining the isolated SDK search path.
        foreach (string property in new[] { "MSBUILD_EXE_PATH", "MSBuildExtensionsPath", "MSBUILDADDITIONALSDKRESOLVERSFOLDER" })
            info.Environment.Remove(property);
        foreach (string argument in new[] { project, "-nologo", "-v:q", "-t:" + target }) info.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(30000), "Native MSBuild timed out.");
        return new(process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
    }
}
