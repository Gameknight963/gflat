using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace gflat.Tests.Std.Hashing;

public class ChecksumTests
{
    private static uint ReferenceCRC32(byte[] bytes)
    {
        // GZipStream can omit the entire stream when no bytes were written.
        if (bytes.Length == 0) return 0;
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true)) gzip.Write(bytes);
        byte[] stream = output.ToArray();
        return BinaryPrimitives.ReadUInt32LittleEndian(stream.AsSpan(stream.Length - 8, 4));
    }

    private static uint ReferenceAdler32(byte[] bytes)
    {
        if (bytes.Length == 0) return 1;
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Fastest, leaveOpen: true)) zlib.Write(bytes);
        byte[] stream = output.ToArray();
        return BinaryPrimitives.ReadUInt32BigEndian(stream.AsSpan(stream.Length - 4, 4));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PublishedCheckValuesAndResetAgreeWithGzipAndZlib(int optimization)
    {
        Assert.Equal(0xcbf43926u, ReferenceCRC32(Encoding.ASCII.GetBytes("123456789")));
        Assert.Equal(0x091e01deu, ReferenceAdler32(Encoding.ASCII.GetBytes("123456789")));
        Assert.Equal(0x11e60398u, ReferenceAdler32(Encoding.ASCII.GetBytes("Wikipedia")));
        Run("""
            int main() {
                std::Hashing::CRC32 crc = new(); std::Hashing::Adler32 adler = new();
                if (crc.GetValue() != 0u || adler.GetValue() != 1u) return 1;
                crc.Append("123456789", 9); adler.Append("123456789", 9);
                if (crc.GetValue() != 0xcbf43926u || adler.GetValue() != 0x091e01deu) return 2;
                if (crc.GetValue() != 0xcbf43926u || adler.GetValue() != 0x091e01deu) return 3;
                crc.Reset(); adler.Reset();
                if (crc.GetValue() != 0u || adler.GetValue() != 1u) return 4;
                adler.Append("Wikipedia", 9);
                if (adler.GetValue() != 0x11e60398u) return 5;
                return 0;
            }
            """, optimization);
    }

    public static IEnumerable<object[]> PatternCases()
    {
        foreach (int optimization in new[] { 0, 2 })
            foreach (var (count, mode) in new[] { (0, 0), (256, 2), (8191, 3), (65520, 1), (65521, 1), (65522, 1), (131079, 1) })
                yield return [optimization, count, mode];
    }

    [Theory]
    [MemberData(nameof(PatternCases))]
    public void WholeAndChunkedStreamsMatchIndependentCompressionTrailers(int optimization, int count, int mode)
    {
        byte[] data = Enumerable.Range(0, count).Select(i => mode switch
        {
            0 => (byte)0,
            1 => (byte)255,
            2 => (byte)i,
            _ => (byte)((i * 73 + (i >> 3) + 19) & 255)
        }).ToArray();
        uint crc = ReferenceCRC32(data), adler = ReferenceAdler32(data);
        string initializer = mode switch { 0 => "(char)0", 1 => "(char)255", 2 => "(char)i", _ => "(char)((i * 73 + (i >> 3) + 19) & 255)" };
        Run($$"""
            int main() {
                char[{{Math.Max(1, count)}}] data;
                for (int i = 0; i < {{count}}; i++) data[i] = {{initializer}};
                std::Hashing::CRC32 crc = new(); std::Hashing::Adler32 adler = new();
                crc.Append(data, {{count}}); adler.Append(data, {{count}});
                if (crc.GetValue() != {{crc}}u || adler.GetValue() != {{adler}}u) return 1;
                crc.Reset(); adler.Reset();
                nuint offset = 0; nuint amount = 0;
                while (offset < (nuint){{count}}) {
                    amount = offset % 97 + 1;
                    if (amount > (nuint){{count}} - offset) amount = (nuint){{count}} - offset;
                    crc.Append(&data[offset], amount); adler.Append(&data[offset], amount);
                    // Empty appends do not read data or alter either state.
                    crc.Append(data, 0); adler.Append(data, 0);
                    offset += amount;
                }
                if (crc.GetValue() != {{crc}}u || adler.GetValue() != {{adler}}u) return 2;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CopyingStreamStateBranchesIndependentlyAndGetValueDoesNotFinalize(int optimization)
    {
        byte[] first = Encoding.ASCII.GetBytes("prefix-first"), second = Encoding.ASCII.GetBytes("prefix-second");
        Run($$"""
            int main() {
                std::Hashing::CRC32 first = new(); std::Hashing::Adler32 firstAdler = new();
                first.Append("prefix-", 7); firstAdler.Append("prefix-", 7);
                uint observed = first.GetValue(); uint adlerObserved = firstAdler.GetValue();
                if (observed != {{ReferenceCRC32(Encoding.ASCII.GetBytes("prefix-"))}}u || adlerObserved != {{ReferenceAdler32(Encoding.ASCII.GetBytes("prefix-"))}}u) return 5;
                std::Hashing::CRC32 second = first; std::Hashing::Adler32 secondAdler = firstAdler;
                first.Append("first", 5); firstAdler.Append("first", 5);
                second.Append("second", 6); secondAdler.Append("second", 6);
                if (first.GetValue() != {{ReferenceCRC32(first)}}u || firstAdler.GetValue() != {{ReferenceAdler32(first)}}u) return 1;
                if (second.GetValue() != {{ReferenceCRC32(second)}}u || secondAdler.GetValue() != {{ReferenceAdler32(second)}}u) return 2;
                second.Reset(); secondAdler.Reset();
                if (first.GetValue() != {{ReferenceCRC32(first)}}u || firstAdler.GetValue() != {{ReferenceAdler32(first)}}u) return 3;
                if (second.GetValue() != 0u || secondAdler.GetValue() != 1u) return 4;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AppendingOwnInitializedStateMatchesIndependentCapturedInput(int optimization)
        => Run("""
            int main() {
                for (int seeded = 0; seeded < 2; seeded++)
                for (nuint offset = 0; offset < 2; offset++) {
                    std::Hashing::CRC32 crc = new();
                    std::Hashing::Adler32 adler = new();
                    if (seeded != 0) { crc.Append("123456789", 9); adler.Append("123456789", 9); }
                    std::Hashing::CRC32 capturedCrc = crc;
                    std::Hashing::Adler32 capturedAdler = adler;
                    char[sizeof(std::Hashing::CRC32)] crcBytes;
                    char[sizeof(std::Hashing::Adler32)] adlerBytes;
                    readonly(char)* crcView = (readonly(char)*)&crc;
                    readonly(char)* adlerView = (readonly(char)*)&adler;
                    for (nuint i = 0; i < (nuint)sizeof(std::Hashing::CRC32); i++) crcBytes[i] = crcView[i];
                    for (nuint i = 0; i < (nuint)sizeof(std::Hashing::Adler32); i++) adlerBytes[i] = adlerView[i];
                    nuint crcCount = (nuint)sizeof(std::Hashing::CRC32) - offset;
                    nuint adlerCount = (nuint)sizeof(std::Hashing::Adler32) - offset;
                    capturedCrc.Append(&crcBytes[offset], crcCount);
                    capturedAdler.Append(&adlerBytes[offset], adlerCount);
                    crc.Append(crcView + offset, crcCount);
                    adler.Append(adlerView + offset, adlerCount);
                    if (crc.GetValue() != capturedCrc.GetValue()) return 1;
                    if (adler.GetValue() != capturedAdler.GetValue()) return 2;
                }
                return 0;
            }
            """, optimization);


    private static void Run(string code, int optimization)
    {
        var sources = new[] { "CRC32.gf", "Adler32.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core/Hashing", name))))
            .Append(new SourceFile("main.gf", code));
        string ir = Compiler.Emit(sources, options: new() { NoStdLib = true, InternalizeFunctions = true });
        foreach (string forbidden in new[] { "@__gflat_eh_", "personality", "gflat$Memory$", "@malloc", "@free" }) Assert.DoesNotContain(forbidden, ir);
        string directory = Path.Combine(Path.GetTempPath(), "gflat-checksum-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "checksums.ll"), output = Path.Combine(directory, "checksums.exe");
            File.WriteAllText(input, ir);
            var compiled = NativeToolchain.Compile(input, output, optimizationLevel: optimization);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            Assert.Equal(0, NativeToolchain.Run(output, []).ExitCode);
        }
        finally { Directory.Delete(directory, true); }
    }
}


