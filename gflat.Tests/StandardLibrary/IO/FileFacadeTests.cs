namespace gflat.Tests.Std.IO;

public sealed class FileFacadeTests
{
    [Fact]
    public void StreamSourcesCompileWithoutStringOrVector()
    {
        var sources = StreamSources().Append(new SourceFile("stream-only.gf", """
            using Standard::IO;

            int main()
            {
                FileStream closed = new();
                if (closed.IsOpen) return 1;
                return 0;
            }
            """));
        Compiler.Emit(sources);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task FactoriesReturnOwningStreamsWithBinaryModes(int optimization)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-file-facade-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string[] names = ["core/IO/IOException.gf", "core/IO/EndOfStreamException.gf",
                "core/IO/IByteReader.gf", "core/IO/SeekOrigin.gf", "libc/IO/FileStream.gf"];
            var sources = FileApiTestSources.WithConveniences(names).Select(ReadSource)
                .Append(new SourceFile("file-factories.gf", """
                    using Standard::IO;

                    int main(int argc, char** argv)
                    {
                        {
                            FileStream output = File::OpenWrite(argv[1]);
                            output.Write("A\0B", 3);
                            output.Close();
                        }
                        {
                            FileStream output = File::OpenAppend(argv[1]);
                            output.Write("C");
                        }
                        {
                            FileStream input = File::OpenRead(argv[1]);
                            if (input.ReadByte() != 65 || input.ReadByte() != 0 ||
                                input.ReadByte() != 66 || input.ReadByte() != 67 ||
                                input.ReadByte() != -1) return 1;
                            input.Close();
                        }
                        FileStream temporary = File::OpenTemporary();
                        if (!temporary.IsOpen) return 2;
                        temporary.Write("scratch");
                        temporary.Close();
                        if (temporary.IsOpen) return 3;
                        return 0;
                    }
                    """));
            string ir = Path.Combine(directory, "factories.ll"), executable = Path.Combine(directory, "factories.exe");
            File.WriteAllText(ir, Compiler.Emit(sources, options: new() { InternalizeFunctions = true }));
            var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            string path = Path.Combine(directory, "contents.bin");
            File.WriteAllBytes(path, [255, 255, 255, 255, 255]);
            var result = NativeToolchain.Run(executable, [path], 30000);
            Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
            Assert.Equal(new byte[] { 65, 0, 66, 67 }, File.ReadAllBytes(path));
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory); }
    }

    private static IEnumerable<SourceFile> StreamSources()
        => new[] { "core/IO/IOException.gf", "core/IO/EndOfStreamException.gf",
            "core/IO/IByteReader.gf", "core/IO/SeekOrigin.gf", "libc/IO/FileStream.gf" }.Select(ReadSource);

    private static SourceFile ReadSource(string name)
        => new(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard", name)));
}
