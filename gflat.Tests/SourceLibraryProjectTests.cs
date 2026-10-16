using System.Text.Json;
using gflat.Projects;
using gflat.TestSupport;

namespace gflat.Tests;

public sealed class SourceLibraryProjectTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "gflat_source_library_" + Guid.NewGuid().ToString("N"));

    private string Project(string content = "")
    {
        Directory.CreateDirectory(root);
        string project = Path.Combine(root, "library.gfproj");
        File.WriteAllText(project, ProjectFileFixture.Xml("SourceLibrary", content));
        File.WriteAllText(Path.Combine(root, "value.gf"), "struct Value { public int number; }");
        return project;
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [Theory]
    [InlineData(null)]
    [InlineData("value.gf")]
    [InlineData("library.gfproj")]
    [InlineData("user.exe")]
    [InlineData("unused-output/ignored.dat")]
    public void SourceLibrariesIgnoreArtifactSettingsAndCleanOnlyTheirIntermediates(string? target)
    {
        string project = Project($"<PropertyGroup><OutputPath>unused-output/</OutputPath>{(target == null ? "" : $"<TargetPath>{target}</TargetPath>")}</PropertyGroup>");
        string source = File.ReadAllText(Path.Combine(root, "value.gf"));
        string xml = File.ReadAllText(project);
        string keep = Path.Combine(root, "user.exe");
        File.WriteAllText(keep, "unrelated user file");
        Assert.Null(ProjectModel.Load(project).TargetPath);
        Assert.Equal(0, Program.Main(["build", project]));
        Assert.Equal(0, Program.Main(["check", project]));
        string manifest = Path.Combine(root, "obj/Debug/library.sources");
        Assert.True(File.Exists(manifest));
        Assert.False(Directory.Exists(Path.Combine(root, "unused-output")));
        Assert.False(Directory.Exists(Path.Combine(root, "bin")));
        Assert.Equal(0, Program.Main(["clean", project]));
        Assert.False(File.Exists(manifest));
        Assert.Equal(source, File.ReadAllText(Path.Combine(root, "value.gf")));
        Assert.Equal(xml, File.ReadAllText(project));
        Assert.Equal("unrelated user file", File.ReadAllText(keep));
    }

    [Fact]
    public void SourceLibraryTargetsReturnNoArtifactAndRejectDirectIrRequests()
    {
        string project = Project();
        string compiler = "dotnet \"" + typeof(Program).Assembly.Location + "\"";
        var built = NativeToolchain.Run("dotnet", ["msbuild", project, "-nologo", "-getTargetResult:Build,GetTargetPath", "-p:GflatCompilerCommand=" + compiler], 30000);
        Assert.Equal(0, built.ExitCode);
        using var json = JsonDocument.Parse(built.StandardOutput);
        foreach (string target in new[] { "Build", "GetTargetPath" })
            Assert.Empty(json.RootElement.GetProperty("TargetResults").GetProperty(target).GetProperty("Items").EnumerateArray());
        var ir = NativeToolchain.Run("dotnet", ["msbuild", project, "-nologo", "-v:q", "-t:Build", "-p:GflatEmitIR=true"], 30000);
        Assert.NotEqual(0, ir.ExitCode);
        Assert.Contains("SourceLibrary projects have no standalone output", ir.StandardOutput + ir.StandardError);
    }

    [Fact]
    public void EmptySourceLibraryDoesNotNeedASourceOrArtifactPath()
    {
        string project = Project("<PropertyGroup><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>");
        var model = ProjectModel.Load(project);
        Assert.Null(model.TargetPath);
        Assert.Empty(model.SourceFiles());
        foreach (string command in new[] { "build", "check", "clean" })
            Assert.Equal(0, Program.Main([command, project]));
        Assert.False(Directory.Exists(Path.Combine(root, "bin")));
    }

    [Theory]
    [InlineData("--run")]
    [InlineData("--emit-ir")]
    [InlineData("-o")]
    public void SourceLibraryCliRejectsExecutableOnlyOutputRequests(string option)
    {
        string project = Project();
        Assert.Equal(2, Program.Main(option == "-o" ? ["build", project, option, Path.Combine(root, "output.exe")] : ["build", project, option]));
        Assert.False(Directory.Exists(Path.Combine(root, "obj")));
        Assert.False(File.Exists(Path.Combine(root, "output.exe")));
    }

    [Theory]
    [InlineData("library.sources")]
    [InlineData("library.build.settings")]
    [InlineData("library.build.success")]
    public void SourceLibraryIntermediatesCannotOverwriteOrCleanAnExplicitSource(string generatedName)
    {
        string project = Project($"<PropertyGroup><IntermediateOutputPath>./</IntermediateOutputPath></PropertyGroup><ItemGroup><Compile Include=\"{generatedName}\" /></ItemGroup>");
        string protectedSource = Path.Combine(root, generatedName);
        const string source = "struct Protected {}";
        File.WriteAllText(protectedSource, source);
        Assert.NotEqual(0, Program.Main(["build", project]));
        Assert.NotEqual(0, Program.Main(["clean", project]));
        Assert.Equal(source, File.ReadAllText(protectedSource));
    }
}
