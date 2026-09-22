namespace gflat.Tests;

public sealed class ProjectDriverTests
{
    [Fact]
    public void ProjectBuildUsesReferenceSourcesAndProtectsProjectFiles()
    {
        string root = Path.Combine(Path.GetTempPath(), "gflat_project_driver_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "lib"));
        try
        {
            string project = Path.Combine(root, "app.gfproj");
            string xml = TestSupport.ProjectFileFixture.Xml(content: """<ItemGroup><Compile Remove="lib/**/*.gf"/><ProjectReference Include="lib/lib.gfproj"/></ItemGroup>""");
            File.WriteAllText(project, xml);
            File.WriteAllText(Path.Combine(root, "main.gf"), "int main() => answer();");
            File.WriteAllText(Path.Combine(root, "lib/lib.gfproj"), TestSupport.ProjectFileFixture.Xml("SourceLibrary"));
            File.WriteAllText(Path.Combine(root, "lib/answer.gf"), "int answer() => 42;");
            Assert.Equal(0, Program.Main(["check", project]));
            Assert.Equal(0, Program.Main(["build", project, "--emit-ir"]));
            Assert.Contains("answer", File.ReadAllText(Path.Combine(root, "bin/Debug/app.ll")));
            Assert.Equal(2, Program.Main([project, "--emit-ir", "-o", project]));
            Assert.Equal(xml, File.ReadAllText(project));
            File.WriteAllText(Path.Combine(root, "bin/keep.txt"), "keep");
            Assert.Equal(0, Program.Main(["clean", project]));
            Assert.False(File.Exists(Path.Combine(root, "bin/Debug/app.exe")));
            Assert.False(File.Exists(Path.Combine(root, "bin/Debug/app.ll")));
            Assert.True(File.Exists(Path.Combine(root, "bin/keep.txt")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ProjectBuildRunsNativeExecutable()
    {
        string root = Path.Combine(Path.GetTempPath(), "gflat_native_project_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string project = Path.Combine(root, "app.gfproj");
            File.WriteAllText(project, TestSupport.ProjectFileFixture.Xml());
            File.WriteAllText(Path.Combine(root, "main.gf"), "int main() => 42;");
            Assert.Equal(42, Program.Main(["build", project, "--run"]));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ProjectCommandsExecuteMSBuildTargetsAndPropagateFailures()
    {
        string root = Path.Combine(Path.GetTempPath(), "gflat msbuild " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string project = Path.Combine(root, "app.gfproj");
            File.WriteAllText(project, TestSupport.ProjectFileFixture.Xml(content: """
                <ItemGroup Condition="'$(Configuration)' == 'Release'"><Compile Remove="invalid.gf" /></ItemGroup>
                <Target Name="RecordBuild" AfterTargets="Build">
                  <WriteLinesToFile File="built.txt" Lines="$(Configuration)" Overwrite="true" />
                </Target>
                <Target Name="FailCheck" BeforeTargets="Check"><Error Text="deliberate target failure" /></Target>
                """));
            File.WriteAllText(Path.Combine(root, "main.gf"), "int main() => 0;");
            File.WriteAllText(Path.Combine(root, "invalid.gf"), "this is invalid");
            Assert.NotEqual(0, Program.Main(["check", project]));
            Assert.False(File.Exists(Path.Combine(root, "built.txt")));
            string output = Path.Combine(root, "new output directory", "custom output.ll");
            Assert.Equal(0, Program.Main(["build", project, "--configuration", "Release", "--emit-ir", "-o", output]));
            Assert.True(File.Exists(output));
            Assert.Equal("Release", File.ReadAllText(Path.Combine(root, "built.txt")).Trim());
            Assert.Equal(0, Program.Main(["clean", project]));
            Assert.True(File.Exists(output));
        }
        finally { Directory.Delete(root, true); }
    }
}
