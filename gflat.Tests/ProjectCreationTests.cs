using gflat.Projects;

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
            var graph = ProjectGraph.Load(path, p => ProjectModel.Load(p));
            Assert.Equal(library ? ProjectKind.SourceLibrary : ProjectKind.Executable, graph.Root.Kind);
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
    public void ImportsEscapeMsBuildCharactersInToolchainLocation()
    {
        string root = TemporaryDirectory();
        string toolchain = Path.Combine(root, "tools $literal;percent%");
        string app = Path.Combine(root, "app");
        Directory.CreateDirectory(Path.Combine(toolchain, "build"));
        try
        {
            string checkout = ProjectCreation.FindToolchainRoot();
            foreach (string file in new[] { "Gflat.props", "Gflat.targets" })
                File.Copy(Path.Combine(checkout, "build", file), Path.Combine(toolchain, "build", file));
            string path = ProjectCreation.Create(app, useStd: false, toolchainRoot: toolchain);
            var project = ProjectModel.Load(path);
            Assert.Equal(Path.GetFullPath(toolchain), project.SdkRoot);
            Assert.Single(project.SourceFiles());
            Assert.Equal(Path.Combine(app, "main.gf"), project.SourceFiles()[0]);
            Assert.Equal(0, Program.Main(["build", path, "--emit-ir"]));
        }
        finally { Directory.Delete(root, true); }
    }
}
