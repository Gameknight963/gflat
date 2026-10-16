using gflat.Projects;
using System.Xml.Linq;

namespace gflat.Tests;

public class ProjectConfigurationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CliHonorsProjectDefaultsAndExplicitOverridesAcrossReferences(bool sharedDefaults)
    {
        string root = Path.Combine(Path.GetTempPath(), "gflat-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "lib"));
        try
        {
            const string configuration = "<Configuration>Release</Configuration><Platform>Custom</Platform>";
            if (sharedDefaults)
                File.WriteAllText(Path.Combine(root, "Directory.Build.props"), "<Project><PropertyGroup>" + configuration + "</PropertyGroup></Project>");
            string project = Path.Combine(root, "app.gfproj");
            File.WriteAllText(project, "<Project Sdk=\"Gflat.Sdk\"><PropertyGroup><NoStdLib>true</NoStdLib>" +
                (sharedDefaults ? "" : configuration) + "</PropertyGroup><ItemGroup><Compile Remove=\"lib/**/*.gf\" />" +
                "<ProjectReference Include=\"lib/lib.gfproj\" /></ItemGroup></Project>");
            File.WriteAllText(Path.Combine(root, "main.gf"), "int main() => Answer();");
            File.WriteAllText(Path.Combine(root, "lib/lib.gfproj"), """
                <Project Sdk="Gflat.Sdk">
                  <PropertyGroup><NoStdLib>true</NoStdLib><OutputType>Library</OutputType><Configuration>Debug</Configuration><Platform>AnyCPU</Platform></PropertyGroup>
                  <ItemGroup Condition="'$(Configuration)' == 'Release' and '$(Platform)' == 'Custom'"><Compile Remove="debug.gf" /></ItemGroup>
                  <ItemGroup Condition="'$(Configuration)' != 'Release' or '$(Platform)' != 'Custom'"><Compile Remove="release.gf" /></ItemGroup>
                </Project>
                """);
            string release = Path.GetFullPath(Path.Combine(root, "lib/release.gf"));
            string debug = Path.GetFullPath(Path.Combine(root, "lib/debug.gf"));
            File.WriteAllText(release, "int Answer() => 42;");
            File.WriteAllText(debug, "int Answer() => 7;");
            var graph = ProjectGraph.Load(project);
            Assert.Equal("Release", graph.Root.Configuration);
            Assert.Equal("Custom", graph.Root.Platform);
            Assert.Equal(2, graph.Root.OptimizationLevel);
            Assert.All(graph.Projects, p => Assert.Equal("Release", p.Configuration));
            Assert.All(graph.Projects, p => Assert.Equal("Custom", p.Platform));
            Assert.Contains(release, graph.Sources);
            Assert.DoesNotContain(debug, graph.Sources);
            Assert.Equal(0, Program.Main(["build", project, "--emit-ir"]));
            string releaseOutput = Path.Combine(root, "bin/Release/app.ll");
            Assert.Contains("ret i32 42", File.ReadAllText(releaseOutput));
            Assert.Equal(0, Program.Main(["build", project, "--configuration", "Debug", "--platform", "AnyCPU", "--emit-ir"]));
            Assert.Contains("ret i32 7", File.ReadAllText(Path.Combine(root, "bin/Debug/app.ll")));
            Assert.True(File.Exists(releaseOutput));
        }
        finally { Directory.Delete(root, true); }
    }
}
