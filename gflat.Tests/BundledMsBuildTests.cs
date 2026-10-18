using gflat.Projects;

namespace gflat.Tests;

public sealed class BundledMsBuildTests
{
    [Fact]
    public void ProjectEvaluationUsesTheEngineBesideTheApplication()
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-engine-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string project = Path.Combine(directory, "app.gfproj");
            File.WriteAllText(project, TestSupport.ProjectFileFixture.Xml(content: """
                <PropertyGroup><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>
                <ItemGroup><Compile Include="$(MSBuildBinPath)/engine.gf" /></ItemGroup>
                """));
            var evaluated = ProjectModel.Load(project);
            Assert.Equal(Path.Combine(AppContext.BaseDirectory, "engine.gf"), Assert.Single(evaluated.SourceFiles()));
            Assert.Contains(Path.Combine(AppContext.BaseDirectory, "Current", "Microsoft.Common.props"), evaluated.Imports);
        }
        finally { Directory.Delete(directory, true); }
    }
}
