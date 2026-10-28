using System.Text;

namespace gflat.Tests.Std.Hashing;

public sealed class CRC32CTests
{
    private static void AssertReferenceAnchors()
    {
        // RFC 3720 B.4 prints digest bytes in wire order; these are numeric values.
        Assert.Equal(0x8a9136aau, CRC32CReference.Compute(new byte[32]));
        Assert.Equal(0x62a8ab43u, CRC32CReference.Compute(Enumerable.Repeat((byte)255, 32).ToArray()));
        Assert.Equal(0x46dd794eu, CRC32CReference.Compute(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray()));
        Assert.Equal(0x113fdb5cu, CRC32CReference.Compute(Enumerable.Range(0, 32).Reverse().Select(i => (byte)i).ToArray()));
        Assert.Equal(0xe3069283u, CRC32CReference.Compute(Encoding.ASCII.GetBytes("123456789")));
        Assert.Equal(0u, CRC32CReference.Compute([]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CountedBytesEverySplitCopiedStatesAndResetMatchIndependentPolynomialOracle(int optimization)
    {
        AssertReferenceAnchors();
        List<byte[]> samples = [[], Encoding.ASCII.GetBytes("123456789"), new byte[32],
            Enumerable.Repeat((byte)255, 32).ToArray(),
            Enumerable.Range(0, 32).Select(i => (byte)i).ToArray(),
            Enumerable.Range(0, 32).Reverse().Select(i => (byte)i).ToArray()];
        for (int value = 0; value < 256; value++) samples.Add([(byte)value]);
        var random = new Random(827361);
        foreach (int length in new[] { 2, 7, 31, 33, 63, 64, 65, 127, 128, 129, 255, 256, 257 })
        {
            byte[] bytes = new byte[length];
            random.NextBytes(bytes);
            samples.Add(bytes);
        }
        for (int i = 0; i < 24; i++)
        {
            byte[] bytes = new byte[random.Next(258)];
            random.NextBytes(bytes);
            samples.Add(bytes);
        }
        using var packet = new MemoryStream();
        using (var writer = new BinaryWriter(packet, Encoding.UTF8, leaveOpen: true))
            foreach (byte[] bytes in samples)
            {
                writer.Write((ushort)bytes.Length);
                writer.Write(CRC32CReference.Compute(bytes));
                writer.Write(bytes);
            }
        string hex = Convert.ToHexString(packet.ToArray());
        Run($$$"""
            using std; using std::Hashing;
            nuint ReadByte(readonly(char)* text, ref nuint offset) nothrow {
                nuint result = 0;
                for (int i = 0; i < 2; i++) {
                    char digit = text[offset]; offset++;
                    nuint value = (nuint)((int)digit - (int)'0');
                    if (digit >= 'A') value = (nuint)((int)digit - (int)'A' + 10);
                    result = result * 16 + value;
                }
                return result;
            }
            int main() {
                readonly(char)* packet = "{{{hex}}}";
                nuint offset = 0;
                char[259] storage; char[259] saved;
                for (int row = 0; row < {{{samples.Count}}}; row++) {
                    nuint count = ReadByte(packet, ref offset);
                    count = count | (ReadByte(packet, ref offset) << 8);
                    uint expected = 0u;
                    for (int i = 0; i < 4; i++) expected = expected | ((uint)ReadByte(packet, ref offset) << (i * 8));
                    if (count > 257) return 1;
                    for (nuint i = 0; i < 259; i++) storage[i] = (char)165;
                    for (nuint i = 0; i < count; i++) storage[i + 1] = (char)ReadByte(packet, ref offset);
                    for (nuint i = 0; i < 259; i++) saved[i] = storage[i];
                    for (nuint split = 0; split <= count; split++) {
                        CRC32C first = new();
                        first.Append(&storage[1], split);
                        uint prefix = first.GetValue();
                        if (first.GetValue() != prefix) return 2;
                        CRC32C second = first;
                        first.Append(&storage[1] + split, count - split);
                        if (second.GetValue() != prefix) return 3;
                        second.Append(&storage[1] + split, count - split);
                        if (first.GetValue() != expected || second.GetValue() != expected || first.GetValue() != expected) return 4;
                        second.Reset();
                        if (second.GetValue() != 0u) return 5;
                        second.Append((readonly(char)*)4096, 0);
                        if (second.GetValue() != 0u) return 6;
                        second.Append(&storage[1], count);
                        if (second.GetValue() != expected) return 7;
                    }
                    for (nuint i = 0; i < 259; i++) if (storage[i] != saved[i]) return 8;
                }
                if (offset != {{{hex.Length}}}) return 9;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void LegalOwnStateInputMatchesCapturedInitializedBytes(int optimization)
        => Run("""
            using std; using std::Hashing;
            int main() {
                for (int seeded = 0; seeded < 2; seeded++) {
                    for (nuint offset = 0; offset <= (nuint)sizeof(CRC32C); offset++) {
                        CRC32C original = new();
                        if (seeded != 0) original.Append("123456789", 9);
                        CRC32C captured = original;
                        char[sizeof(CRC32C)] bytes;
                        readonly(char)* view = (readonly(char)*)&original;
                        for (nuint i = 0; i < (nuint)sizeof(CRC32C); i++) bytes[i] = view[i];
                        nuint count = (nuint)sizeof(CRC32C) - offset;
                        captured.Append((readonly(char)*)bytes + offset, count);
                        original.Append(view + offset, count);
                        if (original.GetValue() != captured.GetValue()) return 1;
                    }
                }
                return 0;
            }
            """, optimization);

    private static void Run(string code, int optimization)
    {
        var library = new SourceFile("CRC32C.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core/Hashing/CRC32C.gf")));
        var sources = new[] { library, new SourceFile("crc32c-test.gf", code) };
        string ir = Compiler.Emit(sources, options: new() { NoStdLib = true, NoBuiltins = true, InternalizeFunctions = true });
        foreach (string forbidden in new[] { "@__gflat_eh_", "personality", "@malloc", "@free", "gflat$Memory$", "@__gflat_gc_" })
            Assert.DoesNotContain(forbidden, ir);
        var headers = System.Text.RegularExpressions.Regex.Matches(ir, @"(?m)^define[^\r\n]*CRC32C[^\r\n]*\{");
        Assert.NotEmpty(headers);
        foreach (System.Text.RegularExpressions.Match header in headers) Assert.Contains("nounwind", header.Value);
        string directory = Path.Combine(Path.GetTempPath(), "gflat-crc32c-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "crc32c.ll");
            string output = Path.Combine(directory, "crc32c.exe");
            File.WriteAllText(input, ir);
            var compiled = NativeToolchain.Compile(input, output, optimizationLevel: optimization);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            var result = NativeToolchain.Run(output, []);
            Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
        }
        finally { Directory.Delete(directory, true); }
    }
}
