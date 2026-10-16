namespace gflat.Tests;

public sealed class IncrementalProjectBuildTests
{
    [Fact]
    public void UnchangedBuildSkipsCompilationButMissingOutputAndRebuildForceIt()
    {
        WithProject((directory, project) => {
            Assert.Equal(0, Build(project));
            string output = Output(directory), marker = Marker(directory);
            DateTime first = File.GetLastWriteTimeUtc(output), success = File.GetLastWriteTimeUtc(marker);
            Assert.Equal(0, Build(project));
            Assert.Equal(first, File.GetLastWriteTimeUtc(output));
            Assert.Equal(success, File.GetLastWriteTimeUtc(marker));
            Assert.Equal(0, ProjectBuild.Run(gflat.Projects.ProjectModel.Load(project), "Rebuild", "Debug", "AnyCPU", emitIr: true));
            Assert.NotEqual(success, File.GetLastWriteTimeUtc(marker));
            File.Delete(output);
            Assert.Equal(0, Build(project));
            Assert.True(File.Exists(output));
            Assert.Equal(0, Program.Main(["clean", project]));
            Assert.False(File.Exists(marker));
            Assert.Equal(0, Build(project));
        });
    }

    [Fact]
    public void AddedOldSourcesAndRemovedSourcesInvalidateTheInputManifest()
    {
        WithProject((directory, project) => {
            Assert.Equal(0, Build(project));
            string extra = Path.Combine(directory, "extra.gf");
            File.WriteAllText(extra, "int Added() => 42;");
            File.SetLastWriteTimeUtc(extra, DateTime.UtcNow.AddYears(-1));
            Assert.Equal(0, Build(project));
            Assert.Contains("gflat$Added", File.ReadAllText(Output(directory)));
            File.Delete(extra);
            Assert.Equal(0, Build(project));
            Assert.DoesNotContain("gflat$Added", File.ReadAllText(Output(directory)));
        });
    }

    [Fact]
    public void OptionsChangesAndFailedBuildsDoNotReuseSuccess()
    {
        WithProject((directory, project) => {
            Assert.Equal(0, Build(project));
            Assert.Equal(0, Program.Main(["build", project, "--emit-ir", "--optimization", "2", "--exception-reserve-bytes", "0"]));
            string settings = File.ReadAllText(Path.Combine(directory, "obj", "Debug", "app.build.settings"));
            Assert.Contains("Optimization=2", settings);
            Assert.Contains("Reserve=0", settings);
            File.WriteAllText(Path.Combine(directory, "main.gf"), "int main() { missing(); return 0; }");
            Assert.NotEqual(0, Build(project));
            Assert.False(File.Exists(Marker(directory)));
            File.WriteAllText(Path.Combine(directory, "main.gf"), "int main() => 0;");
            Assert.Equal(0, Build(project));
            Assert.True(File.Exists(Marker(directory)));
        });
    }

    [Fact]
    public void ReferencedSourcesAndTheirImportedRulesAreInputs()
    {
        WithProject((directory, project) => {
            string library = Path.Combine(directory, "lib"); Directory.CreateDirectory(library);
            string imported = Path.Combine(library, "rules.props");
            File.WriteAllText(imported, "<Project><PropertyGroup><RuleVersion>one</RuleVersion></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(library, "lib.gfproj"), TestSupport.ProjectFileFixture.Xml("SourceLibrary", "<Import Project=\"rules.props\" />"));
            File.WriteAllText(Path.Combine(library, "extra.gf"), "int LibraryAnswer() => 42;");
            File.WriteAllText(project, TestSupport.ProjectFileFixture.Xml(content: "<ItemGroup><Compile Remove=\"lib/**/*.gf\" /><ProjectReference Include=\"lib/lib.gfproj\" /></ItemGroup>"));
            Assert.Equal(0, Build(project));
            Assert.Contains(imported.Replace('\\', '/'), File.ReadAllText(Path.Combine(directory, "obj", "Debug", "app.build.settings")).Replace('\\', '/'));
            DateTime first = File.GetLastWriteTimeUtc(Marker(directory));
            File.WriteAllText(imported, "<Project><PropertyGroup><RuleVersion>two</RuleVersion></PropertyGroup></Project>");
            Assert.Equal(0, Build(project));
            Assert.NotEqual(first, File.GetLastWriteTimeUtc(Marker(directory)));
            File.Delete(Path.Combine(library, "extra.gf"));
            Assert.Equal(0, Build(project));
            Assert.DoesNotContain("gflat$LibraryAnswer", File.ReadAllText(Output(directory)));
        });
    }

    [Fact]
    public void NativeBuildSkipsAndDriverFailureClearsSuccess()
    {
        WithProject((directory, project) => {
            Assert.Equal(0, Program.Main(["build", project]));
            string output = Path.Combine(directory, "bin", "Debug", "app.exe");
            DateTime first = File.GetLastWriteTimeUtc(output);
            Assert.Equal(0, Program.Main(["build", project]));
            Assert.Equal(first, File.GetLastWriteTimeUtc(output));
            Assert.NotEqual(0, Program.Main(["build", project, "--clang", Path.Combine(directory, "missing-clang")]));
            Assert.False(File.Exists(Marker(directory)));
            Assert.Equal(0, Program.Main(["build", project]));
            Assert.True(File.Exists(Marker(directory)));
        });
    }

    [Fact]
    public void OutputPathChangesAndModeChangesInvalidateBuild()
    {
        WithProject((directory, project) => {
            Assert.Equal(0, Build(project));
            string alternate = Path.Combine(directory, "alternate.ll");
            Assert.Equal(0, Program.Main(["build", project, "--emit-ir", "-o", alternate]));
            Assert.True(File.Exists(alternate));
            Assert.Equal(0, Program.Main(["build", project]));
            Assert.True(File.Exists(Path.Combine(directory, "bin", "Debug", "app.exe")));
            Assert.Equal(0, Build(project));
            Assert.Contains("--emit-ir", File.ReadAllText(Path.Combine(directory, "obj", "Debug", "app.build.settings")));
        });
    }

    [Fact]
    public void ConfigurationsSharingAnOutputDoNotReuseAnotherConfigurationsArtifact()
    {
        WithProject((directory, project) => {
            File.WriteAllText(project, TestSupport.ProjectFileFixture.Xml(content: """
                <PropertyGroup><OutputPath>bin/shared/</OutputPath></PropertyGroup>
                <ItemGroup Condition="'$(Configuration)' == 'Debug'"><Compile Remove="release.gf" /></ItemGroup>
                <ItemGroup Condition="'$(Configuration)' == 'Release'"><Compile Remove="debug.gf" /></ItemGroup>
                """));
            File.WriteAllText(Path.Combine(directory, "main.gf"), "int main() => Answer();");
            File.WriteAllText(Path.Combine(directory, "debug.gf"), "int Answer() => 42;");
            File.WriteAllText(Path.Combine(directory, "release.gf"), "int Answer() => 7;");
            string output = Path.Combine(directory, "bin/shared/app.ll");
            Assert.Equal(0, Program.Main(["build", project, "--emit-ir", "--configuration", "Debug"]));
            Assert.Contains("ret i32 42", File.ReadAllText(output));
            Assert.Equal(0, Program.Main(["build", project, "--emit-ir", "--configuration", "Release"]));
            Assert.Contains("ret i32 7", File.ReadAllText(output));
            Assert.Equal(0, Program.Main(["build", project, "--emit-ir", "--configuration", "Debug"]));
            Assert.Contains("ret i32 42", File.ReadAllText(output));
        });
    }

    [Fact]
    public void ReplacedOutputNewerThanItsSuccessStampIsRebuilt()
    {
        WithProject((directory, project) => {
            Assert.Equal(0, Build(project));
            string output = Output(directory);
            File.WriteAllText(output, "externally replaced output");
            File.SetLastWriteTimeUtc(output, File.GetLastWriteTimeUtc(Marker(directory)).AddSeconds(1));
            Assert.Equal(0, Build(project));
            Assert.Contains("ret i32 0", File.ReadAllText(output));
        });
    }

    [Fact]
    public void GeneratedBuildFilesCannotOverwriteExplicitInputs()
    {
        WithProject((directory, project) => {
            File.WriteAllText(project, TestSupport.ProjectFileFixture.Xml(content: "<PropertyGroup><IntermediateOutputPath>.</IntermediateOutputPath></PropertyGroup><ItemGroup><Compile Include=\"app.sources\" /></ItemGroup>"));
            string source = Path.Combine(directory, "app.sources"); File.WriteAllText(source, "int Keep() => 1;");
            Assert.NotEqual(0, Build(project));
            Assert.Equal("int Keep() => 1;", File.ReadAllText(source));
            Assert.NotEqual(0, Program.Main(["clean", project]));
            Assert.True(File.Exists(source));
        });
    }

    private static int Build(string project) => Program.Main(["build", project, "--emit-ir"]);
    private static string Output(string directory) => Path.Combine(directory, "bin", "Debug", "app.ll");
    private static string Marker(string directory) => Path.Combine(directory, "obj", "Debug", "app.build.success");
    private static void WithProject(Action<string, string> action)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-incremental-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string project = Path.Combine(directory, "app.gfproj");
        File.WriteAllText(project, TestSupport.ProjectFileFixture.Xml());
        File.WriteAllText(Path.Combine(directory, "main.gf"), "int main() => 0;");
        try { action(directory, project); }
        finally { Directory.Delete(directory, true); }
    }
}
