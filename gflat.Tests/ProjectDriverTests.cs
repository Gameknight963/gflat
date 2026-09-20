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
            const string xml = """<GflatProject Kind="Executable"><Reference Path="lib/lib.gfproj"/></GflatProject>""";
            File.WriteAllText(project, xml);
            File.WriteAllText(Path.Combine(root, "main.gf"), "int main() => answer();");
            File.WriteAllText(Path.Combine(root, "lib/lib.gfproj"), """<GflatProject Kind="SourceLibrary"/>""");
            File.WriteAllText(Path.Combine(root, "lib/answer.gf"), "int answer() => 42;");
            Assert.Equal(0, Program.Main(["check", project]));
            Assert.Equal(0, Program.Main(["build", project, "--emit-ir"]));
            Assert.Contains("answer", File.ReadAllText(Path.Combine(root, "bin/app.ll")));
            Assert.Equal(42, Program.Main(["build", project, "--run"]));
            Assert.Equal(2, Program.Main([project, "--emit-ir", "-o", project]));
            Assert.Equal(xml, File.ReadAllText(project));
            File.WriteAllText(Path.Combine(root, "bin/keep.txt"), "keep");
            Assert.Equal(0, Program.Main(["clean", project]));
            Assert.False(File.Exists(Path.Combine(root, "bin/app.exe")));
            Assert.False(File.Exists(Path.Combine(root, "bin/app.ll")));
            Assert.True(File.Exists(Path.Combine(root, "bin/keep.txt")));
        }
        finally { Directory.Delete(root, true); }
    }
}
