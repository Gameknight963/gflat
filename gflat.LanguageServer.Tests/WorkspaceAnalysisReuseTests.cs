using gflat.TestSupport;

namespace gflat.LanguageServer.Tests;

public sealed class WorkspaceAnalysisReuseTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "gflat_analysis_reuse_" + Guid.NewGuid().ToString("N"));

    private string Write(string relative, string text)
    {
        string path = Path.GetFullPath(Path.Combine(root, relative));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [Fact]
    public void OpeningAndSavingAnUnchangedProjectSourceReusesItsModel()
    {
        Write("app.gfproj", ProjectFileFixture.Xml());
        string main = Write("main.gf", "int main() => answer();");
        string other = Write("other.gf", "int answer() => 42;");
        var documents = new Dictionary<string, OpenDocument>(Workspace.Paths) { [main] = new(main, File.ReadAllText(main), 1) };
        var projects = new ProjectWorkspace();
        var first = Workspace.Analyze(root, documents, projects);
        documents[other] = new(other, File.ReadAllText(other), 1);
        var opened = Workspace.Analyze(root, documents, projects);
        Assert.Same(first.Models![main], opened.Models![main]);
        Assert.Same(opened.Models[main], opened.Models[other]);
        documents[other] = documents[other] with { Version = 2 };
        projects.Invalidate();
        var saved = Workspace.Analyze(root, documents, projects);
        Assert.Same(first.Models[main], saved.Models![main]);
        Assert.Empty(saved.Diagnostics[main]);
    }

    [Fact]
    public void CanceledAnalysisPreservesTheLastCompleteModel()
    {
        Write("app.gfproj", ProjectFileFixture.Xml());
        string main = Write("main.gf", "int main() => 42;");
        var documents = new Dictionary<string, OpenDocument>(Workspace.Paths) { [main] = new(main, File.ReadAllText(main), 1) };
        var projects = new ProjectWorkspace();
        var first = Workspace.Analyze(root, documents, projects);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => Workspace.Analyze(root, documents, projects, cancellation.Token));
        var retried = Workspace.Analyze(root, documents, projects);
        Assert.Same(first.Models![main], retried.Models![main]);
        Assert.Empty(retried.Diagnostics[main]);
    }

    [Fact]
    public void UnsavedEditsRebuildTheWholeGraphAndRecoverDiagnostics()
    {
        Write("app.gfproj", ProjectFileFixture.Xml());
        string main = Write("main.gf", "int main() => answer();");
        string other = Write("other.gf", "int answer() => 42;");
        var documents = new Dictionary<string, OpenDocument>(Workspace.Paths) {
            [main] = new(main, File.ReadAllText(main), 1), [other] = new(other, File.ReadAllText(other), 1)
        };
        var projects = new ProjectWorkspace();
        var first = Workspace.Analyze(root, documents, projects);
        documents[other] = new(other, "int different() => 42;", 2);
        var edited = Workspace.Analyze(root, documents, projects);
        Assert.NotSame(first.Models![main], edited.Models![main]);
        Assert.Contains(edited.Diagnostics[main], diagnostic => diagnostic.Message.Contains("answer"));
        Assert.Same(edited.Models[main], Workspace.Analyze(root, documents, projects).Models![main]);
        documents.Remove(other);
        var closed = Workspace.Analyze(root, documents, projects);
        Assert.NotSame(edited.Models[main], closed.Models![main]);
        Assert.Empty(closed.Diagnostics[main]);
    }

    [Fact]
    public void UnopenedDiskSourceChangesInvalidateWithoutProjectReevaluation()
    {
        Write("app.gfproj", ProjectFileFixture.Xml());
        string main = Write("main.gf", "int main() => answer();");
        string other = Write("other.gf", "int answer() => 42;");
        var documents = new Dictionary<string, OpenDocument>(Workspace.Paths) { [main] = new(main, File.ReadAllText(main), 1) };
        var projects = new ProjectWorkspace();
        var first = Workspace.Analyze(root, documents, projects);
        File.WriteAllText(other, "int other() => 0;");
        var changed = Workspace.Analyze(root, documents, projects);
        Assert.NotSame(first.Models![main], changed.Models![main]);
        Assert.Contains(changed.Diagnostics[main], diagnostic => diagnostic.Message.Contains("answer"));
        File.Delete(other);
        var missing = Workspace.Analyze(root, documents, projects);
        Assert.Contains(missing.Diagnostics[other], diagnostic => diagnostic.Code == "GFLS0001");
        Assert.False(missing.Models!.ContainsKey(main));
    }

    [Fact]
    public void ProjectGraphChangesReplaceBindingsAndProjectErrorsRemainVisible()
    {
        string project = Write("app/app.gfproj", ProjectFileFixture.Xml(content: """
            <ItemGroup><ProjectReference Include="../lib/lib.gfproj" /></ItemGroup>
            """));
        Write("lib/lib.gfproj", ProjectFileFixture.Xml("SourceLibrary"));
        string main = Write("app/main.gf", "int main() => answer();");
        Write("lib/value.gf", "int answer() => 42;");
        var documents = new Dictionary<string, OpenDocument>(Workspace.Paths) { [main] = new(main, File.ReadAllText(main), 1) };
        var projects = new ProjectWorkspace();
        var first = Workspace.Analyze(root, documents, projects);
        File.WriteAllText(project, "<Project");
        projects.Invalidate();
        var brokenProject = Workspace.Analyze(root, documents, projects);
        Assert.Same(first.Models![main], brokenProject.Models![main]);
        Assert.Contains(brokenProject.Diagnostics[main], diagnostic => diagnostic.Code == "GFPROJ");
        File.WriteAllText(project, ProjectFileFixture.Xml());
        projects.Invalidate();
        var changed = Workspace.Analyze(root, documents, projects);
        Assert.NotSame(first.Models[main], changed.Models![main]);
        Assert.Contains(changed.Diagnostics[main], diagnostic => diagnostic.Message.Contains("answer"));
        Assert.DoesNotContain(project, changed.Diagnostics.Keys);
    }

    [Fact]
    public void LooseFileAutomaticSourceEditsParticipateInReuse()
    {
        string main = Write("main.gf", "int main() => 0;");
        SourceFile attribute = StandardLibrary.Load("core/Attribute.gf");
        var documents = new Dictionary<string, OpenDocument>(Workspace.Paths) {
            [main] = new(main, File.ReadAllText(main), 1),
            [attribute.Path] = new(attribute.Path, attribute.Text, 1)
        };
        var projects = new ProjectWorkspace();
        var first = Workspace.Analyze(root, documents, projects);
        Assert.Same(first.Models![main], Workspace.Analyze(root, documents, projects).Models![main]);
        documents[attribute.Path] = new(attribute.Path, attribute.Text + "\nint cacheProbe() => missingCacheProbe();", 2);
        var edited = Workspace.Analyze(root, documents, projects);
        Assert.NotSame(first.Models[main], edited.Models![main]);
        Assert.Contains(edited.Diagnostics[attribute.Path], diagnostic => diagnostic.Message.Contains("missingCacheProbe"));
    }

    [Fact]
    public void SourceSelectionOptionsCannotReuseALooseFileModel()
    {
        string main = Write("main.gf", "class Attribute {} int main() => 0;");
        var documents = new Dictionary<string, OpenDocument>(Workspace.Paths) { [main] = new(main, File.ReadAllText(main), 1) };
        var projects = new ProjectWorkspace();
        var loose = Workspace.Analyze(root, documents, projects);
        Assert.Contains(loose.Diagnostics[main], diagnostic => diagnostic.Message.Contains("reserved"));
        Write("app.gfproj", ProjectFileFixture.Xml());
        projects.Invalidate();
        var project = Workspace.Analyze(root, documents, projects);
        Assert.NotSame(loose.Models![main], project.Models![main]);
        Assert.Empty(project.Diagnostics[main]);
    }

    [Fact]
    public void SeparateSessionsDoNotShareModels()
    {
        string main = Write("main.gf", "int main() => 0;");
        var documents = new Dictionary<string, OpenDocument>(Workspace.Paths) { [main] = new(main, File.ReadAllText(main), 1) };
        var first = Workspace.Analyze(root, documents, new ProjectWorkspace());
        var second = Workspace.Analyze(root, documents, new ProjectWorkspace());
        Assert.NotSame(first.Models![main], second.Models![main]);
    }

    [Fact]
    public void FocusedSnapshotsCannotReplaceFullAnalysisAndFullSnapshotsCanServeFocusedQueries()
    {
        Write("app.gfproj", ProjectFileFixture.Xml());
        string main = Write("main.gf", "int main() => Answer();");
        string other = Write("other.gf", "int Answer() => missing;");
        var documents = new Dictionary<string, OpenDocument>(Workspace.Paths) { [main] = new(main, File.ReadAllText(main), 1) };
        var projects = new ProjectWorkspace();
        var partial = Workspace.Analyze(root, documents, projects, bodyPaths: new HashSet<string>(Workspace.Paths) { main });
        Assert.True(partial.IsPartial);
        Assert.Empty(partial.Diagnostics[other]);
        var full = Workspace.Analyze(root, documents, projects);
        Assert.False(full.IsPartial);
        Assert.NotSame(partial.Models![main], full.Models![main]);
        Assert.Contains(full.Diagnostics[other], diagnostic => diagnostic.Message.Contains("missing"));
        var reused = Workspace.Analyze(root, documents, projects, bodyPaths: new HashSet<string>(Workspace.Paths) { main });
        Assert.Same(full.Models[main], reused.Models![main]);
        Assert.False(reused.IsPartial);
    }

    [Fact]
    public void ChangingTheFocusedFileDoesNotReuseAnIncompleteBodyModel()
    {
        Write("app.gfproj", ProjectFileFixture.Xml());
        string main = Write("main.gf", "int main() => Answer();");
        string other = Write("other.gf", "int Answer() => missing;");
        var documents = new Dictionary<string, OpenDocument>(Workspace.Paths) { [main] = new(main, File.ReadAllText(main), 1) };
        var projects = new ProjectWorkspace();
        var first = Workspace.Analyze(root, documents, projects, bodyPaths: new HashSet<string>(Workspace.Paths) { main });
        var second = Workspace.Analyze(root, documents, projects, bodyPaths: new HashSet<string>(Workspace.Paths) { other });
        Assert.True(second.IsPartial);
        Assert.NotSame(first.Models![main], second.Models![main]);
        Assert.Contains(second.Diagnostics[other], diagnostic => diagnostic.Message.Contains("missing"));
    }
}
