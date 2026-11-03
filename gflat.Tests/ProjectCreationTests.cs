using gflat.Projects;
using System.Xml.Linq;

namespace gflat.Tests;

public sealed class ProjectCreationTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public void GeneratedProjectLoadsAndCompiles(bool library, bool std)
    {
        string directory = TemporaryDirectory();
        try
        {
            string path = ProjectCreation.Create(directory, "My & Project", library, std);
            var graph = ProjectGraph.Load(path);
            Assert.Equal(library ? ProjectKind.SourceLibrary : ProjectKind.Executable, graph.Root.Kind);
            Assert.Equal(library ? "SourceLibrary" : "Executable",
                XElement.Load(path).Elements("PropertyGroup").Elements("OutputType").Single().Value);
            var sources = graph.Sources.Select(p => new SourceFile(p, File.ReadAllText(p)));
            string ir = Compiler.Emit(sources);
            Assert.NotEmpty(ir);
            Assert.True(File.Exists(Path.Combine(directory, ".gitignore")));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void CreationNeverOverwritesExistingFiles()
    {
        string directory = TemporaryDirectory();
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "main.gf");
        File.WriteAllText(file, "user source");
        try
        {
            Assert.Throws<ArgumentException>(() => ProjectCreation.Create(directory));
            Assert.Equal("user source", File.ReadAllText(file));
            Assert.Single(Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("$(Injected)")]
    [InlineData("a;b")]
    [InlineData("")]
    [InlineData("CON")]
    [InlineData("aux.txt")]
    [InlineData("LPT1")]
    [InlineData("bad.")]
    [InlineData("bad ")]
    [InlineData("bad|name")]
    public void RejectsInvalidNamesBeforeCreatingDirectory(string name)
    {
        string directory = TemporaryDirectory();
        Assert.Throws<ArgumentException>(() => ProjectCreation.Create(directory, name));
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void MissingDistributionDoesNotCreatePartialProject()
    {
        string directory = TemporaryDirectory();
        Assert.Throws<ArgumentException>(() => ProjectCreation.Create(directory, toolchainRoot: directory));
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void GeneratedMinimalProjectBuildsThroughMSBuild()
    {
        string directory = TemporaryDirectory();
        try
        {
            string path = ProjectCreation.Create(directory, useStd: false);
            Assert.Equal(0, Program.Main(["build", path, "--emit-ir"]));
            Assert.True(File.Exists(Path.Combine(directory, "bin", "Debug", Path.GetFileNameWithoutExtension(path) + ".ll")));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static string TemporaryDirectory() => Path.Combine(Path.GetTempPath(), "gflat new " + Guid.NewGuid().ToString("N"));

    [Fact]
    public void CliCreatesAndChecksStandardApplication()
    {
        string directory = TemporaryDirectory();
        try
        {
            Assert.Equal(0, Program.Main(["new", directory, "--name", "Hello"]));
            Assert.Equal(0, Program.Main(["check", Path.Combine(directory, "Hello.gfproj")]));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void CliHelpAndInvalidArgumentsDoNotCreateProjects()
    {
        string directory = TemporaryDirectory();
        Assert.Equal(0, Program.Main(["new", "--help"]));
        Assert.Equal(2, Program.Main(["new", directory, "--name"]));
        Assert.Equal(2, Program.Main(["new", directory, "--unknown"]));
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void GeneratedProjectsUseSdkWithoutInstallationPaths()
    {
        string directory = TemporaryDirectory();
        try
        {
            string path = ProjectCreation.Create(directory, useStd: false);
            var xml = System.Xml.Linq.XElement.Load(path);
            Assert.Equal("Gflat.Sdk", (string?)xml.Attribute("Sdk"));
            Assert.Empty(xml.Elements("Import"));
            Assert.Empty(xml.Elements("PropertyGroup").Elements("GflatSdkRoot"));
            Assert.True(ProjectModel.Load(path).NoStdLib);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void RelativeToolchainImportsCanContainNestedRootPathSegments()
    {
        string directory = TemporaryDirectory();
        try
        {
            string toolchain = Path.Combine(directory, "sdk", "toolchain");
            Directory.CreateDirectory(Path.Combine(toolchain, "build"));
            File.WriteAllText(Path.Combine(toolchain, "build", "Gflat.props"), "<Project />");
            File.WriteAllText(Path.Combine(toolchain, "build", "Gflat.targets"), "<Project />");
            string projectDirectory = Path.Combine(directory, "apps", "nested", "example");
            string path = ProjectCreation.Create(projectDirectory, "Example", useStd: false, toolchainRoot: toolchain);
            string root = XElement.Load(path).Elements("PropertyGroup").Elements("GflatSdkRoot").Single().Value;
            // A relative path legitimately contains rooted-looking substrings.
            // On Linux this also happens when ../../home/... contains /home/....
            Assert.Contains("/sdk/toolchain", root);
            Assert.False(Path.IsPathRooted(root));
            AssertToolchainImportsResolve(path, toolchain);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static void AssertToolchainImportsResolve(string project, string toolchain)
    {
        var xml = XElement.Load(project);
        string directory = Path.GetDirectoryName(project)!;
        string root = xml.Elements("PropertyGroup").Elements("GflatSdkRoot")
            .Single(property => property.Value != "$(GflatDefaultSdkRoot)").Value;
        string expected = Path.GetRelativePath(directory, toolchain).Replace('\\', '/');
        Assert.Equal(ProjectSdk.EscapePropertyValue(expected), root);
        string decodedRoot = Uri.UnescapeDataString(root);
        Assert.Equal(Path.GetFullPath(toolchain), Path.GetFullPath(decodedRoot, directory));
        foreach (var import in xml.Elements("Import"))
        {
            Assert.Equal("Gflat.Sdk", (string?)import.Attribute("Sdk"));
            Assert.Contains((string?)import.Attribute("Project"), new[] { "Sdk.props", "Sdk.targets" });
        }
    }

    [Fact]
    public void RunningCompilerLocatesBuildRulesForAMovedProject()
    {
        string root = TemporaryDirectory();
        try
        {
            string original = Path.Combine(root, "original", "nested", "app");
            ProjectCreation.Create(original, "App", useStd: false);
            string moved = Path.Combine(root, "moved");
            Directory.Move(original, moved);
            string project = Path.Combine(moved, "App.gfproj");
            string before = File.ReadAllText(project);
            Assert.Equal(0, Program.Main(["build", project, "--emit-ir"]));
            Assert.True(File.Exists(Path.Combine(moved, "bin/Debug/App.ll")));
            Assert.Equal(before, File.ReadAllText(project));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task SdkProjectWorksInStandaloneMsBuildWithoutAStoredInstallationPath()
    {
        string directory = TemporaryDirectory();
        try
        {
            string path = ProjectCreation.Create(directory, "Template", useStd: false);
            Assert.Equal(0, Program.Main(["check", path]));
            var start = new System.Diagnostics.ProcessStartInfo("dotnet") {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (string argument in new[] { "msbuild", path, "-t:Check", "-nologo", "-v:q",
                "-p:GflatDefaultSdkRoot=" + ProjectCreation.FindToolchainRoot(),
                "-p:GflatCompilerCommand=dotnet \"" + typeof(Program).Assembly.Location + "\"" })
                start.ArgumentList.Add(argument);
            start.Environment["MSBuildSDKsPath"] = Path.Combine(AppContext.BaseDirectory, "Sdks");
            using var process = System.Diagnostics.Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                throw new TimeoutException("SDK project evaluation timed out");
            }
            Assert.True(process.ExitCode == 0, (await stdout) + (await stderr));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
