using System.Buffers.Binary;

namespace gflat.Tests;

public sealed class StandardFileBinaryTests
{
    private static string Literal(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static void Run(byte[] input, Func<string, string, string> program, Action<byte[]> verify)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-file-binary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string source = Path.Combine(directory, "input.bin"), target = Path.Combine(directory, "output.bin");
            File.WriteAllBytes(source, input);
            var files = new[] { "core/IOException.gf", "libc/File.gf", "libc/File/FileBinary.gf" }
                .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", name))))
                .Append(new SourceFile("main.gf", program(Literal(source), Literal(target))));
            string ir = Path.Combine(directory, "program.ll"), exe = Path.Combine(directory, "program.exe");
            File.WriteAllText(ir, Compiler.Emit(files));
            var compiled = NativeToolchain.Compile(ir, exe);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            var result = NativeToolchain.Run(exe, [], 30000);
            Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
            verify(File.ReadAllBytes(target));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FixedWidthValuesMatchHostIndependentByteOrder(bool little)
    {
        byte[] expected = new byte[28];
        if (little)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(expected, 0x80fe);
            BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(2), 0xfedcba98);
            BinaryPrimitives.WriteUInt64LittleEndian(expected.AsSpan(6), 0xfedcba9876543210);
        }
        else
        {
            BinaryPrimitives.WriteUInt16BigEndian(expected, 0x80fe);
            BinaryPrimitives.WriteUInt32BigEndian(expected.AsSpan(2), 0xfedcba98);
            BinaryPrimitives.WriteUInt64BigEndian(expected.AsSpan(6), 0xfedcba9876543210);
        }
        // Follow high-bit values with zeros to check full-width reads/writes.
        string order = little ? "LittleEndian" : "BigEndian";
        Run(expected, (source, target) => $$"""
            using std;
            int main() {
                File input = File::OpenRead({{source}});
                if (FileBinary::ReadUInt16{{order}}(&input) != (ushort)33022) return 1;
                if (FileBinary::ReadUInt32{{order}}(&input) != (uint)4275878552) return 2;
                if (FileBinary::ReadUInt64{{order}}(&input) != 18364758544493064720ul) return 3;
                if (FileBinary::ReadUInt16{{order}}(&input) != (ushort)0) return 4;
                if (FileBinary::ReadUInt32{{order}}(&input) != (uint)0) return 5;
                if (FileBinary::ReadUInt64{{order}}(&input) != 0ul) return 6;
                File output = File::OpenWrite({{target}});
                FileBinary::WriteUInt16{{order}}(&output, (ushort)33022);
                FileBinary::WriteUInt32{{order}}(&output, (uint)4275878552);
                FileBinary::WriteUInt64{{order}}(&output, 18364758544493064720ul);
                FileBinary::WriteUInt16{{order}}(&output, (ushort)0);
                FileBinary::WriteUInt32{{order}}(&output, (uint)0);
                FileBinary::WriteUInt64{{order}}(&output, 0ul);
                output.Close();
                return 0;
            }
            """, bytes => Assert.Equal(expected, bytes));
    }

    [Fact]
    public void TruncatedReadsAndClosedStreamsThrow()
    {
        Run([1, 2, 3], (source, target) => $$"""
            using std;
            int main() {
                File input = File::OpenRead({{source}});
                try { FileBinary::ReadUInt32LittleEndian(&input); return 1; } catch (IOException* error) { }
                if (input.ReadByte() != -1) return 2;
                input.Close();
                try { FileBinary::ReadUInt16BigEndian(&input); return 3; } catch (IOException* error) { }
                try { FileBinary::WriteUInt64BigEndian(&input, 1ul); return 4; } catch (IOException* error) { }
                File output = File::OpenWrite({{target}}); output.Close();
                return 0;
            }
            """, bytes => Assert.Empty(bytes));
    }
}
