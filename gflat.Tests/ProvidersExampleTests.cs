namespace gflat.Tests;

public sealed class ProvidersExampleTests
{
    [Fact]
    public void ConfiguredChecksumExampleRunsAcrossFiles()
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Examples/providers"), "*.gf")
            .OrderBy(path => path).Select(path => new SourceFile(path, File.ReadAllText(path)));
        Assert.Equal(0, CompilerTestHelper.Run(sources).ExitCode);
    }
}
