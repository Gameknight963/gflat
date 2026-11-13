namespace gflat.Tests;

public sealed class CompilerDriverSeparationTests
{
    [Fact]
    public void CompilerUsesExplicitInputsWhileTheDriverSuppliesStandardForSingleFiles()
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-compiler-driver-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string source = Path.Combine(directory, "hello.gf"), output = Path.Combine(directory, "hello.exe");
            File.WriteAllText(source, "int main(){Console::WriteLine(\"hello\");return 0;}");
            Assert.Equal(1, CompilerProgram.Main([source, "--check"]));
            Assert.Equal(0, Program.Main(["check", source]));
            Assert.Equal(0, Program.Main([source, "-o", output]));
            Assert.Equal("hello" + Environment.NewLine, NativeToolchain.Run(output, []).StandardOutput);
            Assert.Equal(1, Program.Main([source, "--no-stdlib", "--check"]));
            string project = Path.Combine(directory, "project.gfproj");
            File.WriteAllText(project, "<Project Sdk=\"Gflat.Sdk\"/>");
            Assert.Equal(2, CompilerProgram.Main([project, "--check"]));
            File.WriteAllText(source, "int main()=>42;");
            Assert.Equal(42, CompilerProgram.Main([source, "-o", output, "--run"]));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
