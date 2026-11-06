using System.IO.Compression;
using System.Text;
using static gflat.Tests.Std.Compression.ZipArchiveTestData;

namespace gflat.Tests.Std.Compression;

public sealed class ZipArchiveWriterTests
{
    private const string Imports = """
        using Standard;
        using Standard::Collections;
        using Standard::Compression;
        using Standard::IO;
        using Standard::Memory;
        using Standard::Text;

        """ + "\n";

    private const string FixedOutput = """
        struct FixedOutput : ITextWriter {
            public char* Data;
            public nuint Length;
            public int Calls;
            public int FailAt;
            public FixedOutput(char* storage) { Data = storage; Length = 0; Calls = 0; FailAt = 0; }
            public void Write(readonly(char)* data, nuint count) {
                Calls++;
                if (Calls == FailAt) {
                    if (count != 0) { Data[Length] = data[0]; Length++; }
                    throw new IOException("Output failed");
                }
                for (nuint i = 0; i < count; i++) { Data[Length] = data[i]; Length++; }
            }
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void StoredArchivesRoundTripThroughDotNetAndStandardReader(int optimization)
    {
        Item[] items = [
            new("binary", Enumerable.Range(0, 256).Select(value => (byte)value).ToArray(), CompressionLevel.NoCompression),
            new("empty", [], CompressionLevel.NoCompression),
            new("folder/", [], CompressionLevel.NoCompression),
            new("folder/零-é.txt", "hello\0world"u8.ToArray(), CompressionLevel.NoCompression),
            new("duplicate", [1], CompressionLevel.NoCompression),
            new("duplicate", [2], CompressionLevel.NoCompression),
            new("nul\0tail", [3], CompressionLevel.NoCompression)];
        var code = new StringBuilder(Imports);
        for (int index = 0; index < items.Length; index++)
        {
            code.Append(Array("Name" + index, Encoding.UTF8.GetBytes(items[index].Name)));
            code.Append(Array("Payload" + index, items[index].Data));
        }
        code.AppendLine("extern \"C\" int putchar(int value);");
        code.AppendLine("int main() { String output = new(); ZipArchiveWriter writer = new(&output);");
        for (int index = 0; index < items.Length; index++)
        {
            if (index == 0)
                code.AppendLine($"writer.AddEntry(\"binary\", Payload0, {items[0].Data.Length});");
            else
                code.AppendLine($$"""
                    {
                        readonly(byte)* nameBytes = Name{{index}};
                        readonly(byte)* payload = Payload{{index}};
                        writer.AddEntry(new Span<readonly(char)>(nameBytes.reinterpret<readonly(char)*>(), {{Encoding.UTF8.GetByteCount(items[index].Name)}}),
                            new Span<readonly(byte)>(payload, {{items[index].Data.Length}}));
                    }
                    """);
        }
        code.AppendLine($$"""
            if (writer.Count != {{items.Length}} || writer.IsCompleted) return 1;
            writer.Complete();
            nuint length = output.Length;
            writer.Complete();
            if (!writer.IsCompleted || output.Length != length) return 2;
            try { byte value = 0; writer.AddEntry("later", &value, 1); return 3; } catch (IOException* error) {}
            ZipArchive archive = new(output.Data.reinterpret<readonly(byte)*>(), output.Length);
            if (archive.Count != {{items.Length}}) return 4;
            for (nuint i = 0; i < archive.Count; i++) {
                Vector<byte> data = archive.Entries[i].ReadAllBytes(256);
                if (data.Count != (nuint)archive.Entries[i].Length) return 5;
            }
            readonly(char)* digits = "0123456789abcdef";
            for (nuint i = 0; i < output.Length; i++) {
                byte value = (byte)output.Data[i];
                putchar((int)digits[(nuint)(value >> 4)]);
                putchar((int)digits[(nuint)(value & (byte)15)]);
            }
            return 0;
            }
            """);
        var result = Run(code.ToString(), optimization);
        byte[] bytes = Convert.FromHexString(result.StandardOutput.Trim());
        using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        Assert.Equal(items.Length, archive.Entries.Count);
        for (int index = 0; index < items.Length; index++)
        {
            var entry = archive.Entries[index];
            Assert.Equal(items[index].Name, entry.FullName);
            Assert.Equal(items[index].Data.Length, entry.Length);
            Assert.Equal(new DateTime(1980, 1, 1), entry.LastWriteTime.DateTime);
            using var input = entry.Open();
            using var contents = new MemoryStream();
            input.CopyTo(contents);
            Assert.Equal(items[index].Data, contents.ToArray());
        }
        int central = Central(bytes);
        for (int index = 0; index < items.Length; index++)
        {
            Assert.Equal(0, U16(bytes, central + 10));
            Assert.Equal(2048, U16(bytes, central + 8));
            central += 46 + U16(bytes, central + 28);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ValidationAndAllocationFailuresPrecedeOutputAndMovesKeepOwnership(int optimization)
        => Run(Imports + FixedOutput + """
            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* data);
            static class State { public static int Calls; public static int FailAt; public static int Live; public static bool Failing; }
            namespace Memory {
                public replace void*? Allocate(nuint bytes) {
                    State::Calls++;
                    if (State::Failing || State::Calls == State::FailAt) return null;
                    void*? data = malloc(bytes);
                    if (data != null) State::Live++;
                    return data;
                }
                public replace void Free(void* data) nothrow { State::Live--; free(data); }
            }
            int main() {
                {
                    char[4096] buffer;
                    FixedOutput output = new(buffer);
                    ZipArchiveWriter writer = new(&output);
                    byte value = 255;
                    State::FailAt = State::Calls + 1;
                    try { writer.AddEntry("name", &value, 1); return 1; } catch (BadAllocException* error) {}
                    if (output.Length != 0 || output.Calls != 0 || State::Live != 0 || writer.Count != 0) return 2;
                    State::FailAt = State::Calls + 2;
                    try { writer.AddEntry("name", &value, 1); return 3; } catch (BadAllocException* error) {}
                    if (output.Length != 0 || output.Calls != 0 || State::Live != 0 || writer.Count != 0) return 4;
                    State::FailAt = 0;
                    try { writer.AddEntry("", &value, 1); return 5; } catch (IOException* error) {}
                    char invalid = (char)255;
                    Span<readonly(char)> badName = new(&invalid, 1);
                    Span<readonly(byte)> payload = new(&value, 1);
                    try { writer.AddEntry(badName, payload); return 6; } catch (IOException* error) {}
                    Span<readonly(char)> longName = new("x", 65536);
                    try { writer.AddEntry(longName, payload); return 7; } catch (IOException* error) {}
                    try { writer.AddEntry("huge", &value, (nuint)0xffffffffu); return 8; } catch (IOException* error) {}
                    try { writer.AddEntry("huge", &value, (nuint)0xfffffffeu); return 9; } catch (IOException* error) {}
                    if (output.Length != 0 || output.Calls != 0 || State::Live != 0 || writer.Count != 0) return 10;
                    writer.AddEntry("name", &value, 1);
                    ZipArchiveWriter moved = move writer;
                    State::Failing = true;
                    moved.Complete();
                    moved.Complete();
                    State::Failing = false;
                    ZipArchive archive = new((&buffer[0]).reinterpret<readonly(byte)*>(), output.Length);
                    if (archive.Count != 1 || archive.GetEntry("name").Read(&value, 1) != 1 || value != (byte)255) return 11;
                }
                if (State::Live != 0) return 12;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OutputFailurePermanentlyFaultsEntryAndCompletionOperations(int optimization)
        => Run(Imports + FixedOutput + """
            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* data);
            static class State { public static int Live; }
            namespace Memory {
                public replace void*? Allocate(nuint bytes) {
                    void*? data = malloc(bytes);
                    if (data != null) State::Live++;
                    return data;
                }
                public replace void Free(void* data) nothrow { State::Live--; free(data); }
            }
            int main() {
                for (int failure = 1; failure <= 6; failure++) {
                    if (State::Live != 0) return 9;
                    char[4096] buffer;
                    FixedOutput output = new(buffer);
                    output.FailAt = failure;
                    ZipArchiveWriter writer = new(&output);
                    byte value = 0;
                    if (failure <= 3) {
                        try { writer.AddEntry("name", &value, 1); return 1; } catch (IOException* error) {}
                        if (writer.Count != 0) return 2;
                    } else {
                        writer.AddEntry("name", &value, 1);
                        try { writer.Complete(); return 3; } catch (IOException* error) {}
                    }
                    int calls = output.Calls;
                    nuint length = output.Length;
                    output.FailAt = 0;
                    try { writer.Complete(); return 4; } catch (IOException* error) {}
                    try { writer.AddEntry("later", &value, 1); return 5; } catch (IOException* error) {}
                    if (output.Calls != calls || output.Length != length || writer.IsCompleted) return 6;
                }
                if (State::Live != 0) return 10;
                char[22] empty;
                FixedOutput output = new(empty);
                ZipArchiveWriter writer = new(&output);
                writer.Complete();
                writer.Complete();
                if (output.Length != 22 || output.Calls != 1 || !writer.IsCompleted) return 7;
                ZipArchive archive = new((&empty[0]).reinterpret<readonly(byte)*>(), 22);
                if (archive.Count != 0) return 8;
                return 0;
            }
            """, optimization);

    [Fact]
    public void WriterCannotBeCopied()
    {
        var error = Assert.Throws<gflat.CompileExceptions.TypeCheckException>(() => Compiler.Check(Sources(Imports + """
            int main() {
                String output = new();
                ZipArchiveWriter first = new(&output);
                ZipArchiveWriter second = first;
                return 0;
            }
            """)));
        Assert.Contains("Cannot copy", error.Message);
    }

    private static IEnumerable<SourceFile> Sources(string code)
        => new[] { "core/Compression/ZipArchiveWriter.gf", "core/Compression/ZipArchive.gf", "core/Compression/ZipArchiveEntry.gf", "core/Compression/Deflate.gf",
            "core/Hashing/CRC32.gf", "core/Binary/BinaryPrimitives.gf", "core/Binary/BinaryBufferReader.gf",
            "core/Memory/bytes.gf", "core/IO/IByteReader.gf", "core/IO/IOException.gf", "core/Memory/Span.gf",
            "core/Collections/Vector.gf", "core/Collections/vector_resize.gf", "core/Collections/IEnumerable.gf",
            "core/Queries/Enumerable.gf", "core/Text/String.gf", "core/Text/ByteSearch.gf", "core/Text/CString.gf", "core/Text/utf8.gf",
            "core/IStringConvertable.gf", "core/IHashable.gf", "core/IEquatable.gf", "core/IComparable.gf",
            "core/Hashing/primitive_hashing.gf", "core/primitive_format.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard", name))))
            .Append(new SourceFile("zip-writer-tests.gf", code));

    private static ExecutionResult Run(string code, int optimization)
    {
        var result = CompilerTestHelper.Run(Sources(code), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
        return result;
    }
}
