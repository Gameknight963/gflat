using System.Security;
using gflat.Projects;

namespace gflat.Tests;

public sealed class NativeLinkDriverTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RawCliLinksExplicitObjectsOrNamedArchivesWithoutShellInterpretation(bool named)
    {
        InDirectory(root => {
            string directory = Path.Combine(root, "native & semi; space"); Directory.CreateDirectory(directory);
            string obj = CompileObject(directory, "answer", "int native_answer(void) { return 42; }");
            string source = WriteSource(root);
            string output = Path.Combine(root, "program.exe");
            string[] options = named
                ? ["--native-library", "answer", "--native-library-dir", directory]
                : ["--native-library-file", obj];
            if (named) Archive(directory, "answer", obj);
            Assert.Equal(42, Program.Main([source, "--no-stdlib", "-o", output, "--run", .. options]));
        });
    }

    [Theory]
    [InlineData("--check")]
    [InlineData("--emit-ir")]
    public void NonLinkingModesAllowMissingNativeFiles(string mode)
    {
        InDirectory(root => {
            string source = WriteSource(root);
            string absent = Path.Combine(root, "not installed", "missing library.o");
            Assert.Equal(0, Program.Main([source, "--no-stdlib", mode, "--native-library-file", absent, "-o", Path.Combine(root, "program.ll")]));
        });
    }

    [Fact]
    public void ExplicitNativeFileIsProtectedFromRawOutputReplacement()
    {
        InDirectory(root => {
            string obj = CompileObject(root, "answer", "int native_answer(void) { return 42; }");
            byte[] original = File.ReadAllBytes(obj);
            Assert.NotEqual(0, Program.Main([WriteSource(root), "--no-stdlib", "--native-library-file", obj, "-o", obj]));
            Assert.Equal(original, File.ReadAllBytes(obj));
        });
    }

    [Fact]
    public void ExplicitProjectFilesSkipUnchangedBuildAndRelinkChangedNativeInput()
    {
        InDirectory(root => {
            string native = Path.Combine(root, "native & semi; space"); Directory.CreateDirectory(native);
            string obj = CompileObject(native, "answer", "int native_answer(void) { return 42; }");
            string project = Project(root, "<NativeLibraryFile Include='" + Item(Path.GetRelativePath(root, obj)) + "' />");
            Assert.Equal(0, Program.Main(["build", project]));
            string output = Output(root), stamp = Stamp(root);
            DateTime outputTime = File.GetLastWriteTimeUtc(output), stampTime = File.GetLastWriteTimeUtc(stamp);
            Assert.Equal(0, Program.Main(["build", project]));
            Assert.Equal(outputTime, File.GetLastWriteTimeUtc(output));
            Assert.Equal(stampTime, File.GetLastWriteTimeUtc(stamp));
            CompileObject(native, "answer", "int native_answer(void) { return 7; }");
            File.SetLastWriteTimeUtc(obj, stampTime.AddSeconds(1));
            Assert.Equal(0, Program.Main(["build", project]));
            Assert.Equal(7, NativeToolchain.Run(output, []).ExitCode);
        });
    }

    [Fact]
    public void LogicalProjectLibrariesRelinkConservatively()
    {
        InDirectory(root => {
            string native = Path.Combine(root, "native libs"); Directory.CreateDirectory(native);
            string obj = CompileObject(native, "answer", "int native_answer(void) { return 42; }");
            Archive(native, "answer", obj);
            string project = Project(root, "<NativeLibrary Include='answer'/><NativeLibraryDirectory Include='native libs'/>");
            Assert.Equal(0, Program.Main(["build", project]));
            Assert.False(File.Exists(Stamp(root)));
            CompileObject(native, "answer", "int native_answer(void) { return 7; }");
            Archive(native, "answer", obj);
            DateTime before = File.GetLastWriteTimeUtc(Output(root));
            Assert.Equal(0, Program.Main(["build", project]));
            Assert.NotEqual(before, File.GetLastWriteTimeUtc(Output(root)));
            Assert.False(File.Exists(Stamp(root)));
            Assert.Equal(7, NativeToolchain.Run(Output(root), []).ExitCode);
        });
    }

    [Fact]
    public void ProjectOutputAndCleanCannotDeleteExplicitNativeInputs()
    {
        InDirectory(root => {
            string obj = CompileObject(root, "answer", "int native_answer(void) { return 42; }");
            byte[] original = File.ReadAllBytes(obj);
            string project = Project(root, "<NativeLibraryFile Include='" + Item(obj) + "'/>");
            Assert.NotEqual(0, Program.Main(["build", project, "-o", obj]));
            Assert.Equal(original, File.ReadAllBytes(obj));
            File.WriteAllText(project, TestSupport.ProjectFileFixture.Xml(content:
                "<PropertyGroup><TargetPath>" + SecurityElement.Escape(obj) + "</TargetPath></PropertyGroup><ItemGroup><NativeLibraryFile Include='" + Item(obj) + "'/></ItemGroup>"));
            Assert.NotEqual(0, Program.Main(["clean", project]));
            Assert.Equal(original, File.ReadAllBytes(obj));
        });
    }

    [Fact]
    public void TransitiveRelativeArchivesLinkThroughBundledAndDirectMsBuild()
    {
        InDirectory(root => {
            string owner = Path.Combine(root, "owner"), dependency = Path.Combine(root, "dependency");
            Directory.CreateDirectory(owner); Directory.CreateDirectory(dependency);
            string ownerNative = Path.Combine(owner, "native & semi; space"); Directory.CreateDirectory(ownerNative);
            string dependencyNative = Path.Combine(dependency, "native & semi; space"); Directory.CreateDirectory(dependencyNative);
            string a = Archive(ownerNative, "owner", CompileObject(ownerNative, "owner", "extern int native_leaf(void); int native_answer(void) { return native_leaf(); }"));
            string b = Archive(dependencyNative, "leaf", CompileObject(dependencyNative, "leaf", "int native_leaf(void) { return 42; }"));
            File.WriteAllText(Path.Combine(owner, "owner.gfproj"), TestSupport.ProjectFileFixture.Xml("SourceLibrary", "<ItemGroup><nativeLibraryFile Include='" + Item(Path.GetRelativePath(owner, a)) + "'/><ProjectReference Include='../dependency/dependency.gfproj'/></ItemGroup>"));
            File.WriteAllText(Path.Combine(dependency, "dependency.gfproj"), TestSupport.ProjectFileFixture.Xml("SourceLibrary", "<ItemGroup><NaTiVeLiBrArYfIlE Include='" + Item(Path.GetRelativePath(dependency, b)) + "' Condition=\"'$(Configuration)' == 'Debug'\"/></ItemGroup>"));
            string project = Project(root, "<ProjectReference Include='owner/owner.gfproj'/>");
            Assert.Equal(new[] { a, b }, ProjectGraph.Load(project).NativeLinkInputs.Libraries.Select(library => library.Value));
            Assert.Equal(42, Program.Main(["build", project, "--run"]));
            var direct = NativeToolchain.Run("dotnet", ["msbuild", project, "-nologo", "-v:q", "-t:Rebuild"]);
            Assert.True(direct.ExitCode == 0, direct.StandardOutput + direct.StandardError);
            Assert.Equal(42, NativeToolchain.Run(Output(root), []).ExitCode);
        });
    }

    [Fact]
    public void MalformedNativeInputManifestIsAnOrdinaryDriverDiagnostic()
    {
        InDirectory(root => {
            string manifest = Path.Combine(root, "broken.xml"); File.WriteAllText(manifest, "<unclosed");
            string source = WriteSource(root);
            Assert.Equal(2, Program.Main([source, "--no-stdlib", "--check", "--native-link-inputs", manifest]));
        });
    }

    [Fact]
    public void IdenticalProjectCliOverridesDoNotInvalidateTheCacheThroughTemporaryManifestPaths()
    {
        InDirectory(root => {
            string obj = CompileObject(root, "answer", "int native_answer(void) { return 42; }");
            string project = Project(root, "");
            string[] arguments = ["build", project, "--native-library-file", obj];
            Assert.Equal(0, Program.Main(arguments));
            DateTime output = File.GetLastWriteTimeUtc(Output(root)), stamp = File.GetLastWriteTimeUtc(Stamp(root));
            Assert.Equal(0, Program.Main(arguments));
            Assert.Equal(output, File.GetLastWriteTimeUtc(Output(root)));
            Assert.Equal(stamp, File.GetLastWriteTimeUtc(Stamp(root)));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProjectNonLinkingModesAllowMissingNativeFiles(bool emitIr)
    {
        InDirectory(root => {
            string project = Project(root, "<NativeLibraryFile Include='not installed/native dependency.o'/>");
            string absentOverride = Path.Combine(root, "also not installed", "cli dependency.o");
            string[] arguments = emitIr
                ? ["build", project, "--emit-ir", "--native-library-file", absentOverride]
                : ["check", project, "--native-library-file", absentOverride];
            Assert.Equal(0, Program.Main(arguments));
        });
    }

    [Fact]
    public void NativeCompileProtectsExplicitInputsFromOutputsAndWindowsDebugSymbols()
    {
        InDirectory(root => {
            string ir = Path.Combine(root, "program.ll"), output = Path.Combine(root, "program.exe");
            File.WriteAllText(ir, "!llvm.dbg.cu = !{}\n");
            var collisions = new[] { output }.Concat(OperatingSystem.IsWindows() ? [Path.ChangeExtension(output, ".pdb")] : Array.Empty<string>());
            foreach (string collision in collisions)
            {
                File.WriteAllText(collision, "preserve native input");
                var inputs = NativeLinkInputs.Create([new("File", collision)], [], root);
                Assert.Throws<ArgumentException>(() => NativeToolchain.Compile(ir, output, clang: "must-not-run-missing-clang", nativeLibraries: inputs));
                Assert.Equal("preserve native input", File.ReadAllText(collision));
            }
        });
    }

    [Fact]
    public void RawDebugBuildProtectsLinkManifestFromOutputAndWindowsDebugSymbols()
    {
        InDirectory(root => {
            string source = Path.Combine(root, "main.gf"), output = Path.Combine(root, "app.exe");
            File.WriteAllText(source, "int main() => 0;");
            var collisions = new[] { output }.Concat(OperatingSystem.IsWindows() ? [Path.ChangeExtension(output, ".pdb")] : Array.Empty<string>());
            foreach (string manifest in collisions)
            {
                NativeLinkInputs.Empty.Write(manifest);
                byte[] original = File.ReadAllBytes(manifest);
                Assert.Equal(2, Program.Main([source, "--no-stdlib", "--debug", "-o", output, "--native-link-inputs", manifest]));
                Assert.Equal(original, File.ReadAllBytes(manifest));
            }
        });
    }

    [Theory]
    [InlineData("build")]
    [InlineData("check")]
    [InlineData("clean")]
    public void ProjectCommandsRejectPrivateNativeManifestOption(string command)
    {
        InDirectory(root => {
            string project = Project(root, ""), manifest = Path.Combine(root, "inputs.xml");
            NativeLinkInputs.Empty.Write(manifest);
            byte[] original = File.ReadAllBytes(manifest);
            string output = Output(root);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllText(output, "preserve existing project output");
            Assert.Equal(2, Program.Main([command, project, "--native-link-inputs", manifest]));
            Assert.Equal(original, File.ReadAllBytes(manifest));
            Assert.Equal("preserve existing project output", File.ReadAllText(output));
        });
    }

    private static string Project(string root, string nativeItems)
    {
        string project = Path.Combine(root, "app.gfproj");
        File.WriteAllText(project, TestSupport.ProjectFileFixture.Xml(content: "<ItemGroup>" + nativeItems + "</ItemGroup>"));
        WriteSource(root);
        return project;
    }
    private static string WriteSource(string root)
    {
        string source = Path.Combine(root, "main.gf");
        File.WriteAllText(source, "extern int native_answer(); int main() => native_answer();");
        return source;
    }
    private static string CompileObject(string root, string name, string source)
    {
        string code = Path.Combine(root, name + ".c"), obj = Path.Combine(root, name + ".o");
        File.WriteAllText(code, source);
        var result = NativeToolchain.Run(NativeToolchain.FindClang(), ["-x", "c", "-c", code, "-o", obj]);
        Assert.True(result.ExitCode == 0, result.StandardError);
        return obj;
    }
    private static string Archive(string root, string name, params string[] objects)
    {
        string clang = NativeToolchain.FindClang();
        string suffix = System.Text.RegularExpressions.Regex.Match(Path.GetFileNameWithoutExtension(clang), @"-\d+$").Value;
        string extension = OperatingSystem.IsWindows() ? ".exe" : "";
        string[] names = suffix.Length == 0 ? ["llvm-ar" + extension] : ["llvm-ar" + suffix + extension, "llvm-ar" + extension];
        string? realClang = new FileInfo(clang).ResolveLinkTarget(returnFinalTarget: true)?.FullName;
        var directories = new[] { Path.GetDirectoryName(clang)!, realClang == null ? "" : Path.GetDirectoryName(realClang)! }
            .Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            .Where(Directory.Exists).Distinct().ToArray();
        string? archiver = names.SelectMany(executable => directories.Select(directory => Path.Combine(directory, executable))).FirstOrDefault(File.Exists);
        Assert.True(archiver != null, "Native link tests require llvm-ar from the LLVM toolchain.");
        string archive = Path.Combine(root, OperatingSystem.IsWindows() ? name + ".lib" : "lib" + name + ".a");
        var result = NativeToolchain.Run(archiver!, ["rcs", archive, .. objects]);
        Assert.True(result.ExitCode == 0, result.StandardError);
        return archive;
    }
    private static string Item(string path) => SecurityElement.Escape(ProjectSdk.EscapePropertyValue(path))!;
    private static string Output(string root) => Path.Combine(root, "bin", "Debug", "app.exe");
    private static string Stamp(string root) => Path.Combine(root, "obj", "Debug", "app.build.success");
    private static void InDirectory(Action<string> action)
    {
        string root = Path.Combine(Path.GetTempPath(), "gflat-native-link-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { action(root); } finally { Directory.Delete(root, true); }
    }
}
