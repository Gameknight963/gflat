using gflat.Projects;
using System.Xml.Linq;

namespace gflat.Tests;

[CollectionDefinition("Project SDK environment", DisableParallelization = true)]
public sealed class ProjectSdkEnvironmentCollection { }

[Collection("Project SDK environment")]
public sealed class ProjectSdkTests
{
    [Fact]
    public void ProviderPathsEscapeMsBuildExpressionsAndItemSeparators()
    {
        Assert.Equal("C:/sdk space %24%3B%25%40%27%28%29", ProjectSdk.EscapePropertyValue("C:/sdk space $;%@'()"));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DiscoversCheckoutAndVsixLayouts(bool vsix)
    {
        string root = Temporary();
        try
        {
            string sdk = vsix ? Path.Combine(root, "Sdk") : root;
            CreateSdk(sdk);
            string compiler = Path.Combine(root, "Compiler"); Directory.CreateDirectory(compiler);
            Assert.Equal(sdk, ProjectSdk.FindRoot(compiler));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ProjectSdkRootOverrideWinsOverBundledFallback()
    {
        string root = Temporary();
        try
        {
            string sdk = Path.Combine(root, "custom sdk"); CreateSdk(sdk);
            string project = Path.Combine(root, "application.gfproj");
            File.WriteAllText(Path.Combine(root, "main.gf"), "int main() => 0;");
            File.WriteAllText(project, new XElement("Project", new XAttribute("DefaultTargets", "Build"),
                new XElement("PropertyGroup", new XElement("GflatSdkRoot", sdk)),
                XElement.Parse("<PropertyGroup><GflatSdkRoot Condition=\"'$(GflatSdkRoot)' == ''\">$(GflatDefaultSdkRoot)</GflatSdkRoot></PropertyGroup>"),
                XElement.Parse("<Import Project=\"$(GflatSdkRoot)/build/Gflat.props\" />"),
                XElement.Parse("<Import Project=\"$(GflatSdkRoot)/build/Gflat.targets\" />")).ToString());
            var model = ProjectModel.Load(project);
            Assert.Equal(sdk, model.SdkRoot);
            Assert.Contains(Path.Combine(sdk, "build", "Gflat.props"), model.Imports);
            Assert.Equal(0, Program.Main(["build", project, "--emit-ir"]));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void PortableProjectUsesHostFallbackWithoutSavingInstalledPaths()
    {
        string root = Temporary(); Directory.CreateDirectory(root);
        try
        {
            string project = Path.Combine(root, "application.gfproj");
            string xml = """
                <Project DefaultTargets="Build">
                  <PropertyGroup><GflatSdkRoot Condition="'$(GflatSdkRoot)' == ''">$(GflatDefaultSdkRoot)</GflatSdkRoot></PropertyGroup>
                  <Import Project="$(GflatSdkRoot)/build/Gflat.props" />
                  <Import Project="$(GflatSdkRoot)/build/Gflat.targets" />
                </Project>
                """;
            File.WriteAllText(project, xml);
            File.WriteAllText(Path.Combine(root, "main.gf"), "int main() => 0;");
            Assert.Equal(ProjectCreation.FindToolchainRoot(), ProjectModel.Load(project).SdkRoot);
            Assert.Equal(0, Program.Main(["build", project, "--emit-ir"]));
            Assert.Equal(xml, File.ReadAllText(project));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void MissingExplicitSdkGivesActionableError()
    {
        string root = Temporary(); Directory.CreateDirectory(root);
        try
        {
            string project = Path.Combine(root, "broken.gfproj");
            File.WriteAllText(project, """
                <Project><PropertyGroup><GflatSdkRoot>missing-sdk</GflatSdkRoot></PropertyGroup><Import Project="$(GflatSdkRoot)/build/Gflat.props" /></Project>
                """);
            var error = Assert.Throws<ProjectException>(() => ProjectModel.Load(project));
            Assert.Contains("Set GflatSdkRoot", error.Message);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void EnvironmentSdkRootWinsInCreationEvaluationAndBuild()
    {
        string root = Temporary();
        string? previous = Environment.GetEnvironmentVariable("GflatSdkRoot");
        try
        {
            string sdk = Path.Combine(root, "environment sdk"); CreateSdk(sdk);
            Environment.SetEnvironmentVariable("GflatSdkRoot", sdk);
            string project = ProjectCreation.Create(Path.Combine(root, "application"), useStd: false);
            Assert.Equal(sdk, ProjectCreation.FindToolchainRoot());
            Assert.Equal(sdk, ProjectModel.Load(project).SdkRoot);
            Assert.Equal(0, Program.Main(["build", project, "--emit-ir"]));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GflatSdkRoot", previous);
            Directory.Delete(root, true);
        }
    }

    private static void CreateSdk(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "build"));
        Directory.CreateDirectory(Path.Combine(root, "std"));
        string checkout = TestSupport.ProjectFileFixture.RepositoryRoot;
        foreach (string name in new[] { "Gflat.props", "Gflat.targets" })
            File.Copy(Path.Combine(checkout, "build", name), Path.Combine(root, "build", name));
        File.Copy(Path.Combine(checkout, "std", "std.gfproj"), Path.Combine(root, "std", "std.gfproj"));
    }
    private static string Temporary() => Path.Combine(Path.GetTempPath(), "gflat sdk " + Guid.NewGuid().ToString("N"));
}
