using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace gflat.Tests;

public sealed class PngListExampleTests
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];
    private sealed record Chunk(byte[] Type, byte[] Data);
    private static byte[] Type(string value) => Encoding.ASCII.GetBytes(value);
    private static Chunk Header() => new(Type("IHDR"), Convert.FromHexString("00000001000000010806000000"));
    private static Chunk End() => new(Type("IEND"), []);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ActualInspectorMatchesHostFramingAndCrcOracleWithoutApplicationAllocation(int optimization)
        => await WithDirectory(directory =>
        {
            Assert.Equal(0xcbf43926u, Crc("123456789"u8.ToArray()));
            Assert.Equal(0xae426082u, Crc(Type("IEND")));
            Assert.Equal(0x1f15c489u, Crc([.. Type("IHDR"), .. Header().Data]));
            var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Standard"), "*.gf", SearchOption.AllDirectories)
                .Select(path => new SourceFile(path, File.ReadAllText(path)))
                .Append(new SourceFile("png-list.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Examples/png-list.gf"))))
                .Append(new SourceFile("no-allocations.gf", """
                    replace void*? Memory::Allocate(nuint size) => null;
                    replace void Memory::Free(void* storage) nothrow {}
                    """));
            string ir = Path.Combine(directory, "png-list.ll"), executable = Path.Combine(directory, "png-list.exe");
            File.WriteAllText(ir, Compiler.Emit(sources, options: new() { InternalizeFunctions = true }));
            var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            string input = Path.Combine(directory, "PNG input.bin");
            void Check(byte[] bytes, int status, string verifiedPrefix, string? reason = null)
            {
                File.WriteAllBytes(input, bytes);
                var result = NativeToolchain.Run(executable, [input], 30000);
                Assert.True(result.ExitCode == status, $"Expected {status}, got {result.ExitCode}: {result.StandardError}");
                string output = result.StandardOutput.Replace("\r\n", "\n");
                if (status == 0) { Assert.Equal(verifiedPrefix, output); Assert.Empty(result.StandardError); }
                else
                {
                    // Buffered verified rows may not yet be drained; no rollback or full-row prefix is promised.
                    Assert.True(verifiedPrefix.StartsWith(output, StringComparison.Ordinal), $"Unexpected unverified output: {output}");
                    Assert.StartsWith($"Unable to list PNG: {input}: ", result.StandardError);
                    Assert.Contains(reason!, result.StandardError, StringComparison.Ordinal);
                }
                Assert.All(output, value => Assert.True(value == '\n' || value is >= ' ' and <= '~'));
                Assert.Equal(bytes, File.ReadAllBytes(input));
            }
            void Accept(params Chunk[] chunks) => Check(Container(chunks), 0, Rows(chunks));
            Accept(Header(), End());
            // A CRC inspector accepts opaque bytes; it does not claim these IDAT bytes form a zlib image.
            Accept(Header(), new Chunk(Type("IDAT"), [0, 255, 128]), End());
            Accept(new Chunk(Type("IHDR"), new byte[13]), new Chunk(Type("ABCD"), []), End());
            var random = new Random(83741);
            foreach (int length in new[] { 0, 1, 2, 255, 4095, 4096, 4097, 8193 })
            {
                byte[] payload = new byte[length]; random.NextBytes(payload);
                Accept(Header(), new Chunk(Type("aaAa"), payload), new Chunk(Type("IDAT"), [0, 1]), End());
            }
            Accept(Header(), new Chunk(Type("aaAa"), Enumerable.Range(0, 256).Select(value => (byte)value).ToArray()), End());
            for (int sample = 0; sample < 12; sample++)
            {
                var chunks = new List<Chunk> { Header() };
                for (int index = 0; index < random.Next(1, 12); index++)
                {
                    byte[] payload = new byte[random.Next(0, 513)]; random.NextBytes(payload);
                    chunks.Add(new(Type(index % 2 == 0 ? "aaAa" : "ABCD"), payload));
                }
                chunks.Add(End()); Accept(chunks.ToArray());
            }
            // Enough output to drain the 4096-byte BufferedWriter more than once.
            Chunk[] many = [Header(), .. Enumerable.Range(0, 400).Select(_ => new Chunk(Type("aaAa"), [])), End()];
            Accept(many);
            byte[] lateCrcFailure = Container(many); lateCrcFailure[^1] ^= 1;
            Check(lateCrcFailure, 1, Rows(many[..^1]), "CRC mismatch");

            Chunk[] small = [Header(), new(Type("aaAa"), [0, 255, 128, 0, 1]), new(Type("IDAT"), [1, 2, 3]), End()];
            byte[] complete = Container(small);
            for (int length = 0; length < complete.Length; length++)
            {
                int completeChunks = 0, offset = 8;
                foreach (Chunk chunk in small)
                {
                    offset += chunk.Data.Length + 12;
                    if (offset <= length) completeChunks++;
                }
                Check(complete[..length], 1, Rows(small[..completeChunks]), "End of input");
            }
            for (int index = 0; index < 8; index++)
            {
                byte[] bad = (byte[])complete.Clone(); bad[index] ^= 1;
                Check(bad, 1, "", "Invalid PNG signature");
            }
            int start = 8;
            for (int index = 0; index < small.Length; index++)
            {
                byte[] bad = (byte[])complete.Clone(); bad[start + 8 + small[index].Data.Length] ^= 1;
                Check(bad, 1, Rows(small[..index]), "CRC mismatch");
                start += small[index].Data.Length + 12;
            }
            byte[] badPayload = (byte[])complete.Clone(); badPayload[41] ^= 1;
            Check(badPayload, 1, Rows(small[..1]), "CRC mismatch");
            byte[] badTypeCrc = (byte[])complete.Clone(); badTypeCrc[37] = (byte)'b';
            Check(badTypeCrc, 1, Rows(small[..1]), "CRC mismatch");
            foreach (byte bad in new byte[] { 0, 31, (byte)'0', (byte)'@', (byte)'[', (byte)'`', (byte)'{', 255 })
            for (int place = 0; place < 4; place++)
            {
                byte[] type = Type("aaAa"); type[place] = bad;
                Check(Container(Header(), new Chunk(type, []), End()), 1, Rows([Header()]), "Invalid PNG chunk type");
            }
            Check(Container(Header(), new Chunk(Type("aaaa"), []), End()), 1, Rows([Header()]), "Reserved PNG chunk type bit");
            foreach (uint length in new[] { 0x80000000u, uint.MaxValue, 0x7fffffffu })
            {
                byte[] bad = (byte[])complete.Clone(); BinaryPrimitives.WriteUInt32BigEndian(bad.AsSpan(33, 4), length);
                Check(bad, 1, Rows(small[..1]), length > int.MaxValue ? "exceeds 31 bits" : "End of input");
            }
            foreach (int length in new[] { 12, 14 })
                Check(Container(new Chunk(Type("IHDR"), new byte[length]), End()), 1, "", "13-byte IHDR");
            Check(Container(new Chunk(Type("IDAT"), []), End()), 1, "", "13-byte IHDR");
            Check(Container(Header(), Header(), End()), 1, Rows([Header()]), "Duplicate PNG IHDR");
            Check(Container(Header(), new Chunk(Type("IEND"), [1])), 1, Rows([Header()]), "IEND must be empty");
            Check(Container(Header()), 1, Rows([Header()]), "End of input");
            Check(Signature, 1, "", "End of input");
            Check(Container(Header(), End(), End()), 1, Rows([Header()]), "Trailing data");
            Check([.. complete, 0], 1, Rows(small[..^1]), "Trailing data");

            string missing = Path.Combine(directory, "missing.bin");
            foreach (string path in new[] { missing, "" })
            {
                var failed = NativeToolchain.Run(executable, [path], 30000);
                Assert.Equal(1, failed.ExitCode); Assert.Empty(failed.StandardOutput);
                Assert.StartsWith("Unable to list PNG: " + path, failed.StandardError);
                Assert.Contains("File open failed", failed.StandardError, StringComparison.Ordinal);
            }
            foreach (string[] arguments in new[] { Array.Empty<string>(), new[] { missing, "extra" } })
            {
                var usage = NativeToolchain.Run(executable, arguments, 30000);
                Assert.Equal(2, usage.ExitCode); Assert.Empty(usage.StandardOutput);
                Assert.Equal("Usage: png-list <file>\n", usage.StandardError.Replace("\r\n", "\n"));
            }
        });

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ActualInspectorReportsImmediateAndBufferedOutputFailures(int optimization)
        => await WithDirectory(directory =>
        {
            string input = Path.Combine(directory, "input.png");
            byte[] bytes = Container(Header(), End()); File.WriteAllBytes(input, bytes);
            ExampleOutputFailure.AssertReportedWithArguments("png-list", optimization,
                $"Unable to list PNG: {input}: Console flush failed", $"Unable to list PNG: {input}: Console write failed", input);
            Assert.Equal(bytes, File.ReadAllBytes(input));
        });

    private static byte[] Container(params Chunk[] chunks)
    {
        using var output = new MemoryStream(); output.Write(Signature);
        byte[] number = new byte[4];
        foreach (Chunk chunk in chunks)
        {
            BinaryPrimitives.WriteUInt32BigEndian(number, (uint)chunk.Data.Length); output.Write(number);
            output.Write(chunk.Type); output.Write(chunk.Data);
            BinaryPrimitives.WriteUInt32BigEndian(number, Crc([.. chunk.Type, .. chunk.Data])); output.Write(number);
        }
        return output.ToArray();
    }

    private static string Rows(Chunk[] chunks)
    {
        ulong offset = 8; var result = new StringBuilder();
        foreach (Chunk chunk in chunks)
        {
            result.Append(offset.ToString("x16", CultureInfo.InvariantCulture)).Append(' ').Append(Encoding.ASCII.GetString(chunk.Type))
                .Append(' ').Append(chunk.Data.Length.ToString(CultureInfo.InvariantCulture)).Append('\n');
            offset += (ulong)chunk.Data.Length + 12;
        }
        return result.ToString();
    }

    private static uint Crc(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true)) gzip.Write(bytes);
        byte[] encoded = output.ToArray();
        return BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(encoded.Length - 8, 4));
    }

    private static async Task WithDirectory(Action<string> action)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-png-list-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { action(directory); }
        catch (Exception error) { error.Data["PngListExampleDirectory"] = directory; throw; }
        await TestDirectoryCleanup.DeleteAsync(directory);
    }
}
