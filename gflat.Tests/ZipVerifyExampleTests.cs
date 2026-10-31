using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace gflat.Tests;

public sealed class ZipVerifyExampleTests
{
    private sealed record Item(string Name, byte[] Data, CompressionLevel Level);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ActualVerifierChecksPayloadsDescriptorsBoundsAndLeavesInputsUnchanged(int optimization)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-zip-verify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Standard"), "*.gf", SearchOption.AllDirectories)
                .Select(path => new SourceFile(path, File.ReadAllText(path)))
                .Append(new SourceFile("zip-verify.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Examples/zip-verify.gf"))));
            string ir = Path.Combine(directory, "zip-verify.ll"), executable = Path.Combine(directory, "zip-verify.exe");
            File.WriteAllText(ir, Compiler.Emit(sources, options: new() { InternalizeFunctions = true }));
            var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);

            Item[] items = [
                new("stored.bin", Enumerable.Range(0, 256).Select(index => (byte)index).ToArray(), CompressionLevel.NoCompression),
                new("deflated.bin", Encoding.UTF8.GetBytes(new string('x', 5000) + "\0counted\u20ac"), CompressionLevel.Optimal),
                new("folder/", [], CompressionLevel.NoCompression),
                new("../not extracted-\0\n\u001b\"", [0, 255, 128], CompressionLevel.Optimal)
            ];
            byte[] archive = Archive(items);
            Oracle(archive, items);
            Assert.Equal(0, Read16(archive, CentralStart(archive) + 10));
            Check(executable, directory, "ordinary archive", archive, 4);
            Check(executable, directory, "empty", Archive([]), 0);
            Check(executable, directory, "comment", WithComment(archive, "archive comment"u8.ToArray()), 4);
            Check(executable, directory, "maximum comment", WithComment(archive, new byte[65535]), 4);
            byte[] fake = new byte[22]; Put32(fake, 0, 0x06054b50); Put16(fake, 4, 1);
            Check(executable, directory, "invalid candidate in comment", WithComment(archive, fake), 4);

            byte[] empty = Archive([new("empty", [], CompressionLevel.NoCompression)]);
            Assert.Equal(0, Read16(empty, CentralStart(empty) + 10));
            Check(executable, directory, "repeated empty reference", RepeatSingleEntry(empty, 2), 2);
            Check(executable, directory, "empty deflated stream", WithPayload(empty, [3, 0], 8, 0, 0), 1);
            byte[] emptyContainer = WithPayload(empty, [], 8, 0, 0);
            Oracle(emptyContainer, [new("empty", [], CompressionLevel.NoCompression)]);
            Check(executable, directory, "empty deflated container without data", emptyContainer, 1);
            Check(executable, directory, "missing nonempty deflated stream", WithPayload(empty, [], 8, 1, 0), null);
            Check(executable, directory, "empty deflated wrong CRC", WithPayload(empty, [], 8, 0, 1), null);
            Check(executable, directory, "unknown local extra", WithLocalExtra(empty, [2, 0, 0, 0]), 1);
            Check(executable, directory, "zip64 local extra without sentinels", WithLocalExtra(empty, [1, 0, 0, 0]), null, "Unsupported ZIP feature");
            Check(executable, directory, "truncated local extra", WithLocalExtra(empty, [2, 0, 1, 0]), null);

            Item[] streamedItem = [new("streamed.bin", Enumerable.Range(0, 1000).Select(index => (byte)index).ToArray(), CompressionLevel.Optimal)];
            byte[] streamed = Archive(streamedItem, streaming: true);
            Oracle(streamed, streamedItem);
            int streamedCentral = CentralStart(streamed);
            Assert.Equal(8, Read16(streamed, streamedCentral + 10));
            Assert.NotEqual(0, Read16(streamed, streamedCentral + 8) & 8);
            Assert.Equal(0u, Read32(streamed, 14)); Assert.Equal(0u, Read32(streamed, 18)); Assert.Equal(0u, Read32(streamed, 22));
            int descriptor = DataStart(streamed) + checked((int)Read32(streamed, streamedCentral + 20));
            Assert.Equal(0x08074b50u, Read32(streamed, descriptor));
            Check(executable, directory, "signed descriptor", streamed, 1);
            byte[] unsigned = [.. streamed[..descriptor], .. streamed[(descriptor + 4)..]];
            Put32(unsigned, unsigned.Length - 6, (uint)(streamedCentral - 4));
            Check(executable, directory, "unsigned descriptor", unsigned, 1);
            byte[] badDescriptor = streamed.ToArray(); badDescriptor[descriptor + 4] ^= 1;
            Check(executable, directory, "bad descriptor CRC", badDescriptor, null);
            byte[] badDescriptorSize = streamed.ToArray(); Put32(badDescriptorSize, descriptor + 8, 0);
            Check(executable, directory, "bad descriptor size", badDescriptorSize, null);
            byte[] nonzeroLocalDescriptor = streamed.ToArray(); Put32(nonzeroLocalDescriptor, 14, Read32(streamed, streamedCentral + 16));
            Check(executable, directory, "descriptor nonzero local CRC", nonzeroLocalDescriptor, null);

            int central = CentralStart(archive), end = archive.Length - 22;
            void Changed(string name, Action<byte[]> mutate, string? error = null)
            {
                byte[] changed = archive.ToArray(); mutate(changed);
                Check(executable, directory, name, changed, null, error);
            }
            Changed("local signature", bytes => bytes[0] ^= 1);
            Changed("local name", bytes => bytes[30] ^= 1);
            Changed("local flags", bytes => Put16(bytes, 6, (ushort)(Read16(bytes, 6) ^ 2048)));
            Changed("local method", bytes => Put16(bytes, 8, 8));
            Changed("local date", bytes => Put16(bytes, 12, (ushort)(Read16(bytes, 12) ^ 1)));
            Changed("local CRC", bytes => bytes[14] ^= 1);
            Changed("stored payload CRC", bytes => bytes[DataStart(bytes)] ^= 1);
            Changed("entry offset into central", bytes => Put32(bytes, central + 42, (uint)central));
            Changed("central signature", bytes => bytes[central] ^= 1);
            Changed("encrypted flag", bytes => Put16(bytes, central + 8, (ushort)(Read16(bytes, central + 8) | 1)), "Unsupported ZIP feature");
            Changed("unknown method", bytes => Put16(bytes, central + 10, 65000), "Unsupported ZIP feature");
            Changed("ZIP64 version", bytes => Put16(bytes, central + 6, 45), "Unsupported ZIP feature");
            Changed("ZIP64 entry size", bytes => Put32(bytes, central + 20, uint.MaxValue), "Unsupported ZIP feature");
            Changed("nonzero central disk", bytes => Put16(bytes, central + 34, 1), "Unsupported ZIP feature");
            Changed("EOCD count mismatch", bytes => Put16(bytes, end + 8, 1));
            Changed("spanning", bytes => Put16(bytes, end + 4, 1));
            foreach (int count in new[] { 0, 1, 21, central, archive.Length - 1 })
                Check(executable, directory, "truncated " + count, archive[..count], null);

            Item[] compressedItem = [new("single", Enumerable.Repeat((byte)13, 2000).ToArray(), CompressionLevel.Optimal)];
            byte[] compressed = Archive(compressedItem);
            int compressedCentral = CentralStart(compressed);
            Assert.Equal(8, Read16(compressed, compressedCentral + 10));
            byte[] raw = compressed.AsSpan(DataStart(compressed), checked((int)Read32(compressed, compressedCentral + 20))).ToArray();
            uint crc = Read32(compressed, compressedCentral + 16);
            Check(executable, directory, "trailing compressed bytes", WithPayload(compressed, [.. raw, 170], 8, 2000, crc), null);
            Check(executable, directory, "truncated compressed data", WithPayload(compressed, raw[..^1], 8, 2000, crc), null);
            Check(executable, directory, "wrong uncompressed count", WithPayload(compressed, raw, 8, 2001, crc), null);
            Check(executable, directory, "matching wrong CRC metadata", WithPayload(compressed, raw, 8, 2000, crc ^ 1), null);
            Check(executable, directory, "per entry output limit", WithPayload(compressed, raw, 8, 16777217, crc), null, "ZIP work or output limit exceeded");

            // Budget failures are checked in the metadata prepass, before any
            // repeated decode or output allocation can occur.
            byte[] large = Archive([new("large", new byte[16777216], CompressionLevel.Optimal)]);
            Check(executable, directory, "total output replay limit", RepeatSingleEntry(large, 9), null, "ZIP work or output limit exceeded");
            byte[] emptyBlocks = new byte[5 * 32768];
            for (int block = 0; block < 32768; block++)
            {
                emptyBlocks[block * 5] = block == 32767 ? (byte)1 : (byte)0;
                emptyBlocks[block * 5 + 3] = 255; emptyBlocks[block * 5 + 4] = 255;
            }
            byte[] work = WithPayload(empty, emptyBlocks, 8, 0, 0);
            Check(executable, directory, "total compressed replay limit", RepeatSingleEntry(work, 820), null, "ZIP work or output limit exceeded");
            byte[] extra = new byte[65535]; extra[0] = 2; Put16(extra, 2, 65531);
            Check(executable, directory, "local metadata replay limit", RepeatSingleEntry(WithLocalExtra(empty, extra), 2050), null,
                "ZIP work or output limit exceeded");
            byte[] scanBase = Archive([new("scan", new byte[1048576], CompressionLevel.NoCompression)]);
            byte[] candidates = new byte[22 * 200];
            for (int index = 0; index < 200; index++)
            {
                int at = index * 22;
                Put32(candidates, at, 0x06054b50); Put16(candidates, at + 8, 1); Put16(candidates, at + 10, 1);
                Put32(candidates, at + 12, 1048576);
                Put32(candidates, at + 16, (uint)(scanBase.Length + at - 1048576));
                Put16(candidates, at + 20, (ushort)(candidates.Length - at - 22));
            }
            Check(executable, directory, "EOCD candidate scan limit", WithComment(scanBase, candidates), null, "ZIP work or output limit exceeded");

            foreach (string[] arguments in new[] { System.Array.Empty<string>(), new[] { "one", "two" } })
            {
                var result = NativeToolchain.Run(executable, arguments, 30000);
                Assert.Equal(2, result.ExitCode); Assert.Empty(result.StandardOutput);
                Assert.Equal("Usage: zip-verify <file>\n", Normalize(result.StandardError));
            }
            var missing = NativeToolchain.Run(executable, [Path.Combine(directory, "missing archive.zip")], 30000);
            Assert.Equal(1, missing.ExitCode); Assert.Empty(missing.StandardOutput); Assert.StartsWith("zip-verify: ", missing.StandardError);
        }
        catch (Exception error) { error.Data["ZipVerifyExampleDirectory"] = directory; throw; }
        await TestDirectoryCleanup.DeleteAsync(directory);
    }

    private static void Check(string executable, string directory, string name, byte[] bytes, int? entries, string? error = null)
    {
        string path = Path.Combine(directory, name + ".zip"); File.WriteAllBytes(path, bytes);
        string[] before = Directory.GetFiles(directory).Order(StringComparer.Ordinal).ToArray();
        var result = NativeToolchain.Run(executable, [path], 30000);
        if (entries is { } count)
        {
            Assert.True(result.ExitCode == 0, name + ": " + result.StandardError);
            Assert.Empty(result.StandardError);
            Assert.Equal($"Verified {count} entries\n", Normalize(result.StandardOutput));
        }
        else
        {
            Assert.Equal(1, result.ExitCode); Assert.Empty(result.StandardOutput); Assert.StartsWith("zip-verify: ", result.StandardError);
            if (error != null) Assert.Contains(error, result.StandardError);
        }
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Equal(before, Directory.GetFiles(directory).Order(StringComparer.Ordinal).ToArray());
    }

    private static byte[] Archive(Item[] items, bool streaming = false)
    {
        using var target = new MemoryStream();
        Stream output = target;
        if (streaming) output = new NonSeekable(target);
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true, Encoding.UTF8))
        foreach (var item in items)
        {
            ZipArchiveEntry entry = archive.CreateEntry(item.Name, item.Level);
            using var stream = entry.Open(); stream.Write(item.Data);
        }
        return target.ToArray();
    }

    private static void Oracle(byte[] bytes, Item[] items)
    {
        using var input = new MemoryStream(bytes);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, false, Encoding.UTF8);
        Assert.Equal(items.Length, archive.Entries.Count);
        for (int index = 0; index < items.Length; index++)
        {
            Assert.Equal(items[index].Name, archive.Entries[index].FullName);
            using var entry = archive.Entries[index].Open(); using var restored = new MemoryStream(); entry.CopyTo(restored);
            Assert.Equal(items[index].Data, restored.ToArray());
        }
    }

    private static byte[] WithPayload(byte[] original, byte[] payload, ushort method, uint size, uint crc)
    {
        int central = CentralStart(original), data = DataStart(original);
        Assert.Equal(1, Read16(original, original.Length - 12));
        Assert.Equal(0, Read16(original, 6) & 8);
        Assert.Equal(central, data + checked((int)Read32(original, central + 20)));
        byte[] bytes = [.. original[..data], .. payload, .. original[central..]];
        int nextCentral = data + payload.Length;
        ushort version = method == 8 ? (ushort)20 : (ushort)10;
        Put16(bytes, 4, version); Put16(bytes, 8, method);
        Put32(bytes, 14, crc); Put32(bytes, 18, (uint)payload.Length); Put32(bytes, 22, size);
        Put16(bytes, nextCentral + 6, version); Put16(bytes, nextCentral + 10, method);
        Put32(bytes, nextCentral + 16, crc); Put32(bytes, nextCentral + 20, (uint)payload.Length); Put32(bytes, nextCentral + 24, size);
        Put32(bytes, bytes.Length - 6, (uint)nextCentral);
        return bytes;
    }

    private static byte[] WithLocalExtra(byte[] original, byte[] extra)
    {
        int data = DataStart(original), central = CentralStart(original);
        Assert.Equal(1, Read16(original, original.Length - 12)); Assert.Equal(0, Read16(original, 28));
        byte[] bytes = [.. original[..data], .. extra, .. original[data..]];
        Put16(bytes, 28, checked((ushort)extra.Length)); Put32(bytes, bytes.Length - 6, (uint)(central + extra.Length));
        return bytes;
    }

    private static byte[] RepeatSingleEntry(byte[] original, ushort count)
    {
        int central = CentralStart(original), end = original.Length - 22;
        Assert.Equal(1, Read16(original, end + 10));
        byte[] record = original[central..end], terminator = original[end..];
        using var result = new MemoryStream(); result.Write(original.AsSpan(0, central));
        for (int index = 0; index < count; index++) result.Write(record);
        Put16(terminator, 8, count); Put16(terminator, 10, count);
        Put32(terminator, 12, checked((uint)(record.Length * count))); Put32(terminator, 16, (uint)central);
        result.Write(terminator); return result.ToArray();
    }

    private static byte[] WithComment(byte[] original, byte[] comment)
    {
        byte[] result = [.. original, .. comment]; Put16(result, original.Length - 2, checked((ushort)comment.Length)); return result;
    }
    private static int CentralStart(byte[] bytes) => checked((int)Read32(bytes, bytes.Length - 6));
    private static int DataStart(byte[] bytes) => 30 + Read16(bytes, 26) + Read16(bytes, 28);
    private static ushort Read16(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));
    private static uint Read32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
    private static void Put16(byte[] bytes, int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset, 2), value);
    private static void Put32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, 4), value);
    private static string Normalize(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal);

    private sealed class NonSeekable(Stream target) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => target.Flush();
        public override void Write(byte[] buffer, int offset, int count) => target.Write(buffer, offset, count);
        public override void Write(ReadOnlySpan<byte> buffer) => target.Write(buffer);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
