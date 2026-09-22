using gflat.Projects;
using gflat.TestSupport;

namespace gflat.LanguageServer.Tests;

public sealed class ProjectTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "gflat_projects_" + Guid.NewGuid().ToString("N"));
    private string Write(string name, string text)
    {
        string path = Path.GetFullPath(Path.Combine(root, name));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [Fact]
    public void MSBuildEvaluatesCommentsImportsConditionsAndRemovesWithoutRunningTargets()
    {
        string imported = Write("shared/items.props", """
            <Project>
              <ItemGroup Condition="'$(Configuration)' == 'Debug'">
                <Compile Remove="scratch/**/*.gf" />
              </ItemGroup>
            </Project>
            """);
        string project = Write("app/app.gfproj", ProjectFileFixture.Xml(content: """
            <!-- Imported conditions must agree with MSBuild, including for unsaved files. -->
            <Import Project="../shared/items.props" />
            <Target Name="DoNotRun" BeforeTargets="Build"><Error Text="Evaluation ran a build!" /></Target>
            """));
        string main = Write("app/main.gf", "int main() => 0;");
        string helper = Write("app/nested/helper.gf", "int helper() => 0;");
        string scratch = Write("app/scratch/invalid.gf", "invalid");
        Write("app/bin/generated.gf", "invalid");
        var model = ProjectModel.Load(project);
        Assert.Equal(new[] { main, helper }, model.SourceFiles());
        Assert.Contains(imported, model.Imports);
        Assert.True(model.IncludesFile(Path.Combine(root, "app/new.gf")));
        Assert.False(model.IncludesFile(Path.Combine(root, "app/scratch/new.gf")));
        Assert.Contains(scratch, ProjectModel.Load(project, "Release", "AnyCPU").SourceFiles());
    }

    [Fact]
    public void ReferencesDeduplicateDiamondDependencies()
    {
        string project = Write("app/app.gfproj", ProjectFileFixture.Xml(content: """
            <ItemGroup><ProjectReference Include="../a/a.gfproj;../b/b.gfproj"/></ItemGroup>
            """));
        foreach (string name in new[] { "a", "b" })
            Write(name + "/" + name + ".gfproj", ProjectFileFixture.Xml("SourceLibrary", """
                <ItemGroup><ProjectReference Include="../shared/shared.gfproj"/></ItemGroup>
                """));
        Write("shared/shared.gfproj", ProjectFileFixture.Xml("SourceLibrary"));
        string shared = Write("shared/shared.gf", "int answer() => 42;");
        Write("app/main.gf", "int main() => answer();");
        var graph = ProjectGraph.Load(project);
        Assert.Equal(4, graph.Projects.Count);
        Assert.Single(graph.Sources, s => s == shared);
    }

    [Theory]
    [InlineData("<Project/>")]
    [InlineData("<GflatProject Kind=\"Executable\"/>")]
    [InlineData("<Project")]
    [InlineData("<Project><Import Project=\"missing.props\"/></Project>")]
    public void InvalidProjectsProduceLocatedErrors(string xml)
    {
        string project = Write("app.gfproj", xml);
        var error = Assert.Throws<ProjectException>(() => ProjectModel.Load(project));
        Assert.Equal(project, error.ProjectPath);
        Assert.True(error.Line >= 1);
    }

    [Fact]
    public void CyclesAndExecutableReferencesAreRejected()
    {
        string project = Write("app.gfproj", ProjectFileFixture.Xml(content: """
            <ItemGroup><ProjectReference Include="lib/lib.gfproj"/></ItemGroup>
            """));
        Write("lib/lib.gfproj", ProjectFileFixture.Xml("SourceLibrary", """
            <ItemGroup><ProjectReference Include="../app.gfproj"/></ItemGroup>
            """));
        Assert.Contains("Cyclic", Assert.Throws<ProjectException>(() => ProjectGraph.Load(project)).Message);
        Write("lib/lib.gfproj", ProjectFileFixture.Xml());
        Assert.Contains("SourceLibrary", Assert.Throws<ProjectException>(() => ProjectGraph.Load(project)).Message);
    }

    [Fact]
    public void AnalysisUsesUnsavedReferenceAndKeepsLastGoodProjectAfterInvalidXml()
    {
        string project = Write("app/app.gfproj", ProjectFileFixture.Xml(content: """
            <ItemGroup><ProjectReference Include="../lib/lib.gfproj"/></ItemGroup>
            """));
        Write("lib/lib.gfproj", ProjectFileFixture.Xml("SourceLibrary"));
        string main = Write("app/main.gf", "int main() => answer();");
        string library = Write("lib/lib.gf", "int other() => 0;");
        var open = new Dictionary<string, OpenDocument>(Workspace.Paths)
        {
            [main] = new(main, File.ReadAllText(main), 1),
            [library] = new(library, "int answer() => 42;", 1)
        };
        var state = new ProjectWorkspace();
        Assert.Empty(Workspace.Analyze(root, open, state).Diagnostics[main]);
        File.WriteAllText(project, "<Project");
        state.Invalidate();
        var broken = Workspace.Analyze(root, open, state);
        Assert.Contains(broken.Diagnostics[project], d => d.Code == "GFPROJ");
        Assert.DoesNotContain(broken.Diagnostics[main], d => d.Code != "GFPROJ");
        File.WriteAllText(project, ProjectFileFixture.Xml());
        state.Invalidate();
        var changed = Workspace.Analyze(root, open, state);
        Assert.Contains(changed.Diagnostics[main], d => d.Message.Contains("answer"));
        Assert.DoesNotContain(project, changed.Diagnostics.Keys);
    }

    [Fact]
    public void MissingReferencesAndImportsAreWatched()
    {
        string imported = Write("settings.props", "<Project/>");
        string project = Write("app.gfproj", ProjectFileFixture.Xml(content: """
            <Import Project="settings.props"/>
            """));
        var state = new ProjectWorkspace();
        Assert.Contains(imported, state.Load(project).Paths);
        File.WriteAllText(project, ProjectFileFixture.Xml(content: """
            <ItemGroup><ProjectReference Include="missing.gfproj"/></ItemGroup>
            """));
        state.Invalidate();
        var result = state.Load(project);
        Assert.NotNull(result.Error);
        Assert.Contains(Path.Combine(root, "missing.gfproj"), result.Paths);
        Assert.Contains(imported, result.Paths);
    }
}
