using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Xunit.Abstractions;

namespace gflat.Tests.Std.IO;

public sealed class FilePositionTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void TemporaryStreamPositionsSupportReadWriteTransitionsAndResetEof(int optimization)
        => Run("""
            using Standard::IO;

            int main() {
                FileStream file = FileStream::OpenTemporary();
                if (file.Position != 0l) return 1;
                file.Write("abcdef", 6);
                if (file.Position != 6l || file.Seek(-3l, SeekOrigin::Current) != 3l) return 2;
                if (file.ReadByte() != 100 || file.Position != 4l) return 3;
                file.Position = 1l;
                file.Write("Z");
                file.Position = 0l;
                char[6] bytes;
                file.ReadExactly(bytes, 6);
                if (bytes[0] != 'a' || bytes[1] != 'Z' || bytes[5] != 'f') return 4;
                if (file.ReadByte() != -1 || !file.EndOfFile || file.Position != 6l) return 5;
                if (file.Seek(0l, SeekOrigin::Current) != 6l || file.EndOfFile) return 6;
                if (file.Seek(-1l, SeekOrigin::End) != 5l || file.PeekByte() != 102 || file.Position != 5l) return 7;
                if (file.Seek(0l, SeekOrigin::Current) != 5l || file.ReadByte() != 102) return 8;
                file.Position = 10l;
                if (file.Position != 10l || file.Seek(0l, SeekOrigin::End) != 6l) return 9;
                file.Position = 10l;
                file.WriteByte('Q');
                file.Position = 6l;
                for (int index = 0; index < 4; index++) if (file.ReadByte() != 0) return 10;
                if (file.ReadByte() != 81 || file.ReadByte() != -1) return 11;
                file.Close();
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InvalidOriginsNegativePositionsAndClosedStreamsThrowIoExceptions(int optimization)
        => Run("""
            using Standard::IO;

            int main() {
                FileStream closed = new();
                try { long position = closed.Position; return 20; } catch (IOException* error) {}
                try { closed.Position = 0l; return 21; } catch (IOException* error) {}
                try { closed.Seek(0l, SeekOrigin::Begin); return 22; } catch (IOException* error) {}
                FileStream file = FileStream::OpenTemporary();
                file.Write("abc");
                file.Position = 1l;
                try { file.Seek(0l, (SeekOrigin)99); return 23; } catch (IOException* error) {}
                if (file.Position != 1l) return 24;
                try { file.Position = -1l; return 25; } catch (IOException* error) {}
                if (file.Position != 1l) return 26;
                try { file.Seek(-2l, SeekOrigin::Current); return 27; } catch (IOException* error) {}
                file.ClearError();
                file.Position = 0l;
                if (file.ReadByte() != 97) return 28;
                file.Close();
                try { long position = file.Position; return 29; } catch (IOException* error) {}
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task AppendWritesStayAtEndAfterSeekingElsewhere(int optimization)
    {
        string directory = CreateDirectory();
        try
        {
            string executable = Compile(directory, """
                using Standard::IO;

                int main(int argc, char** argv) {
                    {
                        FileStream file = FileStream::OpenWrite(argv[1]);
                        file.Write("abc");
                        file.Position = 0l;
                        file.Write("Z");
                        file.Close();
                    }
                    {
                        FileStream file = FileStream::OpenAppend(argv[1]);
                        if (file.Seek(0l, SeekOrigin::Begin) != 0l) return 30;
                        file.Write("Q");
                        file.Close();
                    }
                    return 0;
                }
                """, optimization);
            string path = Path.Combine(directory, "append.bin");
            var result = NativeToolchain.Run(executable, [path], 30000);
            Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
            Assert.Equal("ZbcQ", File.ReadAllText(path));
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task PositionsBeyondFourGiBArePreservedWithoutLargeAllocations(int optimization)
    {
        string directory = CreateDirectory();
        try
        {
            const long position = (1L << 32) + 17;
            string path = Path.Combine(directory, "sparse.bin");
            if (!PrepareSparseFile(path, position)) return;
            string executable = Compile(directory, """
                using Standard::IO;

                int main(int argc, char** argv) {
                    long position = 4294967313l;
                    {
                        FileStream file = FileStream::OpenRead(argv[1]);
                        if (file.Seek(position, SeekOrigin::Begin) != position || file.Position != position) return 40;
                        if (file.ReadByte() != 65 || file.Position != position + 1l) return 41;
                        if (file.Seek(-1l, SeekOrigin::Current) != position || file.ReadByte() != 65) return 42;
                        if (file.Seek(0l, SeekOrigin::End) != position + 1l || file.ReadByte() != -1) return 43;
                        file.Position = position - 1l;
                        if (file.EndOfFile || file.ReadByte() != 0 || file.ReadByte() != 65) return 44;
                    }
                    {
                        FileStream file = FileStream::OpenAppend(argv[1]);
                        if (file.Seek(0l, SeekOrigin::End) != position + 1l) return 45;
                        file.WriteByte('B');
                        file.Close();
                    }
                    return 0;
                }
                """, optimization);
            var result = NativeToolchain.Run(executable, [path], 30000);
            Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
            Assert.Equal(position + 2, new FileInfo(path).Length);
            using var check = File.OpenRead(path);
            check.Position = position;
            Assert.Equal(65, check.ReadByte());
            Assert.Equal(66, check.ReadByte());
            Assert.Equal(-1, check.ReadByte());
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task SparseFileLengthsRemainSigned64BitAndPreservePositionsAcrossResize(int optimization)
    {
        string directory = CreateDirectory();
        try
        {
            const long position = (1L << 32) + 17;
            string path = Path.Combine(directory, "sparse-length.bin");
            if (!PrepareSparseFile(path, position)) return;
            string executable = Compile(directory, """
                using Standard::IO;

                int main(int argc, char** argv) {
                    long position = 4294967313l;
                    FileStream file = FileStream::OpenAppend(argv[1]);
                    file.Position = 7l;
                    if (file.Length != position + 1l || file.Position != 7l) return 50;
                    file.SetLength(position);
                    if (file.Length != position || file.Position != 7l) return 51;
                    file.SetLength(position + 2l);
                    if (file.Length != position + 2l || file.Position != 7l) return 52;
                    file.Close();
                    FileStream input = FileStream::OpenRead(argv[1]);
                    input.Position = position;
                    if (input.ReadByte() != 0 || input.ReadByte() != 0 || input.ReadByte() != -1) return 53;
                    return 0;
                }
                """, optimization);
            var result = NativeToolchain.Run(executable, [path], 30000);
            Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
            Assert.Equal(position + 2, new FileInfo(path).Length);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory); }
    }

    [Theory]
    [InlineData("declare i64 @__gflat_stdio_tell(i8*)")]
    [InlineData("declare i32 @__gflat_stdio_seek(i8*, i64, i32)")]
    public void PositionBridgeDeclarationsSelectTheExistingCBridge(string declaration)
        => Assert.Equal((false, true), NativeToolchain.RequiredNativeRuntimes(declaration));

    private bool PrepareSparseFile(string path, long position)
    {
        using var file = new System.IO.FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite);
        if (OperatingSystem.IsWindows() && !DeviceIoControl(file.SafeFileHandle, 0x000900C4,
                IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero))
        {
            int error = Marshal.GetLastWin32Error();
            if (error is 1 or 50)
            {
                output.WriteLine("The filesystem does not support sparse files. The >4 GiB positioning case was omitted.");
                return false;
            }
            throw new Win32Exception(error);
        }
        try { file.SetLength(position + 1); }
        catch (IOException error) when ((error.HResult & 0xffff) is 27 or 223)
        {
            output.WriteLine("The filesystem rejected the sparse file's logical size. The >4 GiB positioning case was omitted.");
            return false;
        }
        file.Position = position;
        file.WriteByte(65);
        return true;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle file, uint code,
        IntPtr input, uint inputBytes, IntPtr output, uint outputBytes, out uint returnedBytes, IntPtr overlapped);

    private static string CreateDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-file-position-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void Run(string code, int optimization)
    {
        var result = CompilerTestHelper.Run(Sources(code), optimization);
        Assert.Equal(0, result.ExitCode);
    }

    private static IEnumerable<SourceFile> Sources(string code)
        => new[] { "core/IO/IOException.gf", "core/IO/EndOfStreamException.gf", "core/IO/IByteReader.gf",
            "core/IO/SeekOrigin.gf", "libc/IO/FileStream.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard", name))))
            .Append(new SourceFile("file-position.gf", code));

    private static string Compile(string directory, string code, int optimization)
    {
        string ir = Path.Combine(directory, "program.ll"), executable = Path.Combine(directory, "program.exe");
        File.WriteAllText(ir, Compiler.Emit(Sources(code), options: new() { InternalizeFunctions = true }));
        var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
        Assert.True(compiled.ExitCode == 0, compiled.StandardError);
        return executable;
    }
}
