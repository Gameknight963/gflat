namespace gflat.Tests;

public sealed class NativeLinkManifestSafetyTests
{
    [Theory]
    [InlineData("Build")]
    [InlineData("Clean")]
    public void DirectMsBuildReportsMalformedOverridesAsAnOrdinaryErrorAndPreservesOutput(string target)
    {
        InProject((root, project) => {
            string overrides = Path.Combine(root, "invalid-overrides.xml");
            File.WriteAllText(overrides, "<unclosed");
            string output = PreserveOutput(root);
            var result = NativeToolchain.Run("dotnet", ["msbuild", project, "-nologo", "-v:q", "-t:" + target,
                "-p:GflatNativeLinkOverrides=" + gflat.Projects.ProjectSdk.EscapePropertyValue(overrides)]);
            string log = result.StandardOutput + result.StandardError;
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("Invalid native-link input XML", log);
            Assert.DoesNotContain("MSB4018", log);
            Assert.Equal("preserve existing output", File.ReadAllText(output));
            Assert.Equal("<unclosed", File.ReadAllText(overrides));
        });
    }

    [Theory]
    [InlineData("build")]
    [InlineData("clean")]
    public void GeneratedManifestCannotOverwriteOrDeleteAnExplicitNativeLibraryInput(string command)
    {
        InProject((root, project) => {
            const string original = "preserve supplied native library bytes";
            string manifest = Path.Combine(root, "obj", "Debug", "app.native-link.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
            File.WriteAllText(manifest, original);
            File.WriteAllText(project, TestSupport.ProjectFileFixture.Xml(content: """
                <ItemGroup><NativeLibraryFile Include="obj/Debug/app.native-link.xml" /></ItemGroup>
                """));
            string output = PreserveOutput(root);
            Assert.NotEqual(0, Program.Main([command, project]));
            Assert.Equal(original, File.ReadAllText(manifest));
            Assert.Equal("preserve existing output", File.ReadAllText(output));
        });
    }

    private static string PreserveOutput(string root)
    {
        string output = Path.Combine(root, "bin", "Debug", "app.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, "preserve existing output");
        return output;
    }

    private static void InProject(Action<string, string> action)
    {
        string root = Path.Combine(Path.GetTempPath(), "gflat-native-manifest-safety-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string project = Path.Combine(root, "app.gfproj");
        File.WriteAllText(project, TestSupport.ProjectFileFixture.Xml());
        File.WriteAllText(Path.Combine(root, "main.gf"), "int main() => 0;");
        try { action(root, project); } finally { Directory.Delete(root, true); }
    }
}
