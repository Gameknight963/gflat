namespace gflat.Tests.Std.IO;

public sealed class FileLengthTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void LengthIncludesBufferedOutputAndPreservesLookaheadPositionAndEof(int optimization)
        => Run("""
            using Standard::IO;

            int main() {
                FileStream file = FileStream::OpenTemporary();
                if (file.Length != 0l || file.Position != 0l) return 1;
                file.Write("abc");
                if (file.Length != 3l || file.Position != 3l) return 2;
                file.Position = 1l;
                if (file.PeekByte() != 98 || file.Position != 1l) return 3;
                readonly(FileStream)* view = &file;
                if (view.Length != 3l || view.Position != 1l || file.ReadByte() != 98) return 4;
                if (file.ReadByte() != 99 || file.ReadByte() != -1 || !file.EndOfFile) return 5;
                if (file.Length != 3l || !file.EndOfFile || file.Position != 3l) return 6;
                file.Position = 20l;
                if (file.Length != 3l || file.Position != 20l) return 7;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ResizePreservesPositionDropsTruncatedInputAndZeroFillsExtensions(int optimization)
        => Run("""
            using Standard::IO;

            int main() {
                FileStream file = FileStream::OpenTemporary();
                file.Write("abcdef");
                file.Position = 0l;
                if (file.ReadByte() != 97 || file.PeekByte() != 98) return 10;
                file.SetLength(2l);
                if (file.Length != 2l || file.Position != 1l) return 11;
                if (file.ReadByte() != 98 || file.ReadByte() != -1) return 12;
                file.SetLength(6l);
                if (file.Position != 2l || file.Length != 6l || file.EndOfFile) return 13;
                for (int index = 0; index < 4; index++) if (file.ReadByte() != 0) return 14;
                if (file.ReadByte() != -1) return 15;
                file.SetLength(1l);
                if (file.Position != 6l || file.Length != 1l) return 16;
                file.Position = 0l;
                if (file.ReadByte() != 97 || file.ReadByte() != -1) return 17;
                file.SetLength(0l);
                if (file.Position != 1l || file.Length != 0l) return 18;
                file.Position = 0l;
                if (file.ReadByte() != -1) return 19;
                file.SetLength(0l);
                if (file.Position != 0l || file.Length != 0l || file.EndOfFile) return 20;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task NegativeClosedAndReadOnlyResizeFailuresReportIoErrors(int optimization)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-file-length-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string ir = Path.Combine(directory, "program.ll"), executable = Path.Combine(directory, "program.exe");
            string code = """
                using Standard::IO;

                int main(int argc, char** argv) {
                    FileStream closed = new();
                    try { long length = closed.Length; return 30; } catch (IOException* error) {}
                    try { closed.SetLength(0l); return 31; } catch (IOException* error) {}
                    FileStream input = FileStream::OpenRead(argv[1]);
                    if (input.ReadByte() != 97 || input.Length != 6l || input.Position != 1l) return 32;
                    try { input.SetLength(-1l); return 33; } catch (IOException* error) {}
                    if (input.Length != 6l || input.Position != 1l || input.PeekByte() != 98) return 34;
                    try { input.SetLength(3l); return 35; } catch (IOException* error) {}
                    if (input.Length != 6l || input.Position != 1l || input.ReadByte() != 98) return 36;
                    input.Close();
                    try { long length = input.Length; return 37; } catch (IOException* error) {}
                    try { input.SetLength(2l); return 38; } catch (IOException* error) {}
                    FileStream output = FileStream::OpenWrite(argv[1]);
                    output.Write("abc");
                    try { output.SetLength(-1l); return 39; } catch (IOException* error) {}
                    if (output.Length != 3l || output.Position != 3l) return 40;
                    output.SetLength(1l);
                    if (output.Length != 1l || output.Position != 3l) return 41;
                    output.Close();
                    return 0;
                }
                """;
            File.WriteAllText(ir, Compiler.Emit(Sources(code), options: new() { InternalizeFunctions = true }));
            var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            string path = Path.Combine(directory, "contents.bin");
            File.WriteAllText(path, "abcdef");
            var result = NativeToolchain.Run(executable, [path], 30000);
            Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
            Assert.Equal("a", File.ReadAllText(path));
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory); }
    }

    private static IEnumerable<SourceFile> Sources(string code)
        => new[] { "core/IO/IOException.gf", "core/IO/EndOfStreamException.gf", "core/IO/IByteReader.gf",
            "core/IO/SeekOrigin.gf", "libc/IO/FileStream.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard", name))))
            .Append(new SourceFile("file-length.gf", code));

    private static void Run(string code, int optimization)
        => Assert.Equal(0, CompilerTestHelper.Run(Sources(code), optimization).ExitCode);
}
