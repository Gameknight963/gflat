using System.Text.RegularExpressions;

namespace gflat.Tests;

public sealed class NativeTrimmingStandardTests
{
    [Fact]
    public void EmptyApplicationWithCompleteStandardEmitsOnlyMainAndNeedsNoNativeHelpers()
    {
        string root = Path.Combine(TestSupport.ProjectFileFixture.RepositoryRoot, "Standard");
        var sources = Directory.GetFiles(root, "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("main.gf", "int main() => 0;"));
        string ir = Compiler.Emit(sources, options: new() {
            NoStdLib = true, InternalizeFunctions = true, DebugSymbols = true,
        });
        Assert.Single(Regex.Matches(ir, @"(?m)^define "));
        Assert.Equal((false, false), NativeToolchain.RequiredNativeRuntimes(ir));
        Assert.Equal(0, CompilerTestHelper.RunIr(ir).ExitCode);
    }
}
