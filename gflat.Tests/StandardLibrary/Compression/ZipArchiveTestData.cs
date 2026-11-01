using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace gflat.Tests.Std.Compression;

internal static class ZipArchiveTestData
{
    internal sealed record Item(string Name, byte[] Data, CompressionLevel Level);
    internal sealed record Expected(string Name, byte[] NameBytes, byte[] Data, ushort Flags, ushort Method, uint Crc, uint Compressed);
    internal sealed record Fixture(byte[] Bytes, Expected[] Entries);

    internal static Encoding Cp437()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(437, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }

    internal static Fixture Create(Item[] items, bool streaming = false, Encoding? names = null)
    {
        names ??= new UTF8Encoding(false, true);
        using var target = new MemoryStream();
        Stream output = streaming ? new NonSeekable(target) : target;
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true, names))
        {
            foreach (var item in items)
            {
                var entry = archive.CreateEntry(item.Name, item.Level);
                entry.LastWriteTime = new DateTimeOffset(2020, 1, 2, 3, 4, 6, TimeSpan.Zero);
                using var stream = entry.Open();
                stream.Write(item.Data);
            }
        }
        byte[] bytes = target.ToArray();
        using var input = new MemoryStream(bytes);
        using var reader = new ZipArchive(input, ZipArchiveMode.Read, false, names);
        Assert.Equal(items.Length, reader.Entries.Count);
        var expected = new List<Expected>();
        int at = Central(bytes);
        for (int index = 0; index < items.Length; index++)
        {
            var entry = reader.Entries[index];
            using var source = entry.Open();
            using var decoded = new MemoryStream();
            source.CopyTo(decoded);
            Assert.Equal(items[index].Data, decoded.ToArray());
            Assert.Equal(items[index].Name, entry.FullName);
            ushort flags = U16(bytes, at + 8), method = U16(bytes, at + 10);
            byte[] name = bytes.AsSpan(at + 46, U16(bytes, at + 28)).ToArray();
            expected.Add(new(entry.FullName, name, decoded.ToArray(), flags, method, U32(bytes, at + 16), U32(bytes, at + 20)));
            at += 46 + U16(bytes, at + 28) + U16(bytes, at + 30) + U16(bytes, at + 32);
        }
        return new(bytes, expected.ToArray());
    }

    internal static ushort U16(byte[] bytes, int at) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at));
    internal static uint U32(byte[] bytes, int at) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at));
    internal static void Put16(byte[] bytes, int at, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at), value);
    internal static void Put32(byte[] bytes, int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), value);
    internal static int Central(byte[] bytes) => checked((int)U32(bytes, bytes.Length - 6));
    internal static int DataStart(byte[] bytes) => 30 + U16(bytes, 26) + U16(bytes, 28);

    internal static byte[] WithComment(byte[] original, byte[] comment)
    {
        byte[] bytes = [.. original, .. comment];
        Put16(bytes, original.Length - 2, checked((ushort)comment.Length));
        return bytes;
    }

    internal static byte[] WithoutDescriptorSignature(byte[] original)
    {
        int central = Central(original), descriptor = DataStart(original) + checked((int)U32(original, central + 20));
        Assert.Equal(0x08074b50u, U32(original, descriptor));
        byte[] bytes = [.. original[..descriptor], .. original[(descriptor + 4)..]];
        Put32(bytes, bytes.Length - 6, (uint)(central - 4));
        return bytes;
    }

    internal static byte[] WithPayload(byte[] original, byte[] payload, ushort method, uint length, uint crc)
    {
        int data = DataStart(original), central = Central(original);
        Assert.Equal(0, U16(original, 6) & 8);
        Assert.Equal(central, data + checked((int)U32(original, central + 20)));
        byte[] bytes = [.. original[..data], .. payload, .. original[central..]];
        int next = data + payload.Length;
        ushort version = method == 8 ? (ushort)20 : (ushort)10;
        Put16(bytes, 4, version); Put16(bytes, 8, method);
        Put32(bytes, 14, crc); Put32(bytes, 18, (uint)payload.Length); Put32(bytes, 22, length);
        Put16(bytes, next + 6, version); Put16(bytes, next + 10, method);
        Put32(bytes, next + 16, crc); Put32(bytes, next + 20, (uint)payload.Length); Put32(bytes, next + 24, length);
        Put32(bytes, bytes.Length - 6, (uint)next);
        return bytes;
    }

    internal static byte[] WithLocalExtra(byte[] original, byte[] extra)
    {
        Assert.Equal(0, U16(original, 28));
        int at = DataStart(original), central = Central(original);
        byte[] bytes = [.. original[..at], .. extra, .. original[at..]];
        Put16(bytes, 28, checked((ushort)extra.Length));
        Put32(bytes, bytes.Length - 6, (uint)(central + extra.Length));
        return bytes;
    }

    internal static string Array(string name, byte[] bytes)
        => $"const byte[{bytes.Length}] {name} = [" + string.Join(",", bytes.Select(value => value.ToString(System.Globalization.CultureInfo.InvariantCulture))) + "];\n";

    private sealed class NonSeekable(Stream target) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => target.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => target.Write(buffer, offset, count);
        public override void Write(ReadOnlySpan<byte> buffer) => target.Write(buffer);
    }
}
