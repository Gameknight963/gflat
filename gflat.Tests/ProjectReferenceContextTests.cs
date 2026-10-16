using System.Text.Json;
using gflat.Projects;
using gflat.TestSupport;

namespace gflat.Tests;

public sealed class ProjectReferenceContextTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "gflat-reference-context-" + Guid.NewGuid().ToString("N"));
    private string Write(string relative, string text)
    {
        string path = Path.GetFullPath(Path.Combine(root, relative));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceGraphMatchesMsBuildReferenceConditionalSourcesAndTransitiveReferences(bool customLoader)
    {
        string project = Write("app/app.gfproj", ProjectFileFixture.Xml(content: """
            <PropertyGroup><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>
            <ItemGroup Condition="'$(GflatAsReference)' == ''"><Compile Include="main.gf" /></ItemGroup>
            <ItemGroup><ProjectReference Include="../lib/lib.gfproj" /></ItemGroup>
            """));
        Write("app/main.gf", "int main()=>Answer();");
        Write("lib/lib.gfproj", ProjectFileFixture.Xml("SourceLibrary", """
            <PropertyGroup><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>
            <ItemGroup Condition="'$(GflatAsReference)' == 'true'">
              <Compile Include="reference-only.gf" />
              <ProjectReference Include="../leaf/leaf.gfproj" />
            </ItemGroup>
            <ItemGroup Condition="'$(GflatAsReference)' != 'true'"><Compile Include="standalone.gf" /></ItemGroup>
            """));
        string reference = Write("lib/reference-only.gf", "int Answer()=>Leaf();");
        string standalone = Write("lib/standalone.gf", "int Standalone()=>0;");
        Write("leaf/leaf.gfproj", ProjectFileFixture.Xml("SourceLibrary", """
            <PropertyGroup><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>
            <ItemGroup Condition="'$(GflatAsReference)' == 'true'"><Compile Include="leaf.gf" /></ItemGroup>
            """));
        string leaf = Write("leaf/leaf.gf", "int Leaf()=>42;");
        var evaluations = new List<(string Path, bool AsReference)>();
        var graph = customLoader ? ProjectGraph.Load(project, (path, asReference) =>
        {
            evaluations.Add((path, asReference));
            return ProjectModel.Load(path, "Debug", "AnyCPU", asReference: asReference);
        }) : ProjectGraph.Load(project);
        if (customLoader)
        {
            Assert.Equal((project, false), evaluations[0]);
            Assert.Equal(3, evaluations.Count);
            Assert.All(evaluations.Skip(1), item => Assert.True(item.AsReference));
        }
        var nativeGraph = NativeToolchain.Run("dotnet", ["msbuild", project, "-t:GflatGetSources", "-getTargetResult:GflatGetSources", "-nologo", "-v:q"]);
        Assert.True(nativeGraph.ExitCode == 0, nativeGraph.StandardOutput + nativeGraph.StandardError);
        using var json = JsonDocument.Parse(nativeGraph.StandardOutput);
        var actualSources = json.RootElement.GetProperty("TargetResults").GetProperty("GflatGetSources").GetProperty("Items")
            .EnumerateArray().Select(item => item.GetProperty("FullPath").GetString()!).Order(ProjectModel.Paths).ToArray();
        Assert.Equal(actualSources, graph.Sources.Order(ProjectModel.Paths));
        Assert.Contains(reference, graph.Sources);
        Assert.Contains(leaf, graph.Sources);
        Assert.DoesNotContain(standalone, graph.Sources);
        Assert.Contains(standalone, ProjectModel.Load(Path.Combine(root, "lib/lib.gfproj")).SourceFiles());
        Assert.Equal(0, Program.Main(["build", project, "--emit-ir"]));
    }
}
