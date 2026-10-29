namespace gflat.Tests;

public sealed class DriverOutputProtectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IrOutputCannotReplaceAnInputSourceListThroughNormalizedPaths(bool alias)
    {
        WithFixture(root =>
        {
            string source = WriteSource(root);
            string list = WriteList(root, "inputs with spaces.sources", source);
            byte[] original = File.ReadAllBytes(list);
            string output = alias ? Path.Combine(root, "unused", "..", Path.GetFileName(list)) : list;
            var result = Run(root, ["--source-list", list, "--no-stdlib", "--emit-ir", "-o", output]);
            AssertRejected(result);
            Assert.Equal(original, File.ReadAllBytes(list));
            Assert.Equal("int main() nothrow => 0;", File.ReadAllText(source));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WindowsDebugPdbProtectsEverySuppliedSourceList(bool pdbIsFirst)
    {
        WithFixture(root =>
        {
            string main = WriteSource(root);
            string helper = Path.Combine(root, "helper.gf");
            File.WriteAllText(helper, "int helper() nothrow => 1;");
            string first = WriteList(root, pdbIsFirst ? "app.pdb" : "first.sources", main);
            string second = WriteList(root, pdbIsFirst ? "second.sources" : "app.pdb", helper);
            byte[] originalFirst = File.ReadAllBytes(first);
            byte[] originalSecond = File.ReadAllBytes(second);
            string missing = MissingCompiler(root);
            var result = Run(root, ["--source-list", first, "--source-list", second, "--no-stdlib", "--debug",
                "-o", Path.Combine(root, "app.exe"), "--clang", missing]);
            if (OperatingSystem.IsWindows()) AssertRejected(result);
            else AssertReachedMissingCompiler(result, missing);
            Assert.Equal(originalFirst, File.ReadAllBytes(first));
            Assert.Equal(originalSecond, File.ReadAllBytes(second));
            Assert.Equal("int main() nothrow => 0;", File.ReadAllText(main));
            Assert.Equal("int helper() nothrow => 1;", File.ReadAllText(helper));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourcePdbProtectionFollowsTheFinalDebugOptionAndHost(bool debug)
    {
        WithFixture(root =>
        {
            string main = WriteSource(root);
            string helper = Path.Combine(root, "app.pdb");
            File.WriteAllText(helper, "int helper() nothrow => 1;");
            byte[] original = File.ReadAllBytes(helper);
            string output = Path.Combine(root, "app.exe");
            File.WriteAllText(output, "preserve existing output");
            string[] flags = debug ? ["--no-debug", "--debug"] : ["--debug", "--no-debug"];
            string missing = MissingCompiler(root);
            var result = Run(root, [main, helper, "--no-stdlib", .. flags, "-o", output, "--clang", missing]);
            if (debug && OperatingSystem.IsWindows()) AssertRejected(result);
            else AssertReachedMissingCompiler(result, missing);
            Assert.Equal(original, File.ReadAllBytes(helper));
            Assert.Equal("preserve existing output", File.ReadAllText(output));
            Assert.Equal("int main() nothrow => 0;", File.ReadAllText(main));
        });
    }

    [Theory]
    [InlineData(false, "--emit-ir")]
    [InlineData(true, "--emit-ir")]
    [InlineData(false, "--check")]
    [InlineData(true, "--check")]
    public void ModesWithoutPdbOutputAllowPdbNamedSourcesAndLists(bool list, string mode)
    {
        WithFixture(root =>
        {
            string main = WriteSource(root);
            string input = Path.Combine(root, "app.pdb");
            string[] inputs;
            if (list)
            {
                WriteList(root, "app.pdb", main);
                inputs = ["--source-list", input];
            }
            else
            {
                File.WriteAllText(input, "int helper() nothrow => 1;");
                inputs = [main, input];
            }
            byte[] original = File.ReadAllBytes(input);
            string output = Path.Combine(root, "app.exe");
            var result = Run(root, [.. inputs, "--no-stdlib", "--debug", mode, "-o", output]);
            Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
            Assert.Equal(original, File.ReadAllBytes(input));
            if (mode == "--emit-ir") Assert.Contains("!llvm.dbg.cu", File.ReadAllText(output));
            else Assert.False(File.Exists(output));
            Assert.Equal("int main() nothrow => 0;", File.ReadAllText(main));
        });
    }

    [Fact]
    public void NoncollidingSourceListStillCompilesEveryListedSourceToIr()
    {
        WithFixture(root =>
        {
            string main = WriteSource(root, "int main() nothrow => helper();");
            string helper = Path.Combine(root, "helper.gf");
            File.WriteAllText(helper, "int helper() nothrow => 42;");
            string list = WriteList(root, "inputs.sources", main, helper);
            byte[] original = File.ReadAllBytes(list);
            string output = Path.Combine(root, "result.ll");
            var result = Run(root, ["--source-list", list, "--no-stdlib", "--emit-ir", "-o", output]);
            Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
            string ir = File.ReadAllText(output);
            Assert.Contains("define i32 @main", ir);
            Assert.Contains("@gflat$helper", ir);
            Assert.Equal(original, File.ReadAllBytes(list));
            Assert.Equal("int main() nothrow => helper();", File.ReadAllText(main));
            Assert.Equal("int helper() nothrow => 42;", File.ReadAllText(helper));
        });
    }

    [Theory]
    [InlineData("build")]
    [InlineData("check")]
    [InlineData("clean")]
    public void ProjectsViaSourceListAreRejectedBeforeProjectEvaluationOrClean(string command)
    {
        WithFixture(root =>
        {
            string project = Path.Combine(root, "app.gfproj");
            // If the driver evaluates this file first, its XML error is the wrong diagnostic.
            File.WriteAllText(project, "not valid XML; project evaluation must not start");
            string list = WriteList(root, "projects.sources", project);
            string source = WriteSource(root);
            byte[] originalProject = File.ReadAllBytes(project);
            byte[] originalList = File.ReadAllBytes(list);
            string output = Path.Combine(root, "bin", "Debug", "app.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllText(output, "preserve existing project output");
            var result = Run(root, [command, "--source-list", list]);
            Assert.Equal(2, result.ExitCode);
            Assert.Contains("--source-list is a source-build option. Pass project files directly.", result.StandardError);
            Assert.DoesNotContain("Internal compiler error", result.StandardError);
            Assert.Equal(originalProject, File.ReadAllBytes(project));
            Assert.Equal(originalList, File.ReadAllBytes(list));
            Assert.Equal("preserve existing project output", File.ReadAllText(output));
            Assert.Equal("int main() nothrow => 0;", File.ReadAllText(source));
        });
    }

    private static string WriteSource(string root, string contents = "int main() nothrow => 0;")
    {
        string path = Path.Combine(root, "main.gf");
        File.WriteAllText(path, contents);
        return path;
    }

    private static string WriteList(string root, string name, params string[] sources)
    {
        string path = Path.Combine(root, name);
        File.WriteAllLines(path, sources);
        return path;
    }

    private static string MissingCompiler(string root)
        => Path.Combine(root, "must-not-run-missing-clang" + (OperatingSystem.IsWindows() ? ".exe" : ""));

    private static ProcessResult Run(string root, string[] arguments)
    {
        var result = NativeToolchain.Run("dotnet", [typeof(Program).Assembly.Location, .. arguments], 30000);
        File.WriteAllText(Path.Combine(root, "run.stdout"), result.StandardOutput);
        File.WriteAllText(Path.Combine(root, "run.stderr"), result.StandardError);
        return result;
    }

    private static void AssertRejected(ProcessResult result)
    {
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Output must differ from", result.StandardError);
        Assert.DoesNotContain("Internal compiler error", result.StandardError);
    }

    private static void AssertReachedMissingCompiler(ProcessResult result, string missing)
    {
        Assert.Equal(2, result.ExitCode);
        Assert.DoesNotContain("Output must differ from", result.StandardError);
        Assert.Contains(missing, result.StandardError);
    }

    private static void WithFixture(Action<string> action)
    {
        string root = Path.Combine(Path.GetTempPath(), "gflat-driver-output-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { action(root); }
        catch (Exception error)
        {
            error.Data["DriverOutputDirectory"] = root;
            throw;
        }
        Directory.Delete(root, recursive: true);
    }
}