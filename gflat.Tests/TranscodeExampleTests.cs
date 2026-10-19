using System.Text;

namespace gflat.Tests;

public sealed class TranscodeExampleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ActualToolMatchesDotNetPreservesMalformedOutputAndCanConvertInPlace(int optimization)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("transcode.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Examples/transcode.gf"))));
        string directory = Path.Combine(Path.GetTempPath(), "gflat-transcode-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string ir = Path.Combine(directory, "transcode.ll"), executable = Path.Combine(directory, "transcode.exe");
            File.WriteAllText(ir, Compiler.Emit(sources, options: new() { InternalizeFunctions = true }));
            var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            var utf8 = new UTF8Encoding(false, true);
            var utf16 = new UnicodeEncoding(false, false, true);
            string input = Path.Combine(directory, "input file.bin"), output = Path.Combine(directory, "output file.bin");
            string roundtrip = Path.Combine(directory, "roundtrip.bin");
            foreach (string text in new[] { "", "a\0b", "hello\r\nworld", "\ufeff\u007f\u0080\u07ff\u0800\uffff\ud800\udc00\udbff\udfff", string.Concat(Enumerable.Repeat("A\0\u20ac\ud83d\ude00", 1600)) })
            {
                byte[] original = utf8.GetBytes(text), expected = utf16.GetBytes(text);
                File.WriteAllBytes(input, original);
                AssertSuccess("utf8-to-utf16le", input, output);
                Assert.Equal(expected, File.ReadAllBytes(output));
                AssertSuccess("utf16le-to-utf8", output, roundtrip);
                Assert.Equal(original, File.ReadAllBytes(roundtrip));
                AssertSuccess("utf8-to-utf16le", input, input);
                Assert.Equal(expected, File.ReadAllBytes(input));
                AssertSuccess("utf16le-to-utf8", input, input);
                Assert.Equal(original, File.ReadAllBytes(input));
            }
            foreach (var (mode, bytes) in new (string, byte[])[] {
                ("utf8-to-utf16le", [65, 0xc0, 0x80]),
                ("utf8-to-utf16le", [65, 0xf0, 0x90, 0x80]),
                ("utf8-to-utf16le", [65, 0xed, 0xa0, 0x80]),
                ("utf16le-to-utf8", [65]),
                ("utf16le-to-utf8", [65, 0, 0, 0xd8]),
                ("utf16le-to-utf8", [65, 0, 0, 0xdc]) })
            {
                byte[] sentinel = [1, 2, 3, 0, 255];
                File.WriteAllBytes(input, bytes); File.WriteAllBytes(output, sentinel);
                var failed = NativeToolchain.Run(executable, [mode, input, output], 30000);
                Assert.Equal(1, failed.ExitCode);
                Assert.Empty(failed.StandardOutput);
                Assert.NotEmpty(failed.StandardError);
                Assert.Equal(sentinel, File.ReadAllBytes(output));
                // Failed conversion is also safe when the paths are identical.
                failed = NativeToolchain.Run(executable, [mode, input, input], 30000);
                Assert.Equal(1, failed.ExitCode);
                Assert.Equal(bytes, File.ReadAllBytes(input));
            }
            byte[] kept = [9, 8, 7]; File.WriteAllBytes(output, kept);
            var missing = NativeToolchain.Run(executable, ["utf8-to-utf16le", Path.Combine(directory, "missing"), output], 30000);
            Assert.Equal(1, missing.ExitCode);
            Assert.Equal(kept, File.ReadAllBytes(output));
            foreach (string[] arguments in new[] { Array.Empty<string>(), new[] { "unknown", input, output } })
            {
                var usage = NativeToolchain.Run(executable, arguments, 30000);
                Assert.Equal(2, usage.ExitCode);
                Assert.Empty(usage.StandardOutput);
                Assert.Contains("Usage:", usage.StandardError);
            }

            void AssertSuccess(params string[] arguments)
            {
                var result = NativeToolchain.Run(executable, arguments, 30000);
                Assert.Equal(0, result.ExitCode);
                Assert.Empty(result.StandardOutput);
                Assert.Empty(result.StandardError);
            }
        }
        finally { Directory.Delete(directory, true); }
    }
}
