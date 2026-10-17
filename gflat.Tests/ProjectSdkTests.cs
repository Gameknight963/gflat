using gflat.Projects;
using System.Xml.Linq;

namespace gflat.Tests;

public sealed class ProjectSdkTests
{
    [Fact]
    public void ProviderPathsEscapeMsBuildExpressionsAndItemSeparators()
        => Assert.Equal("C:/sdk space %24%3B%25%40%27%28%29", ProjectSdk.EscapePropertyValue("C:/sdk space $;%@'()"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DiscoveryDoesNotRequireStandardLibrary(bool vsix)
    {
        string root = Temporary();
        try
        {
            string sdk = vsix ? Path.Combine(root, "Sdk") : root;
            CreateSdk(sdk);
            string compiler = Path.Combine(root, "Compiler"); Directory.CreateDirectory(compiler);
            Assert.Equal(sdk, ProjectSdk.FindRoot(compiler));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ExplicitImportsSupportToolchainPathsWithMsBuildSpecialCharacters()
    {
        string root = Temporary();
        try
        {
            string sdk = Path.Combine(root, "tools $literal;percent% & (test)"); CreateSdk(sdk);
            string project = ProjectCreation.Create(Path.Combine(root, "app"), useStd: false, toolchainRoot: sdk);
            var model = ProjectModel.Load(project);
            Assert.Equal(sdk, model.SdkRoot);
            Assert.Single(model.SourceFiles());
            Assert.Empty(model.References);
            Assert.Equal(0, Program.Main(["build", project, "--emit-ir"]));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void MissingBuildRulesAreActionableAndNeverInstalledDuringBuild()
    {
        string root = Temporary(); Directory.CreateDirectory(root);
        try
        {
            string project = Path.Combine(root, "broken.gfproj");
            File.WriteAllText(project, TestSupport.ProjectFileFixture.WithImports("<Project />", Path.Combine(root, "missing")));
            var error = Assert.Throws<ProjectException>(() => ProjectModel.Load(project));
            Assert.Contains("GflatSdkRoot", error.Message);
            Assert.DoesNotContain("sdk install", error.Message);
            Assert.Single(Directory.GetFiles(root));
            Assert.Empty(Directory.GetDirectories(root));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void NoStdLibCanBeOverriddenForProjectEvaluation()
    {
        string root = Temporary(); Directory.CreateDirectory(root);
        try
        {
            string project = Path.Combine(root, "app.gfproj");
            File.WriteAllText(project, TestSupport.ProjectFileFixture.WithImports("<Project />"));
            Assert.Contains(ProjectModel.Load(project).References, path => path.EndsWith("std.gfproj"));
            var bare = ProjectModel.Load(project, "Debug", "AnyCPU", noStdLib: true);
            Assert.True(bare.NoStdLib);
            Assert.Empty(bare.References);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ExplicitImportsWorkInStandaloneMsBuildWithoutRegistrationOrRewriting()
    {
        string root = Temporary();
        try
        {
            string sdk = Path.Combine(root, "distribution"); CreateSdk(sdk);
            string project = Path.Combine(root, "example.gfproj");
            string xml = TestSupport.ProjectFileFixture.WithImports("<Project><PropertyGroup><NoStdLib>true</NoStdLib></PropertyGroup></Project>", sdk);
            File.WriteAllText(project, xml);
            var start = new System.Diagnostics.ProcessStartInfo("dotnet") {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (string argument in new[] { "msbuild", project, "-nologo", "-getProperty:GflatSdkRoot" }) start.ArgumentList.Add(argument);
            start.Environment.Remove("MSBuildSDKsPath");
            using var process = System.Diagnostics.Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await process.WaitForExitAsync(timeout.Token);
            Assert.True(process.ExitCode == 0, await stderr);
            Assert.Equal(sdk, Path.GetFullPath((await stdout).Trim()));
            Assert.Equal(xml, File.ReadAllText(project));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task StandaloneMsBuildDefaultsToBuildInsteadOfOnlyEnumeratingSources()
    {
        string root = Temporary(); Directory.CreateDirectory(root);
        try
        {
            string project = Path.Combine(root, "app.gfproj");
            File.WriteAllText(project, TestSupport.ProjectFileFixture.Xml(content: """
                <Target Name="Built" AfterTargets="Build"><WriteLinesToFile File="built.txt" Lines="done" Overwrite="true" /></Target>
                """));
            File.WriteAllText(Path.Combine(root, "main.gf"), "int main() => 0;");
            var start = new System.Diagnostics.ProcessStartInfo("dotnet") {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (string argument in new[] { "msbuild", project, "-nologo", "-v:q", "-p:GflatEmitIR=true", "-p:GflatCompilerCommand=dotnet \"" + typeof(Program).Assembly.Location + "\"" }) start.ArgumentList.Add(argument);
            using var process = System.Diagnostics.Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await process.WaitForExitAsync(timeout.Token);
            Assert.True(process.ExitCode == 0, (await stdout) + (await stderr));
            Assert.True(File.Exists(Path.Combine(root, "built.txt")));
            Assert.True(File.Exists(Path.Combine(root, "bin/Debug/app.ll")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void InvalidStdSelectionIsRejectedRatherThanSilentlyIncludingTheLibrary()
    {
        string root = Temporary(); Directory.CreateDirectory(root);
        try
        {
            string project = Path.Combine(root, "bad.gfproj");
            File.WriteAllText(project, TestSupport.ProjectFileFixture.WithImports("<Project><PropertyGroup><NoStdLib>yes</NoStdLib></PropertyGroup></Project>"));
            Assert.Contains("NoStdLib must be true or false", Assert.Throws<ProjectException>(() => ProjectModel.Load(project)).Message);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("Executable", ProjectKind.Executable)]
    [InlineData("SourceLibrary", ProjectKind.SourceLibrary)]
    public void ExplicitOutputTypesSelectNativeBuildOrSourceChecking(string outputType, ProjectKind expected)
    {
        string root = Temporary(); Directory.CreateDirectory(root);
        try
        {
            string project = Path.Combine(root, "app.gfproj");
            File.WriteAllText(project, TestSupport.ProjectFileFixture.WithImports($"<Project><PropertyGroup><NoStdLib>true</NoStdLib><OutputType>{outputType}</OutputType></PropertyGroup></Project>"));
            File.WriteAllText(Path.Combine(root, "main.gf"), "int main() => 42;");
            Assert.Equal(expected, ProjectModel.Load(project).Kind);
            Assert.Equal(0, Program.Main(expected == ProjectKind.Executable ? ["build", project, "--emit-ir"] : ["build", project]));
            Assert.Equal(expected == ProjectKind.Executable, File.Exists(Path.Combine(root, "bin/Debug/app.ll")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("Exe")]
    [InlineData("Library")]
    [InlineData("SharedLibrary")]
    [InlineData("invalid")]
    public void UnsupportedOutputTypesFailBothEvaluationAndStandaloneMsBuild(string outputType)
    {
        string root = Temporary(); Directory.CreateDirectory(root);
        try
        {
            string project = Path.Combine(root, "bad.gfproj");
            File.WriteAllText(project, TestSupport.ProjectFileFixture.WithImports($"<Project><PropertyGroup><NoStdLib>true</NoStdLib><OutputType>{outputType}</OutputType></PropertyGroup></Project>"));
            Assert.Contains("OutputType must be Executable or SourceLibrary", Assert.Throws<ProjectException>(() => ProjectModel.Load(project)).Message);
            var result = NativeToolchain.Run("dotnet", ["msbuild", project, "-nologo", "-v:q", "-t:GflatGetSources"], 30000);
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("OutputType must be Executable or SourceLibrary", result.StandardOutput + result.StandardError);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void RemovedInstallationCommandDoesNotWriteFiles()
    {
        string root = Temporary();
        Assert.Equal(2, Program.Main(["sdk", "install", "--sdk-directory", root]));
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void DefaultSourceGlobsCanBeDisabledInsideAnImportProject()
    {
        string root = Temporary(); Directory.CreateDirectory(root);
        try
        {
            string project = Path.Combine(root, "app.gfproj");
            File.WriteAllText(project, TestSupport.ProjectFileFixture.WithImports("""
                <Project>
                  <PropertyGroup><NoStdLib>true</NoStdLib><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>
                  <ItemGroup><Compile Include="main.gf" /></ItemGroup>
                </Project>
                """));
            File.WriteAllText(Path.Combine(root, "main.gf"), "int main() => 0;");
            File.WriteAllText(Path.Combine(root, "scratch.gf"), "invalid source");
            Assert.Equal([Path.Combine(root, "main.gf")], ProjectModel.Load(project).SourceFiles());
            Assert.Equal(0, Program.Main(["check", project]));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void StandardDirectoryBuildSettingsApplyToEvaluationAndBuild()
    {
        string root = Temporary(); Directory.CreateDirectory(Path.Combine(root, "app"));
        try
        {
            string props = Path.Combine(root, "Directory.Build.props");
            string targets = Path.Combine(root, "Directory.Build.targets");
            File.WriteAllText(props, "<Project><PropertyGroup><NoStdLib>true</NoStdLib></PropertyGroup></Project>");
            File.WriteAllText(targets, """
                <Project><Target Name="SharedHook" BeforeTargets="Check"><WriteLinesToFile File="shared-hook.txt" Lines="done" Overwrite="true" /></Target></Project>
                """);
            string project = Path.Combine(root, "app/app.gfproj");
            File.WriteAllText(project, TestSupport.ProjectFileFixture.WithImports("<Project />"));
            File.WriteAllText(Path.Combine(root, "app/main.gf"), "class Attribute {} int main() => 0;");
            var model = ProjectModel.Load(project);
            Assert.True(model.NoStdLib);
            Assert.Empty(model.References);
            Assert.Contains(props, model.Imports);
            Assert.Contains(targets, model.Imports);
            Assert.Equal(0, Program.Main(["check", project]));
            Assert.True(File.Exists(Path.Combine(root, "app/shared-hook.txt")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ExplicitToolchainSelectionWinsOverBundledSdkDiscovery()
    {
        string root = Temporary();
        try
        {
            string extension = Path.Combine(root, "extension");
            string bundled = Path.Combine(extension, "Sdk"); CreateSdk(bundled);
            string selected = Path.Combine(root, "selected"); CreateSdk(selected);
            Assert.Equal(bundled, ProjectSdk.FindRoot(extension));
            Assert.Equal(selected, ProjectSdk.FindRoot(extension, selected));
            Assert.Throws<ArgumentException>(() => ProjectSdk.FindRoot(extension, Path.Combine(root, "missing")));
        }
        finally { Directory.Delete(root, true); }
    }

    private static void CreateSdk(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "build"));
        foreach (string name in new[] { "Gflat.props", "Gflat.targets" })
            File.Copy(Path.Combine(TestSupport.ProjectFileFixture.RepositoryRoot, "build", name), Path.Combine(root, "build", name));
    }
    private static string Temporary() => Path.Combine(Path.GetTempPath(), "gflat sdk " + Guid.NewGuid().ToString("N"));
}
