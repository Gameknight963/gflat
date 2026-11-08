using gflat.Projects;
using gflat.TestSupport;

namespace gflat.Tests;

public sealed class NativeLibraryOutputTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "gflat-library-output-" + Guid.NewGuid().ToString("N"));
    public NativeLibraryOutputTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, recursive: true);

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 2)]
    public void LibraryArtifactLinksWithoutItsImplementationSources(bool shared, int optimization)
    {
        string libraryIr = Path.Combine(root, "library.ll");
        File.WriteAllText(libraryIr, Compiler.Emit("int Answer(int value)=>value;", options: new() { NoStdLib = true }));
        string library = Path.Combine(root, shared ? (OperatingSystem.IsWindows() ? "answer.dll" : "libanswer.so") :
            (OperatingSystem.IsWindows() ? "answer.lib" : "libanswer.a"));
        var built = shared ? NativeToolchain.Compile(libraryIr, library, optimizationLevel: optimization, sharedLibrary: true) :
            NativeToolchain.CompileStaticLibrary(libraryIr, library, optimizationLevel: optimization);
        Assert.True(built.ExitCode == 0, built.StandardError);
        File.Delete(libraryIr);
        string consumerIr = Path.Combine(root, "consumer.ll"), executable = Path.Combine(root, "consumer.exe");
        File.WriteAllText(consumerIr, Compiler.Emit("extern int Answer(int value); int main()=>Answer(42);", options: new() { NoStdLib = true }));
        string linkFile = shared && OperatingSystem.IsWindows() ? Path.ChangeExtension(library, ".lib") : library;
        var linked = NativeToolchain.Compile(consumerIr, executable, optimizationLevel: optimization,
            nativeLibraries: new NativeLinkInputs([new("File", linkFile)], []));
        Assert.True(linked.ExitCode == 0, linked.StandardError);
        Assert.Equal(42, NativeToolchain.Run(executable, []).ExitCode);
    }

    [Theory]
    [InlineData("StaticLibrary")]
    [InlineData("DynamicLibrary")]
    public void ProjectBuildUsesNativeLibraryOutputAndSkipsUnchangedBuilds(string kind)
    {
        string project = Path.Combine(root, "library.gfproj");
        File.WriteAllText(project, ProjectFileFixture.Xml(kind));
        File.WriteAllText(Path.Combine(root, "answer.gf"), "int Answer()=>42;");
        var model = ProjectModel.Load(project);
        Assert.Equal(kind, model.Kind.ToString());
        Assert.Equal(0, Program.Main(["build", project, "--no-debug"]));
        Assert.True(File.Exists(model.TargetPath));
        DateTime written = File.GetLastWriteTimeUtc(model.TargetPath!);
        Assert.Equal(0, Program.Main(["build", project, "--no-debug"]));
        Assert.Equal(written, File.GetLastWriteTimeUtc(model.TargetPath!));
        Assert.Equal(0, Program.Main(["clean", project]));
        Assert.False(File.Exists(model.TargetPath));
        if (kind == "DynamicLibrary" && OperatingSystem.IsWindows())
        {
            Assert.False(File.Exists(Path.ChangeExtension(model.TargetPath, ".lib")));
            Assert.False(File.Exists(Path.ChangeExtension(model.TargetPath, ".exp")));
        }
    }
}
