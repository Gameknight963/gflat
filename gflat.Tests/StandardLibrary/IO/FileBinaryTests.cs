using System.Buffers.Binary;

namespace gflat.Tests.Std.IO;

public sealed class FileBinaryTests
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
            var files = new[] { "core/IO/IOException.gf", "core/IO/EndOfStreamException.gf", "core/IO/IByteReader.gf", "libc/IO/File.gf", "core/Binary/BinaryPrimitives.gf", "core/IO/byte_reader.gf", "core/IO/byte_reader_binary.gf", "core/IO/text_writer_binary.gf", "libc/IO/FileBinary.gf" }
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
            using std; using std::IO;
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
    public void EmptyInputIsEndOfStreamAndReadModeFailureIsIOExceptionWithoutClosingBorrow()
    {
        Run([], (source, target) => $$"""
            using std; using std::IO;
            int main() {
                File empty = File::OpenRead({{source}});
                try { FileBinary::ReadUInt64BigEndian(&empty); return 1; }
                catch (EndOfStreamException* error) { }
                if (!empty.IsOpen || !empty.EndOfFile) return 2;
                File output = File::OpenWrite({{target}});
                try { FileBinary::ReadUInt16LittleEndian(&output); return 3; }
                catch (EndOfStreamException* error) { return 4; }
                catch (IOException* error) { }
                if (!output.IsOpen || output.EndOfFile) return 5;
                output.ClearError(); FileBinary::WriteUInt16LittleEndian(&output, (ushort)513);
                output.Close();
                return 0;
            }
            """, bytes => Assert.Equal(new byte[] { 1, 2 }, bytes));
    }

    [Fact]
    public void TruncatedReadsAndClosedStreamsThrow()
    {
        Run([1, 2, 3], (source, target) => $$"""
            using std; using std::IO;
            int main() {
                File input = File::OpenRead({{source}});
                try { FileBinary::ReadUInt32LittleEndian(&input); return 1; } catch (EndOfStreamException* error) { }
                if (input.ReadByte() != -1) return 2;
                try { FileBinary::ReadUInt16BigEndian(&input); return 5; } catch (IOException* error) { }
                input.Close();
                try { FileBinary::ReadUInt16BigEndian(&input); return 3; }
                catch (EndOfStreamException* error) { return 6; }
                catch (IOException* error) { }
                try { FileBinary::WriteUInt64BigEndian(&input, 1ul); return 4; } catch (IOException* error) { }
                File output = File::OpenWrite({{target}}); output.Close();
                return 0;
            }
            """, bytes => Assert.Empty(bytes));
    }
}
