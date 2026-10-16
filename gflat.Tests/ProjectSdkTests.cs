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
    public void ExplicitInstallationEscapesPathsAndUsesNativeSdkEntrypoints()
    {
        string root = Temporary();
        try
        {
            string sdk = Path.Combine(root, "tools $literal;percent%"); CreateSdk(sdk);
            string host = Path.Combine(root, "host SDKs");
            string installed = ProjectSdk.Install(sdk, host);
            var props = XElement.Load(Path.Combine(installed, "Sdk.props"));
            Assert.Equal(ProjectSdk.EscapePropertyValue(sdk.Replace('\\', '/')), props.Element("PropertyGroup")!.Element("GflatSdkRoot")!.Value);
            Assert.True(File.Exists(Path.Combine(installed, "Sdk.targets")));
            string app = Path.Combine(root, "app"); Directory.CreateDirectory(app);
            string project = Path.Combine(app, "app.gfproj");
            // Explicit imports here test installed entrypoint contents without
            // modifying the SDK resolution settings of parallel test hosts.
            File.WriteAllText(project, new XElement("Project",
                new XElement("Import", new XAttribute("Project", Path.Combine(installed, "Sdk.props"))),
                new XElement("PropertyGroup", new XElement("NoStdLib", "true")),
                new XElement("Import", new XAttribute("Project", Path.Combine(installed, "Sdk.targets")))).ToString());
            File.WriteAllText(Path.Combine(app, "main.gf"), "int main() => 0;");
            var model = ProjectModel.Load(project);
            Assert.Equal(sdk, model.SdkRoot);
            Assert.Single(model.SourceFiles());
            Assert.Empty(model.References);
            Assert.Equal(0, Program.Main(["build", project, "--emit-ir"]));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void MissingSdkInstallationIsActionableAndNeverInstallsDuringBuild()
    {
        string root = Temporary(); Directory.CreateDirectory(root);
        try
        {
            string project = Path.Combine(root, "broken.gfproj");
            File.WriteAllText(project, "<Project Sdk=\"Gflat.Missing.Sdk\" />");
            Assert.Throws<ProjectException>(() => ProjectModel.Load(project));
            Assert.Single(Directory.GetFiles(root));
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
            File.WriteAllText(project, "<Project Sdk=\"Gflat.Sdk\" />");
            Assert.Contains(ProjectModel.Load(project).References, path => path.EndsWith("std.gfproj"));
            var bare = ProjectModel.Load(project, "Debug", "AnyCPU", noStdLib: true);
            Assert.True(bare.NoStdLib);
            Assert.Empty(bare.References);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task InstalledSdkResolvesThroughStandaloneMsBuildWithoutProjectRewriting()
    {
        string root = Temporary();
        try
        {
            string sdk = Path.Combine(root, "distribution"); CreateSdk(sdk);
            string host = Path.Combine(root, "host SDKs");
            ProjectSdk.Install(sdk, host);
            string project = Path.Combine(root, "example.gfproj");
            const string xml = "<Project Sdk=\"Gflat.Sdk\"><PropertyGroup><NoStdLib>true</NoStdLib></PropertyGroup></Project>";
            File.WriteAllText(project, xml);
            var start = new System.Diagnostics.ProcessStartInfo("dotnet") {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (string argument in new[] { "msbuild", project, "-nologo", "-getProperty:GflatSdkRoot" }) start.ArgumentList.Add(argument);
            start.Environment["MSBuildSDKsPath"] = host;
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
            File.WriteAllText(project, "<Project Sdk=\"Gflat.Sdk\"><PropertyGroup><NoStdLib>yes</NoStdLib></PropertyGroup></Project>");
            Assert.Contains("NoStdLib must be true or false", Assert.Throws<ProjectException>(() => ProjectModel.Load(project)).Message);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void CliInstallationIsAnExplicitSetupCommand()
    {
        string root = Temporary();
        try
        {
            Assert.Equal(0, Program.Main(["sdk", "install", "--help"]));
            Assert.False(Directory.Exists(root));
            Assert.Equal(0, Program.Main(["sdk", "install", "--sdk-directory", root]));
            Assert.True(File.Exists(Path.Combine(root, "Gflat.Sdk/Sdk/Sdk.props")));
            Assert.True(File.Exists(Path.Combine(root, "Gflat.Sdk/Sdk/Sdk.targets")));
            Assert.Equal(2, Program.Main(["sdk", "install", "--sdk-directory"]));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void DefaultSourceGlobsCanBeDisabledInsideAnSdkProject()
    {
        string root = Temporary(); Directory.CreateDirectory(root);
        try
        {
            string project = Path.Combine(root, "app.gfproj");
            File.WriteAllText(project, """
                <Project Sdk="Gflat.Sdk">
                  <PropertyGroup><NoStdLib>true</NoStdLib><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>
                  <ItemGroup><Compile Include="main.gf" /></ItemGroup>
                </Project>
                """);
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
            File.WriteAllText(project, "<Project Sdk=\"Gflat.Sdk\" />");
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

    private static void CreateSdk(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "build"));
        foreach (string name in new[] { "Gflat.props", "Gflat.targets" })
            File.Copy(Path.Combine(TestSupport.ProjectFileFixture.RepositoryRoot, "build", name), Path.Combine(root, "build", name));
    }
    private static string Temporary() => Path.Combine(Path.GetTempPath(), "gflat sdk " + Guid.NewGuid().ToString("N"));
}
