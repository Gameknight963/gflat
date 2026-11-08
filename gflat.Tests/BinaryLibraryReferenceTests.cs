using gflat.Projects;
using gflat.TestSupport;

namespace gflat.Tests;

public sealed class BinaryLibraryReferenceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "gflat-binary-reference-" + Guid.NewGuid().ToString("N"));
    private string Write(string name, string text)
    {
        string path = Path.Combine(root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }

    [Theory]
    [InlineData("StaticLibrary")]
    [InlineData("DynamicLibrary")]
    public void ProjectReferenceUsesGeneratedDeclarationsAndBinaryWithoutLoadingTheImplementation(string kind)
    {
        string implementation = Write("library/answer.gf", "namespace Example { int Answer(int value)=>value; int Answer(long value)=>1; }");
        string libraryProject = Write("library/library.gfproj", ProjectFileFixture.Xml(kind));
        string app = Write("app/app.gfproj", ProjectFileFixture.Xml(content: "<ItemGroup><ProjectReference Include=\"../library/library.gfproj\"/></ItemGroup>"));
        Write("app/main.gf", "namespace Example { double Answer(double value)=>value; } int main()=>Example::Answer(42);");
        Assert.Equal(0, Program.Main(["build", app, "--no-debug"]));
        var graph = ProjectGraph.Load(app);
        Assert.DoesNotContain(implementation, graph.Sources);
        var library = ProjectModel.Load(libraryProject);
        string metadata = Assert.Single(graph.BinaryInterfaces);
        Assert.Equal(library.TargetPath + ".gfi", metadata);
        Assert.DoesNotContain("=>", File.ReadAllText(metadata + ".gfh"));
        Assert.Equal(42, NativeToolchain.Run(graph.Root.TargetPath!, []).ExitCode);
        string output = Path.Combine(root, "standalone.exe");
        File.Delete(implementation);
        Assert.Equal(0, Program.Main([Path.Combine(root, "app/main.gf"), "--no-stdlib", "--library-interface", metadata, "-o", output]));
        Assert.Equal(42, NativeToolchain.Run(output, []).ExitCode);
    }

    [Fact]
    public void GeneratedCppHeaderCallsTheExportedFunctions()
    {
        string source = Write("library.gf", "namespace Example { int Answer(int value)=>value; } private int Hidden()=>7;");
        string library = Path.Combine(root, OperatingSystem.IsWindows() ? "answer.lib" : "libanswer.a");
        Assert.Equal(0, Program.Main([source, "--no-stdlib", "--output-type", "StaticLibrary", "-o", library]));
        Assert.DoesNotContain("Hidden", File.ReadAllText(library + ".gfi.gfh"));
        string cpp = Write("consumer.cpp", "#include \"" + library.Replace('\\', '/') + ".hpp\"\nint main() { return Example::Answer(42); }");
        string executable = Path.Combine(root, "consumer.exe");
        var linked = NativeToolchain.CompileCpp(cpp, executable, [library]);
        Assert.True(linked.ExitCode == 0, linked.StandardError);
        Assert.Equal(42, NativeToolchain.Run(executable, []).ExitCode);
    }

    [Fact]
    public void SourceDependenciesRemainPrivateToTheCompiledLibrary()
    {
        Write("common/helper.gf", "int Helper()=>42;");
        Write("common/common.gfproj", ProjectFileFixture.Xml("SourceLibrary"));
        Write("library/answer.gf", "int Answer()=>Helper();");
        string libraryProject = Write("library/library.gfproj", ProjectFileFixture.Xml("StaticLibrary",
            "<ItemGroup><ProjectReference Include=\"../common/common.gfproj\"/></ItemGroup>"));
        string app = Write("app/app.gfproj", ProjectFileFixture.Xml(content:
            "<ItemGroup><ProjectReference Include=\"../library/library.gfproj\"/><ProjectReference Include=\"../common/common.gfproj\"/></ItemGroup>"));
        Write("app/main.gf", "int main()=>Answer()+Helper();");
        Assert.Equal(0, Program.Main(["build", app, "--no-debug"]));
        var library = ProjectModel.Load(libraryProject);
        Assert.DoesNotContain("Helper", File.ReadAllText(library.TargetPath + ".gfi.gfh"));
        Assert.Equal(84, NativeToolchain.Run(ProjectModel.Load(app).TargetPath!, []).ExitCode);
    }

    [Fact]
    public void BinaryDeclarationsRetainConstantDefaults()
    {
        string source = Write("answer.gf", "int Answer(int value = 42)=>value;");
        string library = Path.Combine(root, OperatingSystem.IsWindows() ? "answer.lib" : "libanswer.a");
        Assert.Equal(0, Program.Main([source, "--no-stdlib", "--output-type", "StaticLibrary", "-o", library]));
        string consumer = Write("consumer.gf", "int main()=>Answer();");
        string executable = Path.Combine(root, "consumer.exe");
        Assert.Equal(0, Program.Main([consumer, "--no-stdlib", "--library-interface", library + ".gfi", "-o", executable]));
        Assert.Equal(42, NativeToolchain.Run(executable, []).ExitCode);
    }
}
