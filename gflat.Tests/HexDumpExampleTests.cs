using System.Text;

namespace gflat.Tests;

public sealed class HexDumpExampleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ActualToolMatchesBinaryReferenceWithoutStringOrManagedAllocations(int optimization)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("hexdump.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Examples/hexdump.gf"))))
            .Append(new SourceFile("no-allocations.gf", """
                replace void*? Memory::Allocate(nuint size) => null;
                replace void Memory::Free(void* storage) nothrow {}
                """));
        string directory = Path.Combine(Path.GetTempPath(), "gflat-hexdump-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "hexdump.ll"), executable = Path.Combine(directory, "hexdump.exe");
            File.WriteAllText(input, Compiler.Emit(sources, options: new() { InternalizeFunctions = true }));
            var compiled = NativeToolchain.Compile(input, executable, optimizationLevel: optimization);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            foreach (int length in new[] { 0, 1, 15, 16, 17, 256, 4095, 4096, 4097, 12345 })
            {
                byte[] bytes = Enumerable.Range(0, length).Select(i => (byte)(i * 37 + 11)).ToArray();
                string path = Path.Combine(directory, $"input {length}.bin");
                File.WriteAllBytes(path, bytes);
                var result = NativeToolchain.Run(executable, [path], 30000);
                Assert.Equal(0, result.ExitCode);
                Assert.Empty(result.StandardError);
                Assert.Equal(Reference(bytes), result.StandardOutput.Replace("\r\n", "\n"));
                File.Delete(path);
            }
            string missing = Path.Combine(directory, "missing.bin");
            var failure = NativeToolchain.Run(executable, [missing], 30000);
            Assert.Equal(1, failure.ExitCode);
            Assert.Empty(failure.StandardOutput);
            Assert.Equal($"Unable to dump: {missing}\n", failure.StandardError.Replace("\r\n", "\n"));
            foreach (string[] arguments in new[] { Array.Empty<string>(), new[] { "a", "b" } })
            {
                var usage = NativeToolchain.Run(executable, arguments, 30000);
                Assert.Equal(2, usage.ExitCode);
                Assert.Empty(usage.StandardOutput);
                Assert.Equal("Usage: hexdump <file>\n", usage.StandardError.Replace("\r\n", "\n"));
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    private static string Reference(byte[] bytes)
    {
        var output = new StringBuilder();
        for (int start = 0; start < bytes.Length; start += 16)
        {
            output.Append(start.ToString("x16")).Append("  ");
            int length = Math.Min(16, bytes.Length - start);
            for (int column = 0; column < 16; column++)
                output.Append(column < length ? bytes[start + column].ToString("x2") + " " : "   ");
            output.Append(" |");
            for (int column = 0; column < 16; column++)
            {
                if (column >= length) output.Append(' ');
                else output.Append(bytes[start + column] is >= 32 and <= 126 ? (char)bytes[start + column] : '.');
            }
            output.Append("|\n");
        }
        return output.ToString();
    }
}
