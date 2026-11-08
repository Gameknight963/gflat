using gflat.TestSupport;

namespace gflat.LanguageServer.Tests;

public sealed class WorkspaceAnalysisReuseTests : IDisposable
{
    [Fact]
    public void BinaryInterfacesSupportNavigationAndInvalidateWhenRebuilt()
    {
        string implementation = Path.Combine(root, "library.gf"), metadata = Path.Combine(root, "library.lib.gfi");
        void Publish(string code)
        {
            var source = new SourceFile(implementation, code);
            var program = Compiler.EmitProgram([source], options: new() { NoStdLib = true, InternalizeFunctions = true, ExportSources = [implementation] });
            Directory.CreateDirectory(root);
            File.WriteAllText(metadata, program.Interface!.ToXml().ToString());
            File.WriteAllText(metadata + ".gfh", program.Interface.HeaderText);
        }
        Publish("int Answer(int value)=>value;");
        var caller = new SourceFile(Path.Combine(root, "main.gf"), "int main()=>Answer(42);");
        var options = new CompilationOptions { NoStdLib = true, BinaryInterfaces = [metadata] };
        var cache = new WorkspaceAnalysisCache();
        var first = cache.Get([caller], options);
        Assert.Empty(first.Snapshot.Diagnostics);
        int offset = caller.Text.IndexOf("Answer", StringComparison.Ordinal);
        Assert.Equal(Workspace.FileUri(metadata + ".gfh"), Assert.IsType<Location>(first.Model.Definition(caller.Path, offset)).Uri);
        Assert.DoesNotContain(first.Snapshot.Sources, source => Workspace.Paths.Equals(source.Path, implementation));
        Assert.Same(first.Snapshot, cache.Get([caller], options with { BinaryInterfaces = [metadata] }).Snapshot);
        Publish("bool Answer(int value)=>true;");
        var changed = cache.Get([caller], options);
        Assert.NotSame(first.Snapshot, changed.Snapshot);
        Assert.NotEmpty(changed.Snapshot.Diagnostics);
    }

    [Fact]
    public void DeclarationScopeChangesInvalidateAnalysisWithoutDefeatingReuse()
    {
        var implementation = new SourceFile(Path.GetFullPath("scoped-cache-implementation.gf"), "int Answer()=>42;");
        var caller = new SourceFile(Path.GetFullPath("scoped-cache-main.gf"), "int main()=>Answer();");
        var scopes = new Dictionary<string, DeclarationScope> { [implementation.Path] = DeclarationScope.Project };
        var options = new CompilationOptions { NoStdLib = true, FileDeclarationScopes = scopes };
        var cache = new WorkspaceAnalysisCache();
        var first = cache.Get([implementation, caller], options);
        Assert.Empty(first.Snapshot.Diagnostics);
        Assert.Same(first.Snapshot, cache.Get([implementation, caller], options with {
            FileDeclarationScopes = new Dictionary<string, DeclarationScope>(scopes) }).Snapshot);
        scopes[implementation.Path] = DeclarationScope.File;
        var changed = cache.Get([implementation, caller], options);
        Assert.NotSame(first.Snapshot, changed.Snapshot);
        Assert.NotEmpty(changed.Snapshot.Diagnostics);
        Assert.Equal(DeclarationScope.Project, first.Snapshot.Options.FileDeclarationScopes[implementation.Path]);
    }

    [Fact]
    public void FileScopedFunctionsAreExcludedFromOtherFilesCompletion()
    {
        var implementation = new SourceFile(Path.GetFullPath("scoped-completion-implementation.gf"), "#scope file\nint HiddenAnswer()=>42;");
        var caller = new SourceFile(Path.GetFullPath("scoped-completion-main.gf"), "int main() { Hidden; return 0; }");
        var cache = new WorkspaceAnalysisCache();
        var result = cache.Get([implementation, caller], new() { NoStdLib = true });
        var json = System.Text.Json.JsonSerializer.SerializeToElement(result.Model.Completion(caller.Path, caller.Text.IndexOf("Hidden", StringComparison.Ordinal) + 6));
        Assert.DoesNotContain(json.GetProperty("items").EnumerateArray(), item => item.GetProperty("label").GetString() == "HiddenAnswer");
    }

    [Fact]
    public void MutatingMacroOptionsDoesNotReuseThePreviousAnalysis()
    {
        var source = new SourceFile(Path.GetFullPath("mutable-cache-defines.gf"), "int Read()=>VALUE;");
        var defines = new Dictionary<string, string> { ["VALUE"] = "1" };
        var options = new CompilationOptions { NoStdLib = true, Defines = defines, IncludeLoader = path => path == source.Path ? source : null };
        var cache = new WorkspaceAnalysisCache();
        var first = cache.Get([source], options);
        Assert.Empty(first.Snapshot.Diagnostics);
        Assert.Same(first.Snapshot, cache.Get([source], options).Snapshot);
        defines["VALUE"] = "missing";
        var changed = cache.Get([source], options);
        Assert.NotSame(first.Snapshot, changed.Snapshot);
        Assert.Contains(changed.Snapshot.Diagnostics, diagnostic => diagnostic.Message.Contains("missing", StringComparison.Ordinal));
        Assert.Equal("1", first.Snapshot.Options.Defines["VALUE"]);
        Assert.Same(changed.Snapshot, cache.Get([source], options).Snapshot);
    }

    [Fact]
    public void MutatingIncludeDirectoriesDoesNotReuseThePreviousHeader()
    {
        string firstHeader = Write("first/value.gfh", "int Read()=>1;");
        string secondHeader = Write("second/value.gfh", "int Read()=>missing;");
        var source = new SourceFile(Path.Combine(root, "main.gf"), "#include <value.gfh>\nint main()=>Read();");
        var directories = new List<string> { Path.GetDirectoryName(firstHeader)! };
        var options = new CompilationOptions { NoStdLib = true, IncludeDirectories = directories, IncludeLoader = path => path == source.Path ? source : null };
        var cache = new WorkspaceAnalysisCache();
        var first = cache.Get([source], options);
        Assert.Empty(first.Snapshot.Diagnostics);
        Assert.Same(first.Snapshot, cache.Get([source], options).Snapshot);
        directories[0] = Path.GetDirectoryName(secondHeader)!;
        var changed = cache.Get([source], options);
        Assert.NotSame(first.Snapshot, changed.Snapshot);
        Assert.Contains(changed.Snapshot.Diagnostics, diagnostic => diagnostic.Message.Contains("missing", StringComparison.Ordinal));
        Assert.Equal(Path.GetDirectoryName(firstHeader), first.Snapshot.Options.IncludeDirectories[0]);
        Assert.Same(changed.Snapshot, cache.Get([source], options).Snapshot);
    }

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
