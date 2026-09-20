namespace gflat.Tests;

public class MultiFileDriverTests
{
    [Fact]
    public void CliCompilesAndRunsSeveralFilesInEitherOrder()
    {
        InTemporaryDirectory(dir =>
        {
            string main = Path.Combine(dir, "main file.gf");
            string helper = Path.Combine(dir, "helper.gf");
            string output = Path.Combine(dir, "program.exe");
            File.WriteAllText(main, "int main() => Helpers::answer();");
            File.WriteAllText(helper, "namespace Helpers { int answer() => 42; }");
            Assert.Equal(42, Program.Main([main, helper, "-o", output, "--run"]));
            Assert.Equal(42, Program.Main([helper, main, "-o", output, "--run"]));
            string ir = Path.Combine(dir, "program.ll");
            Assert.Equal(0, Program.Main([main, helper, "--emit-ir", "-o", ir]));
            Assert.Contains("@gflat$Helpers$answer", File.ReadAllText(ir));
        });
    }

    [Fact]
    public void CliProtectsEveryInputAndRejectsDuplicatePaths()
    {
        InTemporaryDirectory(dir =>
        {
            string main = Path.Combine(dir, "main.gf");
            string helper = Path.Combine(dir, "helper.gf");
            File.WriteAllText(main, "int main() => 0;");
            File.WriteAllText(helper, "int helper() => 42;");
            Assert.Equal(2, Program.Main([main, helper, "--emit-ir", "-o", helper]));
            Assert.Equal(2, Program.Main([main, Path.Combine(dir, ".", "main.gf"), "--emit-ir"]));
            Assert.Equal("int helper() => 42;", File.ReadAllText(helper));
            Assert.Equal("int main() => 0;", File.ReadAllText(main));
        });
    }

    [Fact]
    public void CliErrorsIdentifyTheFileLineAndColumn()
    {
        InTemporaryDirectory(dir =>
        {
            string main = Path.Combine(dir, "main.gf");
            string broken = Path.Combine(dir, "broken.gf");
            string ir = Path.Combine(dir, "program.ll");
            File.WriteAllText(main, "int main() => helper();");
            File.WriteAllText(broken, "int helper() {\n    return missing;\n}");
            var result = NativeToolchain.Run("dotnet", [typeof(Program).Assembly.Location, main, broken, "--emit-ir", "-o", ir]);
            Assert.Equal(1, result.ExitCode);
            Assert.Contains(broken + "(2,12): error", result.StandardError);
            Assert.False(File.Exists(ir));
        });
    }

    [Fact]
    public void CliDuplicateErrorsIncludeThePreviousFile()
    {
        InTemporaryDirectory(dir =>
        {
            string first = Path.Combine(dir, "a.gf");
            string second = Path.Combine(dir, "b.gf");
            File.WriteAllText(first, "int main() => 1;");
            File.WriteAllText(second, "int main() => 2;");
            var result = NativeToolchain.Run("dotnet", [typeof(Program).Assembly.Location, first, second, "--emit-ir"]);
            Assert.Equal(1, result.ExitCode);
            Assert.Contains(second + "(1,1): error", result.StandardError);
            Assert.Contains(first + "(1,1): note: previous declaration", result.StandardError);
        });
    }

    private static void InTemporaryDirectory(Action<string> action)
    {
        string dir = Path.Combine(Path.GetTempPath(), "gflat_multifile_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try { action(dir); }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
