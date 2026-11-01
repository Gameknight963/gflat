using System.Text;

namespace gflat.Tests;

public sealed class CsvSelectExampleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ActualToolReordersHeaderNamedColumnsAndValidatesBeforeExclusiveCreation(int optimization)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-csv-select-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string executable = Compile(directory, optimization);
            string input = Path.Combine(directory, "input file.csv"), output = Path.Combine(directory, "output file.csv");
            (byte[] Input, string[] Names, byte[] Expected)[] valid = [
                (Bytes("id,name,note\n1,\"a,b\",\"two\r\nlines\"\n2,,\"x\"\"y\"\n"), ["note", "name"],
                    Bytes("note,name\r\n\"two\r\nlines\",\"a,b\"\r\n\"x\"\"y\",\r\n")),
                ([.. Bytes("left,right\n"), 0, 255, 44, 128, 10], ["right", "left"], [.. Bytes("right,left\r\n"), 128, 44, 0, 255, 13, 10]),
                (Bytes("a,b\n"), ["b", "a"], Bytes("b,a\r\n")),
                (Bytes(",value\nx,y\n"), [""], Bytes("\r\nx\r\n")),
                (Bytes("h,h,v\n1,2,3"), ["v"], Bytes("v\r\n3\r\n")),
                (Bytes("a,b\n1,2"), ["b", "a", "b"], Bytes("b,a,b\r\n2,1,2\r\n"))];
            foreach (var sample in valid)
            {
                File.WriteAllBytes(input, sample.Input);
                var result = NativeToolchain.Run(executable, [input, output, .. sample.Names], 30000);
                Assert.True(result.ExitCode == 0, result.StandardError);
                Assert.Empty(result.StandardOutput);
                Assert.Empty(result.StandardError);
                Assert.Equal(sample.Expected, File.ReadAllBytes(output));
                Assert.Equal(sample.Input, File.ReadAllBytes(input));
                File.Delete(output);
            }
            (string Input, string[] Names)[] invalid = [
                ("", ["a"]), ("a,b\n1,2", ["missing"]), ("a,a\n1,2", ["a"]),
                ("a,b\n1", ["a"]), ("a,b\n1,2,3", ["a"]),
                ("a,b\n1,2\n\"unterminated", ["b"]), ("a,b\n1,2\nbad\"quote,3", ["a"]),
                ("a,b\n1,2\nlast\r", ["a"]), ("a,b\n", ["A"])];
            byte[] sentinel = [0, 255, 65];
            foreach (var sample in invalid)
            {
                File.WriteAllBytes(input, Bytes(sample.Input));
                var absent = NativeToolchain.Run(executable, [input, output, .. sample.Names], 30000);
                Assert.Equal(2, absent.ExitCode);
                Assert.NotEmpty(absent.StandardError);
                Assert.False(File.Exists(output));
                File.WriteAllBytes(output, sentinel);
                var existing = NativeToolchain.Run(executable, [input, output, .. sample.Names], 30000);
                Assert.Equal(2, existing.ExitCode);
                Assert.Equal(sentinel, File.ReadAllBytes(output));
                File.Delete(output);
            }
            File.WriteAllBytes(input, Bytes("a,b\n1,2\n"));
            File.WriteAllBytes(output, sentinel);
            var preserved = NativeToolchain.Run(executable, [input, output, "a"], 30000);
            Assert.Equal(1, preserved.ExitCode);
            Assert.Equal(sentinel, File.ReadAllBytes(output));
            byte[] before = File.ReadAllBytes(input);
            var same = NativeToolchain.Run(executable, [input, input, "a"], 30000);
            Assert.Equal(1, same.ExitCode);
            Assert.Equal(before, File.ReadAllBytes(input));
            foreach (string[] arguments in new string[][] { [], [input], [input, output] })
            {
                var usage = NativeToolchain.Run(executable, arguments, 30000);
                Assert.Equal(2, usage.ExitCode);
                Assert.Contains("Usage:", usage.StandardError);
            }
            var missing = NativeToolchain.Run(executable, [Path.Combine(directory, "missing.csv"), output, "a"], 30000);
            Assert.Equal(1, missing.ExitCode);
            Assert.Equal(sentinel, File.ReadAllBytes(output));
            File.Delete(output);
            var missingAbsent = NativeToolchain.Run(executable, [Path.Combine(directory, "missing.csv"), output, "a"], 30000);
            Assert.Equal(1, missingAbsent.ExitCode);
            Assert.False(File.Exists(output));
            const long oversizedLength = 64L * 1024 * 1024 + 1;
            using (var oversized = new FileStream(input, FileMode.Create, FileAccess.Write)) oversized.SetLength(oversizedLength);
            var oversizedInput = NativeToolchain.Run(executable, [input, output, "a"], 30000);
            Assert.Equal(2, oversizedInput.ExitCode);
            Assert.Contains("64 MiB", oversizedInput.StandardError);
            Assert.False(File.Exists(output));
            Assert.Equal(oversizedLength, new FileInfo(input).Length);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory); }
    }

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    private static string Compile(string directory, int optimization)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Standard"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("csv-select.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Examples/csv-select.gf"))));
        string ir = Path.Combine(directory, "program.ll"), executable = Path.Combine(directory, "csv-select.exe");
        File.WriteAllText(ir, Compiler.Emit(sources, options: new() { InternalizeFunctions = true }));
        var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
        Assert.True(compiled.ExitCode == 0, compiled.StandardError);
        return executable;
    }
}
