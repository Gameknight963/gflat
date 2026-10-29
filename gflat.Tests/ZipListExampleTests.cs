using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace gflat.Tests;

public sealed class ZipListExampleTests
{
    private sealed record Item(string Name, byte[] Data, CompressionLevel Level, ushort Method);
    private sealed record Row(string Name, long Compressed, long Uncompressed, ushort Method);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ActualToolMatchesZipArchiveMetadataAndEscapesCountedNames(int optimization)
        => await WithTool(optimization, (directory, executable) =>
        {
            Item[] items = [
                new("stored.bin", Enumerable.Range(0, 256).Select(i => (byte)i).ToArray(), CompressionLevel.NoCompression, 0),
                new("deflated.txt", Encoding.UTF8.GetBytes(new string('x', 5000)), CompressionLevel.Optimal, 8),
                new("folder/", [], CompressionLevel.NoCompression, 0),
                new("empty", [], CompressionLevel.NoCompression, 0),
                new("unicode-\u20ac-\ud83d\ude00", [0, 255, 128], CompressionLevel.Optimal, 8),
                new("controls-\0\n\r\u001b-\"-\\", [1, 2, 3], CompressionLevel.NoCompression, 0)
            ];
            byte[] archive = Archive(items);
            Row[] rows = Oracle(archive, items);
            Check(executable, directory, "ordinary", archive, Render(rows));
            Check(executable, directory, "empty", Archive([]), Render([]));
            Check(executable, directory, "ordinary-comment", WithComment(archive, "archive comment"u8.ToArray()), Render(rows));
            Check(executable, directory, "maximum-comment", WithComment(archive, Enumerable.Repeat((byte)'x', 65535).ToArray()), Render(rows));
            Check(executable, directory, "empty-with-maximum-comment", WithComment(Archive([]), new byte[65535]), Render([]));

            // Derive expected metadata before comment patches: a host reader may choose the last fake EOCD.
            byte[] wrongDisk = new byte[22]; Put32(wrongDisk, 0, 0x06054b50); Put16(wrongDisk, 4, 1);
            Check(executable, directory, "fake-eocd-disk", WithComment(archive, wrongDisk), Render(rows));
            byte[] impossibleCount = new byte[22]; Put32(impossibleCount, 0, 0x06054b50);
            Put16(impossibleCount, 8, 1); Put16(impossibleCount, 10, 1);
            Put32(impossibleCount, 16, (uint)archive.Length);
            Check(executable, directory, "fake-eocd-count", WithComment(archive, impossibleCount), Render(rows));
            byte[] badDirectory = new byte[22]; Put32(badDirectory, 0, 0x06054b50);
            Put16(badDirectory, 8, 1); Put16(badDirectory, 10, 1); Put32(badDirectory, 12, (uint)archive.Length);
            // The extent fits this candidate, but starts with a local header rather than a central entry.
            Check(executable, directory, "fake-eocd-directory", WithComment(archive, badDirectory), Render(rows));

            // The method column is numeric metadata; unsupported payload methods are not decoded.
            byte[] unknownMethod = archive.ToArray(); Put16(unknownMethod, CentralStart(archive) + 10, 65000);
            Row[] unknownRows = rows.ToArray(); unknownRows[0] = unknownRows[0] with { Method = 65000 };
            Check(executable, directory, "unknown-method", unknownMethod, Render(unknownRows));
            byte[] damagedLocalHeader = archive.ToArray(); damagedLocalHeader[0] ^= 1;
            Check(executable, directory, "local-header-not-inspected", damagedLocalHeader, Render(rows));
        });

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task BadCentralMetadataProducesNoRowsAndCliFailuresPreserveInput(int optimization)
        => await WithTool(optimization, (directory, executable) =>
        {
            byte[] archive = Archive([
                new("first", [1, 2, 3], CompressionLevel.NoCompression, 0),
                new("last", [4, 5], CompressionLevel.Optimal, 8)
            ]);
            int end = archive.Length - 22, central = CentralStart(archive);
            foreach (int length in new[] { 0, 1, 3, 21, central, central + 45, archive.Length - 1 }.Distinct())
                Check(executable, directory, "truncated-" + length, archive[..length], null);
            void Changed(string name, Action<byte[]> mutate)
            {
                byte[] bytes = archive.ToArray(); mutate(bytes); Check(executable, directory, name, bytes, null);
            }
            Changed("eocd-signature", bytes => bytes[end] ^= 1);
            Changed("central-signature", bytes => bytes[central] ^= 1);
            Changed("disk", bytes => Put16(bytes, end + 4, 1));
            Changed("central-start-disk", bytes => Put16(bytes, end + 6, 1));
            Changed("count-disagreement", bytes => Put16(bytes, end + 8, 1));
            Changed("count-too-small", bytes => { Put16(bytes, end + 8, 1); Put16(bytes, end + 10, 1); });
            Changed("count-too-large", bytes => { Put16(bytes, end + 8, 3); Put16(bytes, end + 10, 3); });
            Changed("zip64-count", bytes => { Put16(bytes, end + 8, 65535); Put16(bytes, end + 10, 65535); });
            Changed("zip64-directory-size", bytes => Put32(bytes, end + 12, uint.MaxValue));
            Changed("zip64-directory-offset", bytes => Put32(bytes, end + 16, uint.MaxValue));
            Changed("offset-past-eocd", bytes => Put32(bytes, end + 16, (uint)end + 1));
            Changed("directory-gap", bytes => Put32(bytes, end + 12, (uint)(end - central - 1)));
            Changed("directory-overlap", bytes => Put32(bytes, end + 12, (uint)(end - central + 1)));
            Changed("entry-disk", bytes => Put16(bytes, central + 34, 1));
            foreach (int field in new[] { 20, 24, 42 })
                Changed("zip64-entry-field-" + field, bytes => Put32(bytes, central + field, uint.MaxValue));
            foreach (int field in new[] { 28, 30, 32 })
                Changed("entry-variable-extent-" + field, bytes => Put16(bytes, central + field, 65535));
            Changed("comment-truncated", bytes => Put16(bytes, end + 20, 1));
            byte[] trailingArchive = [.. archive, 0];
            Check(executable, directory, "trailing-archive-byte", trailingArchive, null);
            byte[] trailingDirectory = [.. archive[..end], 0, .. archive[end..]];
            Put32(trailingDirectory, end + 1 + 12, (uint)(end - central + 1));
            Check(executable, directory, "trailing-central-byte", trailingDirectory, null);
            // Locate the second record in this trusted host-created fixture, solely to mutate it.
            int second = central + 46 + Read16(archive, central + 28) + Read16(archive, central + 30) + Read16(archive, central + 32);
            Changed("late-entry-signature", bytes => bytes[second] ^= 1);

            var missing = NativeToolchain.Run(executable, [Path.Combine(directory, "missing.zip")], 30000);
            Assert.Equal(1, missing.ExitCode); Assert.Empty(missing.StandardOutput); Assert.StartsWith("zip-list: ", missing.StandardError);
            foreach (string[] arguments in new[] { Array.Empty<string>(), new[] { "one", "two" } })
            {
                var usage = NativeToolchain.Run(executable, arguments, 30000);
                Assert.Equal(2, usage.ExitCode); Assert.Empty(usage.StandardOutput);
                Assert.Equal("Usage: zip-list <file>\n", Normalize(usage.StandardError));
            }
        });

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ActualToolReportsImmediateAndBufferedOutputFailures(int optimization)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-zip-output-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            byte[] bytes = Archive([new("entry", [1], CompressionLevel.NoCompression, 0)]);
            string input = Path.Combine(directory, "input archive.zip"); File.WriteAllBytes(input, bytes);
            ExampleOutputFailure.AssertReportedWithArguments("zip-list", optimization,
                "zip-list: Console flush failed", "zip-list: Console write failed", input);
            Assert.Equal(bytes, File.ReadAllBytes(input));
        }
        catch (Exception error) { error.Data["ZipListExampleDirectory"] = directory; throw; }
        await TestDirectoryCleanup.DeleteAsync(directory);
    }

    private static async Task WithTool(int optimization, Action<string, string> check)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-zip-list-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
                .Select(path => new SourceFile(path, File.ReadAllText(path)))
                .Append(new SourceFile("zip-list.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Examples/zip-list.gf"))));
            string ir = Path.Combine(directory, "zip-list.ll"), executable = Path.Combine(directory, "zip-list.exe");
            File.WriteAllText(ir, Compiler.Emit(sources, options: new() { InternalizeFunctions = true }));
            var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            check(directory, executable);
        }
        catch (Exception error) { error.Data["ZipListExampleDirectory"] = directory; throw; }
        await TestDirectoryCleanup.DeleteAsync(directory);
    }

    private static void Check(string executable, string directory, string name, byte[] bytes, string? expected)
    {
        string input = Path.Combine(directory, name + ".zip"); File.WriteAllBytes(input, bytes);
        var result = NativeToolchain.Run(executable, [input], 30000);
        if (expected is null)
        {
            Assert.Equal(1, result.ExitCode); Assert.Empty(result.StandardOutput);
            Assert.StartsWith("zip-list: ", result.StandardError);
            Assert.Contains("Unsupported or invalid ZIP32 central directory", result.StandardError);
        }
        else
        {
            Assert.True(result.ExitCode == 0, result.StandardError); Assert.Empty(result.StandardError);
            Assert.Equal(expected, Normalize(result.StandardOutput));
            Assert.All(result.StandardOutput, value => Assert.True(value is '\r' or '\n' or >= ' ' and <= '~'));
        }
        Assert.Equal(bytes, File.ReadAllBytes(input));
    }

    private static byte[] Archive(Item[] items)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true, Encoding.UTF8))
            foreach (Item item in items)
            {
                var entry = archive.CreateEntry(item.Name, item.Level);
                using var output = entry.Open(); output.Write(item.Data);
            }
        return stream.ToArray();
    }

    private static Row[] Oracle(byte[] bytes, Item[] items)
    {
        using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, false, Encoding.UTF8);
        Assert.Equal(items.Select(item => item.Name), archive.Entries.Select(entry => entry.FullName));
        return archive.Entries.Select((entry, index) => new Row(entry.FullName, entry.CompressedLength, entry.Length, items[index].Method)).ToArray();
    }

    private static string Render(Row[] rows)
        => "compressed uncompressed method name\n" + string.Concat(rows.Select(row =>
            row.Compressed.ToString(CultureInfo.InvariantCulture) + " " + row.Uncompressed.ToString(CultureInfo.InvariantCulture) + " " +
            row.Method.ToString(CultureInfo.InvariantCulture) + " " + Quote(Encoding.UTF8.GetBytes(row.Name)) + "\n"));

    private static string Quote(byte[] name) => "\"" + string.Concat(name.Select(value =>
        value is >= 32 and <= 126 && value != (byte)'"' && value != (byte)'\\'
            ? ((char)value).ToString() : "\\x" + value.ToString("X2", CultureInfo.InvariantCulture))) + "\"";

    private static byte[] WithComment(byte[] archive, byte[] comment)
    {
        Assert.True(comment.Length <= ushort.MaxValue);
        byte[] result = [.. archive, .. comment]; Put16(result, archive.Length - 2, (ushort)comment.Length); return result;
    }
    private static int CentralStart(byte[] archive) => checked((int)BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(archive.Length - 6, 4)));
    private static ushort Read16(byte[] bytes, int at) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at, 2));
    private static void Put16(byte[] bytes, int at, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at, 2), value);
    private static void Put32(byte[] bytes, int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at, 4), value);
    private static string Normalize(string value) => value.Replace("\r\n", "\n");
}
