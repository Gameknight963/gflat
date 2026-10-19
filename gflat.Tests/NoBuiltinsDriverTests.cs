using gflat.Projects;

namespace gflat.Tests;

public sealed class NoBuiltinsDriverTests
{
    private static void WithDirectory(Action<string> action)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-no-builtins-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { action(directory); }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectSourceOptionIsIndependentOfStandardLibrarySelection(bool noStdLib)
    {
        WithDirectory(directory =>
        {
            string source = Path.Combine(directory, "main.gf"), ir = Path.Combine(directory, "main.ll");
            File.WriteAllText(source, "int main() => 0;");
            string[] options = noStdLib ? ["--no-stdlib"] : [];
            Assert.Equal(0, Program.Main([source, "--emit-ir", "-o", ir, .. options]));
            Assert.DoesNotContain("\"no-builtins\"", File.ReadAllText(ir));
            Assert.Equal(0, Program.Main([source, "--emit-ir", "-o", ir, "--no-builtins", .. options]));
            Assert.Contains("\"no-builtins\"", File.ReadAllText(ir));
            Assert.Equal(0, Program.Main([source, "--emit-ir", "-o", ir, "--no-builtins", "--builtins", .. options]));
            Assert.DoesNotContain("\"no-builtins\"", File.ReadAllText(ir));
            Assert.Equal(0, Program.Main([source, "--check", "--no-builtins", .. options]));
        });
    }

    [Fact]
    public void ProjectModelDefaultsValidationAndOverridesAreExplicit()
    {
        WithDirectory(directory =>
        {
            string project = Path.Combine(directory, "app.gfproj");
            File.WriteAllText(project, TestSupport.ProjectFileFixture.Xml());
            Assert.False(ProjectModel.Load(project).NoBuiltins);
            File.WriteAllText(project, TestSupport.ProjectFileFixture.Xml(content: "<PropertyGroup><NoBuiltins>true</NoBuiltins></PropertyGroup>"));
            Assert.True(ProjectModel.Load(project).NoBuiltins);
            Assert.False(ProjectModel.Load(project, null, null, noBuiltins: false).NoBuiltins);
            File.WriteAllText(project, TestSupport.ProjectFileFixture.Xml(content: "<PropertyGroup><NoBuiltins>sometimes</NoBuiltins></PropertyGroup>"));
            var error = Assert.Throws<ProjectException>(() => ProjectModel.Load(project));
            Assert.Contains("NoBuiltins must be true or false", error.Message);
            Assert.True(ProjectModel.Load(project, null, null, noBuiltins: true).NoBuiltins);
        });
    }

    [Fact]
    public void ProjectBuildAndIncrementalManifestHonorPropertyAndBothCliOverrides()
    {
        WithDirectory(directory =>
        {
            string project = Path.Combine(directory, "app.gfproj");
            File.WriteAllText(project, TestSupport.ProjectFileFixture.Xml(content: "<PropertyGroup><NoBuiltins>true</NoBuiltins></PropertyGroup>"));
            File.WriteAllText(Path.Combine(directory, "main.gf"), "int main() => 0;");
            string output = Path.Combine(directory, "bin/Debug/app.ll"), settings = Path.Combine(directory, "obj/Debug/app.build.settings");
            Assert.Equal(0, Program.Main(["build", project, "--emit-ir"]));
            Assert.Contains("\"no-builtins\"", File.ReadAllText(output));
            Assert.Contains("NoBuiltins=true", File.ReadAllText(settings));
            DateTime previous = File.GetLastWriteTimeUtc(output);
            Assert.Equal(0, Program.Main(["build", project, "--emit-ir"]));
            Assert.Equal(previous, File.GetLastWriteTimeUtc(output));
            Assert.Equal(0, Program.Main(["build", project, "--emit-ir", "--builtins"]));
            Assert.DoesNotContain("\"no-builtins\"", File.ReadAllText(output));
            Assert.Contains("NoBuiltins=false", File.ReadAllText(settings));
            Assert.Equal(0, Program.Main(["build", project, "--emit-ir", "--no-builtins"]));
            Assert.Contains("\"no-builtins\"", File.ReadAllText(output));
            Assert.Equal(0, Program.Main(["check", project, "--builtins"]));
        });
    }

    [Fact]
    public void SourceLibraryBuildAndProjectReferencesReceiveExplicitOverride()
    {
        WithDirectory(directory =>
        {
            string libraryDirectory = Path.Combine(directory, "lib"); Directory.CreateDirectory(libraryDirectory);
            string library = Path.Combine(libraryDirectory, "library.gfproj");
            File.WriteAllText(library, TestSupport.ProjectFileFixture.Xml("SourceLibrary", """
                <ItemGroup Condition="'$(NoBuiltins)' == 'true'"><Compile Remove="normal.gf" /></ItemGroup>
                <ItemGroup Condition="'$(NoBuiltins)' != 'true'"><Compile Remove="restricted.gf" /></ItemGroup>
                """));
            File.WriteAllText(Path.Combine(libraryDirectory, "normal.gf"), "int Answer() => 7;");
            File.WriteAllText(Path.Combine(libraryDirectory, "restricted.gf"), "int Answer() => 42;");
            Assert.Equal(0, Program.Main(["build", library, "--no-builtins"]));
            Assert.Equal(0, Program.Main(["check", library, "--builtins"]));
            string project = Path.Combine(directory, "app.gfproj");
            File.WriteAllText(project, TestSupport.ProjectFileFixture.Xml(content: "<ItemGroup><Compile Remove=\"lib/**/*.gf\" /><ProjectReference Include=\"lib/library.gfproj\" /></ItemGroup>"));
            File.WriteAllText(Path.Combine(directory, "main.gf"), "int main() => Answer();");
            string output = Path.Combine(directory, "bin/Debug/app.ll");
            Assert.Equal(0, Program.Main(["build", project, "--emit-ir", "--no-builtins"]));
            Assert.Contains("ret i32 42", File.ReadAllText(output));
            Assert.Equal(0, Program.Main(["build", project, "--emit-ir", "--builtins"]));
            Assert.Contains("ret i32 7", File.ReadAllText(output));
        });
    }
}
