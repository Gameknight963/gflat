using gflat.Projects;

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
    public void CommentsGlobsAndExclusionsResolveRelativeToProject()
    {
        string project = Write("app/app.gfproj", """
            <GflatProject Kind="Executable">
              <!-- A hand-written project file. -->
              <Sources Include="**/*.gf" />
              <Sources Exclude="scratch/**" />
            </GflatProject>
            """);
        string main = Write("app/main.gf", "int main() => 0;");
        string helper = Write("app/nested/helper.gf", "int helper() => 0;");
        Write("app/scratch/invalid.gf", "invalid");
        Write("app/bin/generated.gf", "invalid");
        Write("app/obj/generated.gf", "invalid");
        Write("app/nested/other.txt", "ignored");
        Assert.Equal(new[] { main, helper }, ProjectGraph.Load(project).Sources);
    }

    [Fact]
    public void ReferencesDeduplicateDiamondDependencies()
    {
        string rootProject = Write("app/app.gfproj", """<GflatProject Kind="Executable"><Reference Path="../a/a.gfproj"/><Reference Path="../b/b.gfproj"/></GflatProject>""");
        Write("a/a.gfproj", """<GflatProject Kind="SourceLibrary"><Reference Path="../shared/shared.gfproj"/></GflatProject>""");
        Write("b/b.gfproj", """<GflatProject Kind="SourceLibrary"><Reference Path="../shared/shared.gfproj"/></GflatProject>""");
        Write("shared/shared.gfproj", """<GflatProject Kind="SourceLibrary"/>""");
        string shared = Write("shared/shared.gf", "int answer() => 42;");
        Write("app/main.gf", "int main() => answer();");
        var graph = ProjectGraph.Load(rootProject);
        Assert.Equal(4, graph.Projects.Count);
        Assert.Single(graph.Sources, s => s == shared);
    }

    [Theory]
    [InlineData("<Project/>")]
    [InlineData("<GflatProject Kind=\"Wrong\"/>")]
    [InlineData("<GflatProject Kind=\"Executable\" Unknown=\"1\"/>")]
    [InlineData("<GflatProject Kind=\"Executable\"><Source Include=\"*.gf\"/></GflatProject>")]
    [InlineData("<GflatProject Kind=\"Executable\"><Sources Include=\"../*.gf\"/></GflatProject>")]
    [InlineData("<GflatProject Kind=\"Executable\"><Sources Include=\"*.gf\" Exclude=\"*.gf\"/></GflatProject>")]
    [InlineData("<GflatProject Kind=\"Executable\"><Reference Path=\"C:/a.gfproj\"/></GflatProject>")]
    [InlineData("<GflatProject Kind=\"Executable\"><Reference Path=\"\\absolute.gfproj\"/></GflatProject>")]
    [InlineData("<GflatProject Kind=\"Executable\"><Reference Path=\"\\\\server/share/a.gfproj\"/></GflatProject>")]
    [InlineData("<!DOCTYPE GflatProject [<!ENTITY x SYSTEM 'file:///secret'>]><GflatProject Kind=\"Executable\">&x;</GflatProject>")]
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
        string project = Write("app.gfproj", """<GflatProject Kind="Executable"><Reference Path="lib/lib.gfproj"/></GflatProject>""");
        Write("lib/lib.gfproj", """<GflatProject Kind="SourceLibrary"><Reference Path="../app.gfproj"/></GflatProject>""");
        Assert.Contains("Cyclic", Assert.Throws<ProjectException>(() => ProjectGraph.Load(project)).Message);
        Write("lib/lib.gfproj", """<GflatProject Kind="Executable"/>""");
        Assert.Contains("SourceLibrary", Assert.Throws<ProjectException>(() => ProjectGraph.Load(project)).Message);
    }

    [Fact]
    public void NestedProjectsOwnTheirSources()
    {
        string project = Write("app.gfproj", """<GflatProject Kind="Executable"/>""");
        string main = Write("main.gf", "int main() => 0;");
        Write("nested/nested.gfproj", """<GflatProject Kind="Executable"/>""");
        Write("nested/main.gf", "int main() => 1;");
        Assert.Equal(new[] { main }, ProjectGraph.Load(project).Sources);
    }

    [Fact]
    public void AnalysisUsesUnsavedReferenceAndKeepsLastGoodProjectAfterInvalidXml()
    {
        string project = Write("app/app.gfproj", """<GflatProject Kind="Executable"><Reference Path="../lib/lib.gfproj"/></GflatProject>""");
        Write("lib/lib.gfproj", """<GflatProject Kind="SourceLibrary"/>""");
        string main = Write("app/main.gf", "int main() => answer();");
        string library = Write("lib/lib.gf", "int other() => 0;");
        var open = new Dictionary<string, OpenDocument>(Workspace.Paths)
        {
            [main] = new(main, File.ReadAllText(main), 1),
            [library] = new(library, "int answer() => 42;", 1)
        };
        var state = new ProjectWorkspace();
        var good = Workspace.Analyze(root, open, state);
        Assert.Empty(good.Diagnostics[main]);
        File.WriteAllText(project, "<GflatProject");
        state.Invalidate();
        var broken = Workspace.Analyze(root, open, state);
        Assert.Contains(broken.Diagnostics[project], d => d.Code == "GFPROJ");
        Assert.DoesNotContain(broken.Diagnostics[main], d => d.Code != "GFPROJ");
        File.WriteAllText(project, """<GflatProject Kind="Executable"/>""");
        state.Invalidate();
        var changed = Workspace.Analyze(root, open, state);
        Assert.Contains(changed.Diagnostics[main], d => d.Message.Contains("answer"));
        Assert.DoesNotContain(project, changed.Diagnostics.Keys);
    }

    [Fact]
    public void MissingReferenceIsWatchedSoCreatingItCanRecover()
    {
        string project = Write("app.gfproj", """<GflatProject Kind="Executable"><Reference Path="missing.gfproj"/></GflatProject>""");
        var state = new ProjectWorkspace();
        var result = state.Load(project);
        Assert.NotNull(result.Error);
        Assert.Contains(Path.Combine(root, "missing.gfproj"), result.Paths);
    }
}
