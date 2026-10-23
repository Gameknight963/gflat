namespace gflat.Tests;

public sealed class SequenceExampleTests
{
    [Theory]
    [InlineData("-3", "7", "--even", 0, "-2\n0\n2\n")]
    [InlineData("-2", "3", null, 0, "-2\n-1\n0\n")]
    [InlineData("0", "0", null, 0, "")]
    [InlineData("2147483647", "1", null, 0, "2147483647\n")]
    [InlineData("bad", "1", null, 1, null)]
    [InlineData("0", "-1", null, 1, null)]
    [InlineData("2147483647", "2", null, 1, null)]
    [InlineData("0", "3", "--bad", 2, "Usage: seq <start> <count> [--even]\n")]
    public void ExampleParsesFiltersAndPrints(string start, string count, string? option, int status, string? expected)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-seq-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var sources = ConsoleWriterTests.Sources(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Examples", "seq.gf")))
                .Concat(new[] { "core/CString.gf", "core/Collections/IEnumerable.gf", "core/Collections/Enumerable.gf", "core/Parsing.gf", "core/IntRange.gf", "core/FilteredIntRange.gf" }.Select(name =>
                    new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", name)))));
            string ir = Path.Combine(directory, "seq.ll"), executable = Path.Combine(directory, "seq.exe");
            File.WriteAllText(ir, Compiler.Emit(sources));
            var compilation = NativeToolchain.Compile(ir, executable);
            Assert.Equal(0, compilation.ExitCode);
            var arguments = new List<string> { start, count };
            if (option != null) arguments.Add(option);
            var result = NativeToolchain.Run(executable, arguments);
            Assert.Equal(status, result.ExitCode);
            if (expected != null) Assert.Equal(expected, result.StandardOutput.Replace("\r\n", "\n"));
            Assert.Empty(result.StandardError);
        }
        finally { Directory.Delete(directory, true); }
    }
}
