using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace gflat.Tests;

public sealed class PngVerifyExampleTests
{
    private sealed record Chunk(string Type, byte[] Data);
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ActualVerifierChecksSupportedImageDataWithoutWritingFiles(int optimization)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-png-verify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Standard"), "*.gf", SearchOption.AllDirectories)
                .Select(path => new SourceFile(path, File.ReadAllText(path)))
                .Append(new SourceFile("png-verify.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Examples/png-verify.gf"))));
            string ir = Path.Combine(directory, "png-verify.ll"), executable = Path.Combine(directory, "png-verify.exe");
            File.WriteAllText(ir, Compiler.Emit(sources, options: new() { InternalizeFunctions = true }));
            var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);

            foreach (byte channels in new byte[] { 3, 4 })
            foreach (CompressionLevel level in new[] { CompressionLevel.NoCompression, CompressionLevel.Fastest, CompressionLevel.SmallestSize })
            {
                byte[] rows = Rows(7, 5, channels); // Every filter 0..4, plus zero/high sample bytes.
                byte[] compressed = Compress(rows, level);
                Oracle(compressed, rows);
                string mode = channels == 3 ? "RGB8" : "RGBA8";
                byte color = channels == 3 ? (byte)2 : (byte)6;
                Check(executable, directory, $"{mode}-{level}", Container(Header(7, 5, color: color), new("IDAT", compressed), End()), $"Verified {mode} image data: 7 x 5\n");
                // Chunk boundaries can split every byte of zlib header/body/checksum.
                Chunk[] pieces = [Header(7, 5, color: color), new("IDAT", []),
                    .. compressed.Select(value => new Chunk("IDAT", [value])), new("IDAT", []), End()];
                Check(executable, directory, $"{mode}-{level}-split", Container(pieces), $"Verified {mode} image data: 7 x 5\n");
            }

            byte[] smallRows = Rows(1, 1, 3);
            byte[] stream = Compress(smallRows, CompressionLevel.Fastest);
            Oracle(stream, smallRows);
            byte[] ordinary = Container(Header(), new("IDAT", stream), End());
            const string smallSuccess = "Verified RGB8 image data: 1 x 1\n";
            Check(executable, directory, "small", ordinary, smallSuccess);
            Check(executable, directory, "suggested palette", Container(Header(), new("PLTE", [0, 255, 128]), new("IDAT", stream), End()), smallSuccess);
            Check(executable, directory, "maximum suggested palette", Container(Header(), new("PLTE", new byte[768]), new("IDAT", stream), End()), smallSuccess);
            Check(executable, directory, "rgba suggested palette", Container(Header(color: 6), new("PLTE", [0, 255, 128]), new("IDAT", Compress(Rows(1, 1, 4))), End()), "Verified RGBA8 image data: 1 x 1\n");
            Check(executable, directory, "opaque ancillary", Container(Header(), new("aaAa", [255, 0, 128]), new("IDAT", stream), new("aaAa", []), End()), smallSuccess);
            // This is deliberately not a full ancillary metadata conformance checker.
            Check(executable, directory, "uninterpreted text payload", Container(Header(), new("tEXt", [0, 255]), new("IDAT", stream), End()), smallSuccess);
            Check(executable, directory, "unused idat bytes", Container(Header(), new("IDAT", [.. stream, 170, 255, 0]), End()), smallSuccess);
            Check(executable, directory, "unused second zlib stream", Container(Header(), new("IDAT", [.. stream, .. stream]), End()), smallSuccess);
            Check(executable, directory, "unused tail separate idat", Container(Header(), new("IDAT", stream), new("IDAT", [170, 0, 255]), End()), smallSuccess);

            for (int length = 0; length < ordinary.Length; length++)
                Check(executable, directory, "prefix-" + length, ordinary[..length], null);
            for (int index = 0; index < 8; index++)
            {
                byte[] bad = ordinary.ToArray(); bad[index] ^= 1;
                Check(executable, directory, "signature-" + index, bad, null);
            }
            byte[] badCrc = ordinary.ToArray(); badCrc[^13] ^= 1;
            Check(executable, directory, "idat crc", badCrc, null);
            badCrc = ordinary.ToArray(); badCrc[^1] ^= 1;
            Check(executable, directory, "iend crc before success", badCrc, null);
            byte[] badLength = ordinary.ToArray(); BinaryPrimitives.WriteUInt32BigEndian(badLength.AsSpan(8, 4), 0x80000000u);
            Check(executable, directory, "31bit chunk length", badLength, null);
            badLength = ordinary.ToArray(); BinaryPrimitives.WriteUInt32BigEndian(badLength.AsSpan(8, 4), 0x7fffffffu);
            Check(executable, directory, "chunk length beyond input", badLength, null);

            foreach (var item in new (string Name, Chunk[] Chunks)[] {
                ("idat before header", [new("IDAT", stream), Header(), End()]),
                ("short header", [new("IHDR", new byte[12]), new("IDAT", stream), End()]),
                ("long header", [new("IHDR", new byte[14]), new("IDAT", stream), End()]),
                ("duplicate header", [Header(), Header(), new("IDAT", stream), End()]),
                ("missing idat", [Header(), End()]),
                ("empty image stream", [Header(), new("IDAT", []), End()]),
                ("nonconsecutive idat", [Header(), new("IDAT", stream), new("aaAa", []), new("IDAT", []), End()]),
                ("late palette", [Header(), new("IDAT", stream), new("PLTE", [1, 2, 3]), End()]),
                ("duplicate palette", [Header(), new("PLTE", [1, 2, 3]), new("PLTE", [1, 2, 3]), new("IDAT", stream), End()]),
                ("empty palette", [Header(), new("PLTE", []), new("IDAT", stream), End()]),
                ("partial palette", [Header(), new("PLTE", new byte[4]), new("IDAT", stream), End()]),
                ("oversized palette", [Header(), new("PLTE", new byte[771]), new("IDAT", stream), End()]),
                ("missing end", [Header(), new("IDAT", stream)]),
                ("nonempty end", [Header(), new("IDAT", stream), new("IEND", [0])]),
                ("nonletter chunk", [Header(), new("aa1a", []), new("IDAT", stream), End()])
            }) Check(executable, directory, item.Name, Container(item.Chunks), null);
            Check(executable, directory, "bytes after iend", [.. ordinary, 0], null);
            Check(executable, directory, "second png", [.. ordinary, .. ordinary], null);
            foreach (string type in new[] { "ABCD", "acTL", "fcTL", "fdAT", "aaab" })
                Check(executable, directory, "unsupported chunk-" + type, Container(Header(), new(type, []), new("IDAT", stream), End()), null, "Unsupported PNG image mode or chunk");

            foreach (var mode in new (byte Depth, byte Color, byte Interlace)[] {
                (1, 0, 0), (8, 0, 0), (8, 3, 0), (8, 4, 0), (16, 2, 0), (16, 6, 0), (8, 2, 1)
            }) Check(executable, directory, $"unsupported mode-{mode}", Container(Header(depth: mode.Depth, color: mode.Color, interlace: mode.Interlace), new("IDAT", stream), End()), null, "Unsupported PNG image mode or chunk");
            foreach (Chunk header in new[] {
                Header(0, 1), Header(1, 0), Header(0x80000000u, 1), Header(1, 0xffffffffu),
                Header(depth: 7), Header(depth: 4), Header(depth: 16, color: 3), Header(depth: 1, color: 4), Header(color: 1),
                Header(compression: 1), Header(filter: 1), Header(interlace: 2)
            }) Check(executable, directory, "invalid header-" + Convert.ToHexString(header.Data), Container(header, new("IDAT", stream), End()), null, "Invalid PNG structure or image data");
            Check(executable, directory, "row width limit", Container(Header(22369622u, 1), new("IDAT", stream), End()), null, "PNG file or decoded image limit exceeded");
            Check(executable, directory, "row height limit", Container(Header(1, 16777217u), new("IDAT", stream), End()), null, "PNG file or decoded image limit exceeded");

            byte[] badAdler = stream.ToArray(); badAdler[^1] ^= 1;
            // Container reconstructs CRCs after inner changes, so these reach image validation.
            foreach (var item in new (string Name, byte[] Stream)[] {
                ("bad zlib adler", badAdler), ("short zlib", stream[..^1]), ("invalid raw body", [120, 1, 7]),
                ("forbidden dictionary", [120, 32, 0, 0, 0, 1]),
                ("short filtered row", Compress(smallRows[..^1])), ("long filtered row", Compress([.. smallRows, 0])),
                ("bad row filter", Compress([5, 0, 128, 255]))
            }) Check(executable, directory, item.Name, Container(Header(), new("IDAT", item.Stream), End()), null);
            byte[] protectedTail = Container(Header(), new("IDAT", [.. stream, 170]), End());
            protectedTail[41 + stream.Length] ^= 1;
            Check(executable, directory, "ignored tail crc still checked", protectedTail, null);

            foreach (string[] arguments in new[] { Array.Empty<string>(), new[] { "one", "two" } })
            {
                var usage = NativeToolchain.Run(executable, arguments, 30000);
                Assert.Equal(2, usage.ExitCode); Assert.Empty(usage.StandardOutput);
                Assert.Equal("Usage: png-verify <file>\n", Normalize(usage.StandardError));
            }
            var missing = NativeToolchain.Run(executable, [Path.Combine(directory, "missing.png")], 30000);
            Assert.Equal(1, missing.ExitCode); Assert.Empty(missing.StandardOutput);
            Assert.Contains("File open failed", missing.StandardError);

            // A sparse input exercises the actual ReadAll bound without a 32 MiB host literal.
            string huge = Path.Combine(directory, "archive limit.png");
            using (var file = File.Create(huge)) file.SetLength(33554433);
            byte[] beforeHash = HashFile(huge);
            string[] beforeFiles = Directory.GetFiles(directory).Order(StringComparer.Ordinal).ToArray();
            var limit = NativeToolchain.Run(executable, [huge], 120000);
            Assert.Equal(1, limit.ExitCode); Assert.Empty(limit.StandardOutput);
            Assert.Contains("File exceeds maximum byte count", limit.StandardError);
            Assert.Equal(beforeHash, HashFile(huge));
            Assert.Equal(beforeFiles, Directory.GetFiles(directory).Order(StringComparer.Ordinal).ToArray());
        }
        catch (Exception error) { error.Data["PngVerifyExampleDirectory"] = directory; throw; }
        await TestDirectoryCleanup.DeleteAsync(directory);
    }

    private static void Check(string executable, string directory, string name, byte[] bytes, string? expected, string? reason = null)
    {
        string path = Path.Combine(directory, name + ".png"); File.WriteAllBytes(path, bytes);
        string[] before = Directory.GetFiles(directory).Order(StringComparer.Ordinal).ToArray();
        var result = NativeToolchain.Run(executable, [path], 30000);
        if (expected != null)
        {
            Assert.True(result.ExitCode == 0, name + ": " + result.StandardError);
            Assert.Equal(expected, Normalize(result.StandardOutput)); Assert.Empty(result.StandardError);
        }
        else
        {
            Assert.True(result.ExitCode == 1, name + ": " + result.StandardError);
            Assert.Empty(result.StandardOutput); Assert.StartsWith("png-verify: ", result.StandardError);
            if (reason != null) Assert.Contains(reason, result.StandardError);
        }
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Equal(before, Directory.GetFiles(directory).Order(StringComparer.Ordinal).ToArray());
    }
    private static byte[] HashFile(string path) { using var file = File.OpenRead(path); return SHA256.HashData(file); }
    private static string Normalize(string text) => text.Replace("\r\n", "\n");
    private static Chunk End() => new("IEND", []);
    private static Chunk Header(uint width = 1, uint height = 1, byte depth = 8, byte color = 2, byte compression = 0, byte filter = 0, byte interlace = 0)
    {
        byte[] data = new byte[13]; BinaryPrimitives.WriteUInt32BigEndian(data, width); BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), height);
        data[8] = depth; data[9] = color; data[10] = compression; data[11] = filter; data[12] = interlace; return new("IHDR", data);
    }
    private static byte[] Rows(int width, int height, int channels)
    {
        int stride = width * channels + 1; byte[] rows = new byte[stride * height];
        for (int y = 0; y < height; y++)
        {
            rows[y * stride] = (byte)(y % 5);
            for (int x = 1; x < stride; x++)
                rows[y * stride + x] = x == 1 ? (byte)0 : x == 2 ? (byte)255 : (byte)((x * 73 + y * 29) & 255);
        }
        return rows;
    }
    private static byte[] Compress(byte[] rows, CompressionLevel level = CompressionLevel.Fastest)
    {
        using var output = new MemoryStream();
        using (var encoder = new ZLibStream(output, level, leaveOpen: true)) encoder.Write(rows);
        return output.ToArray();
    }
    private static void Oracle(byte[] compressed, byte[] rows)
    {
        using var input = new MemoryStream(compressed);
        using var decoder = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream(); decoder.CopyTo(output); Assert.Equal(rows, output.ToArray());
    }
    private static byte[] Container(params Chunk[] chunks)
    {
        using var output = new MemoryStream(); output.Write(Signature);
        byte[] field = new byte[4];
        foreach (Chunk chunk in chunks)
        {
            byte[] type = Encoding.ASCII.GetBytes(chunk.Type); Assert.Equal(4, type.Length);
            BinaryPrimitives.WriteUInt32BigEndian(field, (uint)chunk.Data.Length); output.Write(field);
            output.Write(type); output.Write(chunk.Data);
            BinaryPrimitives.WriteUInt32BigEndian(field, Crc([.. type, .. chunk.Data])); output.Write(field);
        }
        return output.ToArray();
    }
    private static uint Crc(byte[] bytes)
    {
        // Every chunk includes four type bytes, so GZipStream cannot omit an empty member.
        using var output = new MemoryStream();
        using (var encoder = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true)) encoder.Write(bytes);
        byte[] encoded = output.ToArray();
        return BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(encoded.Length - 8, 4));
    }
}
