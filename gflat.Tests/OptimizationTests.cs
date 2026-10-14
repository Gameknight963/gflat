using gflat.Projects;

namespace gflat.Tests;

public class OptimizationTests
{
    [Theory]
    [InlineData("Debug", 0)]
    [InlineData("Release", 2)]
    public void ConfigurationSelectsOptimizationAndExplicitOverrideWins(string configuration, int expected)
    {
        string dir = Path.Combine(Path.GetTempPath(), "gflat-opt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "app.gfproj");
            File.WriteAllText(path, TestSupport.ProjectFileFixture.Xml());
            Assert.Equal(expected, ProjectModel.Load(path, configuration, "AnyCPU").OptimizationLevel);
            Assert.Equal(3, ProjectModel.Load(path, configuration, "AnyCPU", optimizationLevel: 3).OptimizationLevel);
            File.WriteAllText(path, TestSupport.ProjectFileFixture.Xml(content: "<PropertyGroup><GflatOptimizationLevel>1</GflatOptimizationLevel></PropertyGroup>"));
            Assert.Equal(1, ProjectModel.Load(path, configuration, "AnyCPU").OptimizationLevel);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void CliOverrideReachesMSBuildTargets()
    {
        string dir = Path.Combine(Path.GetTempPath(), "gflat opt " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "app.gfproj");
            File.WriteAllText(path, TestSupport.ProjectFileFixture.Xml(content: """
                <Target Name="RecordOptimization" AfterTargets="Build">
                    <WriteLinesToFile File="optimization.txt" Lines="$(GflatOptimizationLevel)" Overwrite="true" />
                </Target>
                """));
            File.WriteAllText(Path.Combine(dir, "main.gf"), "int main() => 0;");
            Assert.Equal(0, Program.Main(["build", path, "--configuration", "Release", "--optimization", "1", "--emit-ir"]));
            Assert.Equal("1", File.ReadAllText(Path.Combine(dir, "optimization.txt")).Trim());
            Assert.Equal(2, Program.Main(["build", path, "--optimization", "9"]));
            File.WriteAllText(path, TestSupport.ProjectFileFixture.Xml(content: "<PropertyGroup><GflatOptimizationLevel>9</GflatOptimizationLevel></PropertyGroup>"));
            Assert.Throws<ProjectException>(() => ProjectModel.Load(path));
            Assert.Equal(2, Program.Main(["check", path]));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NativeCompilerAcceptsExplicitOptimization(int level)
    {
        string dir = Path.Combine(Path.GetTempPath(), "gflat-native-opt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string input = Path.Combine(dir, "input.ll"), exe = Path.Combine(dir, "app.exe");
            File.WriteAllText(input, CompilerTestHelper.EmitIr("inline int Add(int x) => x + 2; int main() => Add(40);"));
            Assert.Equal(0, NativeToolchain.Compile(input, exe, optimizationLevel: level).ExitCode);
            Assert.Equal(42, NativeToolchain.Run(exe, []).ExitCode);
        }
        finally { Directory.Delete(dir, true); }
    }
}
