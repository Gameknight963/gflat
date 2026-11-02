using gflat.Projects;
using gflat.TestSupport;

namespace gflat.Tests;

public sealed class PreprocessorProjectTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "gflat-preprocessor-" + Guid.NewGuid().ToString("N"));
    private string Write(string name, string text)
    {
        string path = Path.GetFullPath(Path.Combine(root, name));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [Fact]
    public void CliOptionsAndDependencyOutputsPreserveInputFiles()
    {
        string header = Write("include/value.gfh", "#define ANSWER VALUE");
        string main = Write("main.gf", "#include <value.gfh>\nint main()=>ANSWER;");
        string ir = Path.Combine(root, "app.ll"), deps = Path.Combine(root, "app.includes");
        Assert.Equal(0, Program.Main([main, "--no-stdlib", "--emit-ir", "-o", ir, "-I" + Path.Combine(root, "include"), "-DVALUE=42", "--dependencies", deps]));
        Assert.Contains("ret i32 42", File.ReadAllText(ir));
        Assert.Contains(header, File.ReadAllLines(deps));
        Assert.Equal(2, Program.Main([main, "--no-stdlib", "--emit-ir", "-o", header, "-I", Path.Combine(root, "include"), "-D", "VALUE=42"]));
        Assert.Equal("#define ANSWER VALUE", File.ReadAllText(header));
        Assert.Equal(2, Program.Main([main, "--no-stdlib", "--emit-ir", "-o", ir, "-I", Path.Combine(root, "include"), "-DVALUE=42", "--dependencies", main]));
        Assert.StartsWith("#include", File.ReadAllText(main));
    }

    [Fact]
    public void ProjectsTrackTransitiveHeadersMissingCandidatesAndDefinitions()
    {
        string project = Write("app.gfproj", ProjectFileFixture.Xml(content: """
            <ItemGroup>
              <IncludeDirectory Include="include" />
              <Define Include="VALUE=42" />
            </ItemGroup>
            """));
        Write("main.gf", "#include \"value.gfh\"\nint main()=>ANSWER;");
        string transitive = Write("include/transitive.gfh", "#define ANSWER VALUE");
        Write("include/value.gfh", "#include \"transitive.gfh\"");
        string output = Path.Combine(root, "bin/Debug/app.ll");
        int Build() => Program.Main(["build", project, "--emit-ir"]);
        Assert.Equal(0, Build());
        Assert.Contains("ret i32 42", File.ReadAllText(output));
        DateTime first = File.GetLastWriteTimeUtc(output);
        Assert.Equal(0, Build());
        Assert.Equal(first, File.GetLastWriteTimeUtc(output));
        File.WriteAllText(transitive, "#define ANSWER 7");
        Assert.Equal(0, Build());
        Assert.Contains("ret i32 7", File.ReadAllText(output));
        string shadow = Write("value.gfh", "#define ANSWER 9");
        File.SetLastWriteTimeUtc(shadow, DateTime.UtcNow.AddYears(-1));
        Assert.Equal(0, Build());
        Assert.Contains("ret i32 9", File.ReadAllText(output));
        File.Delete(shadow);
        Assert.Equal(0, Build());
        Assert.Contains("ret i32 7", File.ReadAllText(output));
        File.Delete(transitive);
        Assert.NotEqual(0, Build());
        Write("include/transitive.gfh", "#define ANSWER VALUE");
        Assert.Equal(0, Program.Main(["build", project, "--emit-ir", "-DVALUE=11"]));
        Assert.Contains("ret i32 11", File.ReadAllText(output));
    }

    [Fact]
    public void ReferencedProjectsExposeIncludeDirectoriesAndKeepDefinitionsAtTheRoot()
    {
        Write("lib/lib.gfproj", ProjectFileFixture.Xml("SourceLibrary", "<ItemGroup><IncludeDirectory Include=\"headers\"/><Define Include=\"PRIVATE=1\"/><ProjectReference Include=\"child/child.gfproj\"/></ItemGroup>"));
        Write("lib/headers/value.gfh", "#define ANSWER 42");
        Write("lib/child/child.gfproj", ProjectFileFixture.Xml("SourceLibrary", "<ItemGroup><IncludeDirectory Include=\"headers\"/></ItemGroup>"));
        Write("lib/child/headers/value.gfh", "#define ANSWER 7");
        string project = Write("app.gfproj", ProjectFileFixture.Xml(content: "<ItemGroup><ProjectReference Include=\"lib/lib.gfproj\"/></ItemGroup>"));
        Write("main.gf", "#include <value.gfh>\n#ifdef PRIVATE\n#error leaked\n#endif\nint main()=>ANSWER;");
        var graph = ProjectGraph.Load(project);
        Assert.Contains(Path.GetFullPath(Path.Combine(root, "lib/headers")), graph.PreprocessorInputs.Directories, ProjectModel.Paths);
        Assert.Equal(Path.GetFullPath(Path.Combine(root, "lib/headers")), graph.PreprocessorInputs.Directories[0]);
        Assert.Empty(graph.PreprocessorInputs.Defines);
        Assert.Equal(0, Program.Main(["build", project, "--emit-ir"]));
    }
}
