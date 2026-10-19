namespace gflat.Tests;

public class DebugInfoTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DebugInformationSurvivesNativeCompilation(bool standardLibrary)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat_debug_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "source with spaces.gf");
            string source = """
                struct Pair { public int first; public int second; }
                int Read(ref int value) { return value; }
                int main() {
                    Pair pair;
                    pair.first = 21;
                    pair.second = 21;
                    char[4] buffer;
                    int answer = Read(ref pair.first) + pair.second;
                    return answer;
                }
                """;
            File.WriteAllText(path, source, new System.Text.UTF8Encoding(standardLibrary));
            string ir = Compiler.Emit([new(path, source)], options: new() { DebugSymbols = true, NoStdLib = !standardLibrary, InternalizeFunctions = true });
            Assert.Contains("!DICompileUnit", ir);
            Assert.Contains(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))), ir);
            Assert.Contains("!DILexicalBlock", ir);
            Assert.Contains("!DILocalVariable(name: \"answer\"", ir);
            Assert.Contains("!DICompositeType(tag: DW_TAG_structure_type", ir);
            string ll = Path.Combine(directory, "test.ll"), exe = Path.Combine(directory, "test.exe");
            File.WriteAllText(ll, ir);
            var result = NativeToolchain.Compile(ll, exe);
            Assert.True(result.ExitCode == 0, result.StandardError);
            Assert.DoesNotContain("invalid debug", result.StandardError, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(42, NativeToolchain.Run(exe, []).ExitCode);
            if (OperatingSystem.IsWindows()) Assert.True(File.Exists(Path.ChangeExtension(exe, ".pdb")));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void SymbolsAreOptInForCompilerApi()
        => Assert.DoesNotContain("!llvm.dbg.cu", Compiler.Emit("int main() { return 0; }"));

    [Fact]
    public void ProjectDebugFlagsAndPdbRebuildAndClean()
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat_debug_project_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string project = Path.Combine(directory, "App.gfproj");
            File.WriteAllText(project, "<Project Sdk=\"Gflat.Sdk\"><PropertyGroup><NoStdLib>true</NoStdLib></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(directory, "Main.gf"), "int main() => 42;");
            Assert.Equal(0, Program.Main(["build", project, "--emit-ir", "--no-debug"]));
            Assert.DoesNotContain("!llvm.dbg.cu", File.ReadAllText(Path.Combine(directory, "bin", "Debug", "App.ll")));
            Assert.Equal(0, Program.Main(["build", project, "--configuration", "Release", "--emit-ir", "--debug"]));
            Assert.Contains("!llvm.dbg.cu", File.ReadAllText(Path.Combine(directory, "bin", "Release", "App.ll")));
            if (OperatingSystem.IsWindows())
            {
                string pdb = Path.Combine(directory, "bin", "Debug", "App.pdb");
                Assert.Equal(0, Program.Main(["build", project]));
                Assert.True(File.Exists(pdb));
                File.Delete(pdb);
                Assert.Equal(0, Program.Main(["build", project]));
                Assert.True(File.Exists(pdb));
                Assert.Equal(0, Program.Main(["clean", project]));
                Assert.False(File.Exists(pdb));
            }
        }
        finally { Directory.Delete(directory, true); }
    }
}
