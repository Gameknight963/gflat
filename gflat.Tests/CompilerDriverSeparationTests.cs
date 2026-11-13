namespace gflat.Tests;

public sealed class CompilerDriverSeparationTests
{
    [Fact]
    public void CompilerSuppliesStandardForSingleFilesAndCanOptOut()
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-compiler-driver-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string source = Path.Combine(directory, "hello.gf"), output = Path.Combine(directory, "hello.exe");
            File.WriteAllText(source, "int main(int argc,char** argv){Console::WriteLine(\"hello\");return argc == 2 && argv[1][0] == 'h' ? 42 : 1;}");
            Assert.Equal(0, CompilerProgram.Main([source, "--check"]));
            string list = Path.Combine(directory, "inputs.sources");
            File.WriteAllLines(list, [source, Path.Combine(ProjectCreation.FindToolchainRoot(), "Standard", "core", "Exception.gf")]);
            Assert.Equal(0, CompilerProgram.Main(["--source-list", list, "--check"]));
            Assert.Equal(42, CompilerProgram.Main([source, "-o", output, "--run", "--", "hello"]));
            Assert.Equal("hello" + Environment.NewLine, NativeToolchain.Run(output, ["hello"]).StandardOutput);
            Assert.Equal(1, CompilerProgram.Main([source, "--no-stdlib", "--check"]));
            Assert.Equal(2, Program.Main([source, "--check"]));
            string project = Path.Combine(directory, "project.gfproj");
            File.WriteAllText(project, "<Project Sdk=\"Gflat.Sdk\"/>");
            Assert.Equal(2, CompilerProgram.Main([project, "--check"]));
            File.WriteAllText(source, "int main()=>42;");
            Assert.Equal(42, CompilerProgram.Main([source, "--no-stdlib", "-o", output, "--run"]));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void AutomaticallyIncludedStandardSourcesCannotBeOverwritten()
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-compiler-output-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string source = Path.Combine(directory, "main.gf");
            string standardSource = Path.Combine(ProjectCreation.FindToolchainRoot(), "Standard", "core", "Exception.gf");
            string original = File.ReadAllText(standardSource);
            File.WriteAllText(source, "int main()=>0;");
            Assert.Equal(2, CompilerProgram.Main([source, "--emit-ir", "-o", standardSource]));
            Assert.Equal(original, File.ReadAllText(standardSource));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
