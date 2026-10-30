using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace gflat.Tests;

public sealed class RiffListExampleTests
{
    private sealed record Chunk(byte[] Id, byte[] Payload, byte Pad = 0);
    private static byte[] FourCC(string value) => Encoding.ASCII.GetBytes(value);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ActualToolMatchesHostChunkOracleAndRejectsBoundsAndTruncation(int optimization)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-riff-list-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Standard"), "*.gf", SearchOption.AllDirectories)
                .Select(path => new SourceFile(path, File.ReadAllText(path)))
                .Append(new SourceFile("riff-list.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Examples/riff-list.gf"))))
                .Append(new SourceFile("no-allocations.gf", """
                    replace void*? Memory::Allocate(nuint size) => null;
                    replace void Memory::Free(void* storage) nothrow {}
                    """));
            string ir = Path.Combine(directory, "riff-list.ll"), executable = Path.Combine(directory, "riff-list.exe");
            File.WriteAllText(ir, Compiler.Emit(sources, options: new() { InternalizeFunctions = true }));
            var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            string input = Path.Combine(directory, "RIFF input.bin");
            void Check(byte[] bytes, int exit, string expected)
            {
                File.WriteAllBytes(input, bytes);
                var run = NativeToolchain.Run(executable, [input], 30000);
                Assert.True(run.ExitCode == exit, $"Expected {exit}, got {run.ExitCode}: {run.StandardError}");
                string output = run.StandardOutput.Replace("\r\n", "\n");
                Assert.Equal(expected, output);
                Assert.All(output, value => Assert.True(value == '\n' || value is >= ' ' and <= '~'));
                Assert.Equal(exit == 0 ? "" : $"Unable to list RIFF: {input}\n", run.StandardError.Replace("\r\n", "\n"));
                Assert.Equal(bytes, File.ReadAllBytes(input));
            }
            void Valid(byte[] form, params Chunk[] chunks)
            {
                byte[] bytes = Riff(form, chunks);
                Check(bytes, 0, Reference(form, chunks, bytes.Length));
            }
            byte[] wave = FourCC("WAVE");
            Valid(wave);
            Valid(wave, new Chunk(FourCC("fmt "), new byte[16]), new Chunk(FourCC("data"), []));
            Valid(FourCC("????"), new Chunk(FourCC("LIST"), [255]), new Chunk(FourCC("JUNK"), [1, 2, 3], 254));
            var random = new Random(68129);
            foreach (int length in new[] { 0, 1, 2, 15, 16, 17, 4095, 4096, 4097, 12289 })
            {
                byte[] payload = new byte[length]; random.NextBytes(payload);
                foreach (byte pad in new byte[] { 0, 255 })
                    Valid(wave, new Chunk(FourCC("odd!"), payload, pad), new Chunk(FourCC("next"), [42], pad), new Chunk(FourCC("zero"), []));
            }
            var everyId = Enumerable.Range(0, 64).Select(row => new Chunk(
                Enumerable.Range(row * 4, 4).Select(value => (byte)value).ToArray(), [])).ToArray();
            Valid(wave, everyId);
            foreach (Chunk chunk in everyId) Valid(chunk.Id);
            for (int row = 0; row < 16; row++)
            {
                var chunks = Enumerable.Range(0, random.Next(1, 15)).Select(_ =>
                {
                    byte[] id = new byte[4], payload = new byte[random.Next(0, 128)];
                    random.NextBytes(id); random.NextBytes(payload);
                    return new Chunk(id, payload, (byte)random.Next(256));
                }).ToArray();
                Valid(wave, chunks);
            }

            Chunk[] small = [new Chunk(FourCC("one!"), [1]), new Chunk(FourCC("even"), [2, 3]), new Chunk(FourCC("last"), [])];
            byte[] complete = Riff(wave, small);
            for (int length = 0; length < complete.Length; length++)
                Check(complete[..length], 1, Reference(wave, small, length));
            foreach (string marker in new[] { "RIFX", "RF64", "NOPE" })
            {
                byte[] bad = Riff(wave); FourCC(marker).CopyTo(bad, 0); Check(bad, 1, "");
            }
            for (uint size = 0; size < 4; size++)
            {
                byte[] bad = Riff(wave); PutSize(bad, 4, size); Check(bad, 1, "");
            }
            for (int tail = 1; tail < 8; tail++)
            {
                byte[] bad = [.. Riff(wave), .. new byte[tail]]; PutSize(bad, 4, (uint)(4 + tail));
                Check(bad, 1, RootLine(wave, (uint)(4 + tail)));
            }
            // The complete child header fits; its declared payload or mandatory pad does not.
            foreach ((uint rootSize, uint chunkSize) in new[] { (12u, 1u), (13u, 1u), (14u, uint.MaxValue) })
            {
                byte[] bad = Riff(wave, new Chunk(FourCC("bad!"), [1]));
                PutSize(bad, 4, rootSize); PutSize(bad, 16, chunkSize);
                Check(bad, 1, RootLine(wave, rootSize));
            }
            // Huge valid-in-root claims must stop at physical EOF without allocating the claim.
            foreach ((uint rootSize, uint chunkSize) in new[] { (uint.MaxValue, uint.MaxValue), (0xfffffffcu, 0xfffffff0u) })
            {
                byte[] bad = Riff(wave, new Chunk(FourCC("huge"), [1]));
                PutSize(bad, 4, rootSize); PutSize(bad, 16, chunkSize);
                Check(bad, 1, RootLine(wave, rootSize));
            }
            byte[] oversized = Riff(wave); PutSize(oversized, 4, uint.MaxValue);
            Check(oversized, 1, RootLine(wave, uint.MaxValue));
            foreach (byte[] tail in new[] { new byte[] { 0 }, Riff(wave) })
                Check([.. complete, .. tail], 1, Reference(wave, small, complete.Length));

            string missing = Path.Combine(directory, "missing.bin");
            var failure = NativeToolchain.Run(executable, [missing], 30000);
            Assert.Equal(1, failure.ExitCode); Assert.Empty(failure.StandardOutput);
            Assert.Equal($"Unable to list RIFF: {missing}\n", failure.StandardError.Replace("\r\n", "\n"));
            foreach (string[] arguments in new[] { Array.Empty<string>(), new[] { missing, "extra" } })
            {
                var usage = NativeToolchain.Run(executable, arguments, 30000);
                Assert.Equal(2, usage.ExitCode); Assert.Empty(usage.StandardOutput);
                Assert.Equal("Usage: riff-list <file>\n", usage.StandardError.Replace("\r\n", "\n"));
            }
        }
        catch (Exception error)
        {
            // Preserve the original failure and its fixture for investigation.
            error.Data["RiffListExampleDirectory"] = directory;
            throw;
        }
        await TestDirectoryCleanup.DeleteAsync(directory);
    }

    private static void PutSize(byte[] bytes, int start, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(start, 4), value);

    private static byte[] Riff(byte[] form, params Chunk[] chunks)
    {
        int size = 4 + chunks.Sum(chunk => 8 + chunk.Payload.Length + (chunk.Payload.Length & 1));
        byte[] bytes = new byte[8 + size];
        FourCC("RIFF").CopyTo(bytes, 0); PutSize(bytes, 4, (uint)size); form.CopyTo(bytes, 8);
        int offset = 12;
        foreach (Chunk chunk in chunks)
        {
            chunk.Id.CopyTo(bytes, offset); PutSize(bytes, offset + 4, (uint)chunk.Payload.Length);
            chunk.Payload.CopyTo(bytes, offset + 8);
            if ((chunk.Payload.Length & 1) != 0) bytes[offset + 8 + chunk.Payload.Length] = chunk.Pad;
            offset += 8 + chunk.Payload.Length + (chunk.Payload.Length & 1);
        }
        return bytes;
    }

    private static string Quote(byte[] id) => "\"" + string.Concat(id.Select(value =>
        value is >= 32 and <= 126 && value != '"' && value != '\\'
            ? ((char)value).ToString() : "\\x" + value.ToString("x2", CultureInfo.InvariantCulture))) + "\"";

    private static string RootLine(byte[] form, uint size)
        => $"0000000000000000 \"RIFF\" {size.ToString(CultureInfo.InvariantCulture)} {Quote(form)}\n";

    private static string Reference(byte[] form, Chunk[] chunks, int available)
    {
        if (available < 12) return "";
        uint size = (uint)(4 + chunks.Sum(chunk => 8 + chunk.Payload.Length + (chunk.Payload.Length & 1)));
        var result = new StringBuilder(RootLine(form, size));
        int offset = 12;
        foreach (Chunk chunk in chunks)
        {
            int next = offset + 8 + chunk.Payload.Length + (chunk.Payload.Length & 1);
            if (next > available) break;
            result.Append(offset.ToString("x16", CultureInfo.InvariantCulture)).Append(' ').Append(Quote(chunk.Id))
                .Append(' ').Append(chunk.Payload.Length.ToString(CultureInfo.InvariantCulture)).Append('\n');
            offset = next;
        }
        return result.ToString();
    }
}
