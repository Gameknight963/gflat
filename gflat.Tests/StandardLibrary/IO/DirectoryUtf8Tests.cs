using Xunit.Abstractions;

namespace gflat.Tests.Std.IO;

public sealed class DirectoryUtf8Tests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task WindowsUtf8CrtLocaleUsesLiteralUnicodeDirectoryNames(int optimization)
    {
        if (!OperatingSystem.IsWindows()) { output.WriteLine("The UTF-8 CRT locale case uses Windows UCRT."); return; }
        string temp = Path.GetTempPath();

        string directory = Path.Combine(temp, "gflat-directory-utf8-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            const string name = "é日本😀";
            string child = Path.Combine(directory, name), regular = Path.Combine(directory, name + "-file.bin");
            byte[] original = [0, 255, 65];
            File.WriteAllBytes(regular, original);
            string source = $$"""
                using Standard::IO;

                // Windows SDK ucrt/locale.h defines LC_CTYPE as 2.
                extern "C" char*? setlocale(int category, readonly(char)* locale) nothrow;
                int main(int argc, char** argv) {
                    if (setlocale(2, ".UTF8") == null) return 1;
                    readonly(char)* path = {{Literal(child)}};
                    readonly(char)* regular = {{Literal(regular)}};
                    if (argv[1][0] == 'c') {
                        Directory::CreateDirectory(path);
                        Directory::CreateDirectory(path);
                        if (!Directory::Exists(path)) return 2;
                        try { Directory::CreateDirectory(regular); return 3; } catch (IOException* error) {}
                    } else {
                        Directory::Remove(path);
                        if (Directory::Exists(path)) return 4;
                        try { Directory::Remove(regular); return 5; } catch (IOException* error) {}
                    }
                    return 0;
                }
                """;
            string[] selected = ["core/IO/IOException.gf", "libc/IO/Directory.gf"];
            var sources = selected.Select(path => new SourceFile(path,
                File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard", path))))
                .Append(new SourceFile("directory-utf8.gf", source));
            string ir = Path.Combine(directory, "program.ll"), executable = Path.Combine(directory, "program.exe");
            File.WriteAllText(ir, Compiler.Emit(sources, options: new() { InternalizeFunctions = true }));
            var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);

            Check(executable, "create");
            Assert.True(Directory.Exists(child));
            Assert.Equal(new[] { name }, Directory.GetDirectories(directory).Select(path => Path.GetFileName(path)).ToArray());
            Assert.Equal(original, File.ReadAllBytes(regular));

            Check(executable, "remove");
            Assert.False(Directory.Exists(child));
            Assert.Empty(Directory.GetDirectories(directory));
            Assert.Equal(original, File.ReadAllBytes(regular));
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory); }
    }

    private static string Literal(string value)
        => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static void Check(string executable, string mode)
    {
        var result = NativeToolchain.Run(executable, [mode], 30000);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }
}
