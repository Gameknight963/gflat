using System.IO.Compression;
using System.Text;
using static gflat.Tests.Std.Compression.ZipArchiveTestData;

namespace gflat.Tests.Std.Compression;

public sealed class ZipArchiveTests
{
    private const string Imports = """
        using Standard;
        using Standard::Collections;
        using Standard::Compression;
        using Standard::IO;
        using Standard::Memory;
        using Standard::Text;

        """ + "\n";

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void StoredDeflatedNamesAndDescriptorLayoutsMatchDotNet(int optimization)
    {
        var random = new Random(741);
        byte[] randomData = new byte[513]; random.NextBytes(randomData);
        Fixture ordinary = Create([
            new("stored.bin", Enumerable.Range(0, 256).Select(value => (byte)value).ToArray(), CompressionLevel.NoCompression),
            new("folder/零.txt", Encoding.UTF8.GetBytes(new string('x', 4096) + "\0bytes\u20ac"), CompressionLevel.Optimal),
            new("random", randomData, CompressionLevel.SmallestSize),
            new("folder/", [], CompressionLevel.NoCompression),
            new("empty", [], CompressionLevel.Optimal)]);
        Fixture streaming = Create([new("streamed", "data with descriptor\0"u8.ToArray(), CompressionLevel.Optimal)], streaming: true);
        Assert.NotEqual(0, U16(streaming.Bytes, Central(streaming.Bytes) + 8) & 8);
        Fixture legacy = Create([new("éÇα─.bin", [255, 0, 128], CompressionLevel.NoCompression)], names: Cp437());
        Assert.Equal(0, legacy.Entries[0].Flags & 2048);
        string allHigh = Cp437().GetString(Enumerable.Range(128, 128).Select(value => (byte)value).ToArray());
        Fixture legacyHigh = Create([new(allHigh, [], CompressionLevel.NoCompression)], names: Cp437());
        byte[] fakeEnd = new byte[22]; Put32(fakeEnd, 0, 0x06054b50u); Put16(fakeEnd, 4, 1);
        Fixture[] cases = [ordinary, streaming, streaming with { Bytes = WithoutDescriptorSignature(streaming.Bytes) },
            legacy, legacyHigh, Create([]), ordinary with { Bytes = WithComment(ordinary.Bytes, fakeEnd) },
            ordinary with { Bytes = WithComment(ordinary.Bytes, new byte[65535]) }];
        var globals = new StringBuilder(Imports);
        var body = new StringBuilder("int main() {\n");
        for (int index = 0; index < cases.Length; index++) AddValidCase(globals, body, cases[index], index);
        body.Append("return 0; }\n");
        Run(globals.Append(body).ToString(), optimization);
    }

    private static void AddValidCase(StringBuilder globals, StringBuilder body, Fixture fixture, int id)
    {
        string image = "Image" + id;
        globals.Append(Array(image, fixture.Bytes));
        body.AppendLine($$"""
            {
                ZipArchive archive = new({{image}}, {{fixture.Bytes.Length}});
                if (archive.Count != {{fixture.Entries.Length}}) return 1;
                Span<readonly(ZipArchiveEntry)> table = archive.Entries;
            """);
        for (int index = 0; index < fixture.Entries.Length; index++)
        {
            Expected expected = fixture.Entries[index];
            string prefix = "Expected" + id + "_" + index;
            byte[] name = Encoding.UTF8.GetBytes(expected.Name);
            globals.Append(Array(prefix + "Name", name));
            globals.Append(Array(prefix + "Raw", expected.NameBytes));
            globals.Append(Array(prefix + "Data", expected.Data));
            bool directory = expected.NameBytes.Length > 0 && expected.NameBytes[^1] == (byte)'/';
            body.AppendLine($$"""
                {
                    ZipArchiveEntry entry = table[{{index}}];
                    if (entry.Length != (ulong){{expected.Data.Length}} || entry.CompressedLength != (ulong){{expected.Compressed}} || entry.Method != {{expected.Method}} || entry.CRC32 != {{expected.Crc}}u) return 2;
                    if (entry.IsUtf8Name != {{((expected.Flags & 2048) != 0).ToString().ToLowerInvariant()}} || entry.IsDirectory != {{directory.ToString().ToLowerInvariant()}}) return 3;
                    Span<readonly(char)> raw = entry.NameBytes;
                    if (raw.Length != {{expected.NameBytes.Length}}) return 4;
                    for (nuint i = 0; i < raw.Length; i++) if ((byte)raw.Data![i] != {{prefix}}Raw[i]) return 5;
                    String name = entry.FullName;
                    if (name.Length != {{name.Length}}) return 6;
                    for (nuint i = 0; i < name.Length; i++) if ((byte)name.Data[i] != {{prefix}}Name[i]) return 7;
                    Vector<byte> output = new(); output.Resize({{expected.Data.Length + 4}});
                    for (nuint i = 0; i < output.Count; i++) output[i] = (byte)204;
                """);
            if (expected.Data.Length != 0)
                body.AppendLine($$"""
                    try { entry.Read(output.AsSpan().Data!, {{expected.Data.Length - 1}}); return 8; } catch (IOException* error) {}
                    for (nuint i = 0; i < output.Count; i++) if (output[i] != (byte)204) return 9;
                    try { Vector<byte> tooSmall = entry.ReadAllBytes({{expected.Data.Length - 1}}); return 10; } catch (IOException* error) {}
                    """);
            body.AppendLine($$"""
                    if (entry.Read(output.AsSpan().Data!, {{expected.Data.Length}}) != {{expected.Data.Length}}) return 11;
                    Vector<byte> owned = entry.ReadAllBytes({{expected.Data.Length}});
                    if (owned.Count != {{expected.Data.Length}}) return 12;
                    for (nuint i = 0; i < owned.Count; i++) if (owned[i] != {{prefix}}Data[i] || output[i] != {{prefix}}Data[i]) return 13;
                    for (nuint i = {{expected.Data.Length}}; i < output.Count; i++) if (output[i] != (byte)204) return 14;
                }
                """);
        }
        body.AppendLine("}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MalformedAndUnsupportedMetadataAreRejectedDuringConstruction(int optimization)
    {
        byte[] original = Create([new("name", "payload"u8.ToArray(), CompressionLevel.NoCompression)]).Bytes;
        int central = Central(original), end = original.Length - 22;
        var cases = new List<byte[]> { System.Array.Empty<byte>(), original[..21], original[..central], original[..^1] };
        void Changed(Action<byte[]> mutate)
        {
            byte[] value = original.ToArray();
            mutate(value);
            Assert.False(value.AsSpan().SequenceEqual(original), "Malformed fixture must change the archive bytes.");
            cases.Add(value);
        }
        Changed(bytes => bytes[0] ^= 1);
        Changed(bytes => bytes[30] ^= 1);
        Changed(bytes => Put16(bytes, 6, (ushort)(U16(bytes, 6) ^ 2048)));
        Changed(bytes => Put16(bytes, 8, 8));
        Changed(bytes => Put32(bytes, 18, U32(bytes, 18) ^ 1));
        Changed(bytes => Put32(bytes, 22, U32(bytes, 22) ^ 1));
        Changed(bytes => Put32(bytes, 14, U32(bytes, 14) ^ 1));
        Changed(bytes => Put32(bytes, central + 42, (uint)central));
        Changed(bytes => bytes[central] ^= 1);
        Changed(bytes => Put16(bytes, central + 8, 1));
        Changed(bytes => Put16(bytes, central + 8, 0x40));
        Changed(bytes => Put16(bytes, central + 8, 0x20));
        Changed(bytes => Put16(bytes, central + 10, 65000));
        Changed(bytes => Put16(bytes, central + 6, 45));
        Changed(bytes => Put32(bytes, central + 20, uint.MaxValue));
        Changed(bytes => Put32(bytes, central + 24, uint.MaxValue));
        Changed(bytes => Put16(bytes, central + 34, 1));
        Changed(bytes => Put16(bytes, end + 8, 2));
        Changed(bytes => Put16(bytes, end + 4, 1));
        cases.Add(WithLocalExtra(original, [1, 0, 0, 0]));
        cases.Add(WithLocalExtra(original, [2, 0, 8, 0, 0]));
        byte[] invalidUtf8 = original.ToArray();
        Put16(invalidUtf8, 6, 2048); Put16(invalidUtf8, central + 8, 2048);
        invalidUtf8[30] = 255; invalidUtf8[central + 46] = 255; cases.Add(invalidUtf8);
        byte[] descriptor = Create([new("descriptor", [1, 2, 3], CompressionLevel.Optimal)], streaming: true).Bytes;
        int descriptorAt = DataStart(descriptor) + checked((int)U32(descriptor, Central(descriptor) + 20));
        descriptor[descriptorAt + 4] ^= 1; cases.Add(descriptor);
        var code = new StringBuilder(Imports);
        for (int index = 0; index < cases.Count; index++) code.Append(Array("Invalid" + index, cases[index]));
        code.AppendLine("int main() {");
        for (int index = 0; index < cases.Count; index++)
            code.AppendLine($"try {{ ZipArchive archive = new(Invalid{index}, {cases[index].Length}); return {index + 1}; }} catch (IOException* error) {{}}");
        code.AppendLine("return 0; }");
        Run(code.ToString(), optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CorruptPayloadsRequireExactOutputLengthConsumptionAndCrc(int optimization)
    {
        Fixture stored = Create([new("stored", "stored data"u8.ToArray(), CompressionLevel.NoCompression)]);
        Fixture compressed = Create([new("compressed", Enumerable.Repeat((byte)90, 2000).ToArray(), CompressionLevel.Optimal)]);
        byte[] badStored = stored.Bytes.ToArray(); badStored[DataStart(badStored)] ^= 1;
        int at = Central(compressed.Bytes), data = DataStart(compressed.Bytes);
        byte[] raw = compressed.Bytes.AsSpan(data, checked((int)U32(compressed.Bytes, at + 20))).ToArray();
        uint crc = U32(compressed.Bytes, at + 16);
        byte[][] cases = [badStored,
            WithPayload(compressed.Bytes, [.. raw, 170], 8, 2000, crc),
            WithPayload(compressed.Bytes, raw[..^1], 8, 2000, crc),
            WithPayload(compressed.Bytes, raw, 8, 2001, crc),
            WithPayload(compressed.Bytes, raw, 8, 1999, crc),
            WithPayload(compressed.Bytes, raw, 8, 2000, crc ^ 1)];
        var code = new StringBuilder(Imports);
        for (int index = 0; index < cases.Length; index++) code.Append(Array("Corrupt" + index, cases[index]));
        code.AppendLine("int main() {");
        for (int index = 0; index < cases.Length; index++)
            code.AppendLine($$"""
                {
                    ZipArchive archive = new(Corrupt{{index}}, {{cases[index].Length}});
                    ZipArchiveEntry entry = archive.Entries[0];
                    Vector<byte> bytes = new(); bytes.Resize((nuint)entry.Length + 8);
                    for (nuint i = 0; i < bytes.Count; i++) bytes[i] = (byte)204;
                    try { entry.Read(bytes.AsSpan().Data!, (nuint)entry.Length); return 1; } catch (IOException* error) {}
                    for (nuint i = (nuint)entry.Length; i < bytes.Count; i++) if (bytes[i] != (byte)204) return 2;
                    try { Vector<byte> owned = entry.ReadAllBytes(4096); return 3; } catch (IOException* error) {}
                }
                """);
        code.AppendLine("return 0; }");
        Run(code.ToString(), optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void IndexCopiesAndEntryValuesKeepImageBorrowsAndReleaseAllOwnedAllocations(int optimization)
    {
        Fixture fixture = Create([new("owner", [0, 255, 128], CompressionLevel.NoCompression)]);
        byte[] badCrc = fixture.Bytes.ToArray(); badCrc[DataStart(badCrc)] ^= 1;
        Run(Imports + Array("Image", fixture.Bytes) + Array("BadCrc", badCrc) + """
            extern void*? malloc(nuint bytes);
            extern void free(void* data);
            static class State { public static bool Failing; public static int Live; }
            namespace Memory {
                public replace void*? Allocate(nuint bytes) {
                    if (State::Failing) return null;
                    void*? data = malloc(bytes);
                    if (data != null) State::Live++;
                    return data;
                }
                public replace void Free(void* data) nothrow { State::Live--; free(data); }
            }
            int main() {
                {
            """ + $$"""
                    ZipArchive first = new(Image, {{fixture.Bytes.Length}});
                    ZipArchive second = first;
                    if (first.Entries.Data == second.Entries.Data || first.Entries[0].NameBytes.Data != second.Entries[0].NameBytes.Data) return 1;
                    ZipArchive moved = move second;
                    second = new ZipArchive(Image, {{fixture.Bytes.Length}});
                    ZipArchiveEntry saved;
                    {
                        ZipArchive temporary = new(Image, {{fixture.Bytes.Length}});
                        saved = temporary.Entries[0];
                    }
                    Vector<byte> owned = saved.ReadAllBytes(3);
                    if (owned.Count != 3 || owned[0] != (byte)0 || owned[1] != (byte)255 || owned[2] != (byte)128) return 2;
                    int live = State::Live;
                    State::Failing = true;
                    try { ZipArchive failed = new(Image, {{fixture.Bytes.Length}}); return 3; } catch (BadAllocException* error) {}
                    try { second = first; return 4; } catch (BadAllocException* error) {}
                    try { Vector<byte> bytes = saved.ReadAllBytes(3); return 5; } catch (BadAllocException* error) {}
                    try { String name = saved.FullName; return 6; } catch (BadAllocException* error) {}
                    State::Failing = false;
                    if (State::Live != live || second.Count != 1 || moved.Count != 1) return 7;
                    ZipArchive corrupt = new(BadCrc, {{badCrc.Length}});
                    live = State::Live;
                    try { Vector<byte> bytes = corrupt.Entries[0].ReadAllBytes(3); return 8; } catch (IOException* error) {}
                    if (State::Live != live) return 9;
                }
                if (State::Live != 0) return 10;
                return 0;
            }
            """, optimization);
    }

    private static IEnumerable<SourceFile> Sources(string code)
        => new[] { "core/Compression/ZipArchive.gf", "core/Compression/ZipArchiveEntry.gf", "core/Compression/Deflate.gf",
            "core/Hashing/CRC32.gf", "core/Binary/BinaryPrimitives.gf", "core/Binary/BinaryBufferReader.gf",
            "core/Memory/bytes.gf", "core/IO/IByteReader.gf", "core/IO/IOException.gf", "core/Memory/Span.gf",
            "core/Collections/Vector.gf", "core/Collections/vector_resize.gf", "core/Collections/IEnumerable.gf",
            "core/Queries/Enumerable.gf", "core/Text/String.gf", "core/Text/ByteSearch.gf", "core/Text/utf8.gf",
            "core/IStringConvertable.gf", "core/IHashable.gf", "core/IEquatable.gf", "core/IComparable.gf",
            "core/Hashing/primitive_hashing.gf", "core/primitive_format.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard", name))))
            .Append(new SourceFile("zip-archive-tests.gf", code));

    private static void Run(string code, int optimization)
    {
        var result = CompilerTestHelper.Run(Sources(code), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }
}
