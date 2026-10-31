using System.IO.Compression;
using System.Text;

namespace gflat.Tests.Std.Compression;

public sealed class GzipTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MembersValidateFramingIntegrityAndCallerBufferBounds(int optimization)
    {
        var cases = new List<(byte[] Member, byte[] Plain)>();
        // A host GZipStream can emit no member at all when nothing is written.
        // Use an explicit empty fixed-block member, then host-check its contents.
        cases.Add(([31,139,8,0,0,0,0,0,0,255,3,0,0,0,0,0,0,0,0,0], []));
        byte[][] payloads = [[0], [0, 255, 128, 0, 65], Encoding.UTF8.GetBytes("bounded gzip\0member\n"),
            Enumerable.Repeat((byte)71, 258).ToArray(), Enumerable.Range(0, 256).Select(i => (byte)i).ToArray()];
        foreach (CompressionLevel level in new[] { CompressionLevel.NoCompression, CompressionLevel.Fastest, CompressionLevel.SmallestSize })
        foreach (byte[] plain in payloads) cases.Add((Encode(plain, level), plain));
        byte[] small = Encode([65, 0, 255], CompressionLevel.Fastest);
        foreach (byte flags in new byte[] { 1, 2, 4, 8, 16, 31 })
            cases.Add((WithHeader(small, flags, false), [65, 0, 255]));
        cases.Add((WithHeader(small, 30, true), [65, 0, 255]));
        // Known empty fixed block with deliberately nonzero unused final bits.
        byte[] padded = [31, 139, 8, 0, 0, 0, 0, 0, 0, 255, 3, 252, 0, 0, 0, 0, 0, 0, 0, 0];
        cases.Add((padded, []));
        foreach (var item in cases) VerifyHost(item.Member, item.Plain);

        var program = new StringBuilder("using Standard::Compression;\n");
        for (int i = 0; i < cases.Count; i++) AppendValid(program, i, cases[i]);
        AppendFailures(program);
        AppendLargeExtra(program, small);
        byte[] first = cases[1].Member;
        byte[] second = WithHeader(small, 30, false);
        program.Append($$"""
            int Concatenated() nothrow {
                byte[{{first.Length + second.Length + 1}}] input = [{{Bytes(first)}},{{Bytes(second)}},(byte)99];
                byte[5] output;
                nuint consumed = 999; nuint written = 999;
                if (Gzip::DecompressMember(input, {{first.Length + second.Length + 1}}, output, 5,
                    ref consumed, ref written) != Gzip::Status::Done || consumed != {{first.Length}} || written != 1 || output[0] != (byte)0) return 1;
                if (Gzip::DecompressMember((byte*)input + consumed, {{second.Length + 1}}, output, 5,
                    ref consumed, ref written) != Gzip::Status::Done || consumed != {{second.Length}} || written != 3) return 2;
                if (output[0] != (byte)65 || output[1] != (byte)0 || output[2] != (byte)255) return 3;
                return 0;
            }
            int main() nothrow {
            """);
        for (int i = 0; i < cases.Count; i++) program.Append($"if (Case{i}() != 0) return {i + 1};\n");
        program.Append("if (Failures() != 0) return 100; if (Concatenated() != 0) return 101; if (LargeExtra() != 0) return 102; return 0; }\n");
        string ir = Compiler.Emit(Sources(program.ToString()), options: new() {
            NoStdLib = true, NoBuiltins = true, InternalizeFunctions = true
        });
        Assert.Equal((false, false), NativeToolchain.RequiredNativeRuntimes(ir));
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }

    [Fact]
    public void MemberDecoderNeedsNoAllocationOrExceptionRuntime()
    {
        string ir = Compiler.Emit(Sources("""
            using Standard::Compression;
            int main() nothrow {
                byte source = 0; byte output = 0; nuint consumed = 99; nuint written = 99;
                if (Gzip::DecompressMember(&source, 0, &output, 0, ref consumed, ref written) != Gzip::Status::NeedInput) return 1;
                if (consumed != 0 || written != 0 || source != (byte)0 || output != (byte)0) return 2;
                return 0;
            }
            """), options: new() { NoStdLib = true, NoBuiltins = true, InternalizeFunctions = true });
        Assert.Equal((false, false), NativeToolchain.RequiredNativeRuntimes(ir));
        foreach (string forbidden in new[] { "@malloc", "@calloc", "@realloc", "gflat$Memory$Allocate", "personality", "@__gflat_eh_" })
            Assert.DoesNotContain(forbidden, ir);
    }

    private static SourceFile[] Sources(string program) => [
        new("BinaryPrimitives.gf", Read("Binary/BinaryPrimitives.gf")),
        new("CRC32.gf", Read("Hashing/CRC32.gf")),
        new("Deflate.gf", Read("Compression/Deflate.gf")),
        new("Gzip.gf", Read("Compression/Gzip.gf")),
        new("gzip-caller-buffers.gf", program)
    ];
    private static string Read(string relative) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard/core", relative));
    private static string Bytes(byte[] input) => string.Join(",", input.Select(value => "(byte)" + value));

    private static void AppendValid(StringBuilder program, int index, (byte[] Member, byte[] Plain) item)
    {
        int size = item.Plain.Length + 5;
        int[] capacities = new[] { 0, 1, Math.Max(0, item.Plain.Length - 1), item.Plain.Length, item.Plain.Length + 2 }.Distinct().ToArray();
        program.Append($$"""
            int Case{{index}}() nothrow {
                byte[{{item.Member.Length + 4}}] input = [(byte)90,{{Bytes(item.Member)}},(byte)170,(byte)0,(byte)255];
                byte[{{item.Member.Length + 4}}] original = [(byte)90,{{Bytes(item.Member)}},(byte)170,(byte)0,(byte)255];
                byte[{{Math.Max(1, item.Plain.Length)}}] expected = [{{Bytes(item.Plain.Length == 0 ? new byte[1] : item.Plain)}}];
                byte[{{size}}] output;
                nuint[{{capacities.Length}}] capacities = [{{string.Join(",", capacities)}}];
                for (nuint c = 0; c < {{capacities.Length}}; c++) {
                    for (nuint j = 0; j < {{size}}; j++) output[j] = (byte)165;
                    nuint consumed = 999; nuint written = 999;
                    Gzip::Status status = Gzip::DecompressMember((byte*)input + 1, {{item.Member.Length + 3}},
                        (byte*)output + 1, capacities[c], ref consumed, ref written);
                    if (capacities[c] < {{item.Plain.Length}}) {
                        if (status != Gzip::Status::OutputTooSmall) return 1;
                    } else {
                        if (status != Gzip::Status::Done || consumed != {{item.Member.Length}} || written != {{item.Plain.Length}}) return 2;
                    }
                    if (consumed > {{item.Member.Length}} || written > capacities[c] || written > {{item.Plain.Length}}) return 3;
                    if (output[0] != (byte)165) return 4;
                    for (nuint j = 0; j < written; j++) if (output[j + 1] != expected[j]) return 5;
                    for (nuint j = written + 1; j < {{size}}; j++) if (output[j] != (byte)165) return 6;
                    for (nuint j = 0; j < {{item.Member.Length + 4}}; j++) if (input[j] != original[j]) return 7;
                }
            """);
        if (item.Plain.Length <= 20)
        {
            program.Append($$"""
                for (nuint prefix = 0; prefix < {{item.Member.Length}}; prefix++) {
                    for (nuint j = 0; j < {{size}}; j++) output[j] = (byte)165;
                    nuint consumed = 999; nuint written = 999;
                    if (Gzip::DecompressMember((byte*)input + 1, prefix, (byte*)output + 1, {{item.Plain.Length + 2}},
                        ref consumed, ref written) != Gzip::Status::NeedInput) return 8;
                    if (consumed > prefix || written > {{item.Plain.Length}} || output[0] != (byte)165) return 9;
                    for (nuint j = 0; j < written; j++) if (output[j + 1] != expected[j]) return 10;
                    for (nuint j = written + 1; j < {{size}}; j++) if (output[j] != (byte)165) return 11;
                    for (nuint j = 0; j < {{item.Member.Length + 4}}; j++) if (input[j] != original[j]) return 12;
                }
                """);
        }
        program.Append("return 0; }\n");
    }

    private static void AppendFailures(StringBuilder program)
    {
        byte[] header = [31, 139, 8, 0, 0, 0, 0, 0, 0, 255];
        byte[] good = header.Concat(new byte[] { 75, 76, 74, 6, 0, 194, 65, 36, 53, 3, 0, 0, 0 }).ToArray();
        VerifyHost(good, Encoding.ASCII.GetBytes("abc"));
        var cases = new List<(byte[] Input, string Status, int Consumed, int Written)>();
        for (int field = 0; field < 4; field++) {
            byte[] bad = (byte[])good.Clone(); bad[field] = field == 3 ? (byte)32 : (byte)0;
            cases.Add((bad, "InvalidData", 10, 0));
        }
        byte[] extra = (byte[])header.Clone(); extra[3] = 4;
        cases.Add((extra.Concat(new byte[] { 3 }).ToArray(), "NeedInput", 10, 0));
        cases.Add((extra.Concat(new byte[] { 3, 0, 99 }).ToArray(), "NeedInput", 12, 0));
        foreach (byte flag in new byte[] { 8, 16 }) {
            byte[] text = (byte[])header.Clone(); text[3] = flag;
            cases.Add((text.Concat(new byte[] { 65, 66 }).ToArray(), "NeedInput", 12, 0));
        }
        byte[] crc = (byte[])header.Clone(); crc[3] = 2;
        cases.Add((crc.Concat(new byte[] { 0 }).ToArray(), "NeedInput", 10, 0));
        ushort correct = (ushort)Crc(crc);
        cases.Add((crc.Concat(new byte[] { (byte)(correct ^ 1), (byte)(correct >> 8) }).ToArray(), "InvalidData", 12, 0));
        cases.Add((header, "NeedInput", 10, 0));
        cases.Add((header.Concat(new byte[] { 7 }).ToArray(), "InvalidData", 11, 0));
        for (int footer = 0; footer < 8; footer++) cases.Add((good[..(15 + footer)], "NeedInput", 15, 3));
        foreach (int field in new[] { 15, 19 }) {
            byte[] bad = (byte[])good.Clone(); bad[field] ^= 1;
            cases.Add((bad, "InvalidData", good.Length, 3));
        }
        program.Append("int Failures() nothrow {\n");
        for (int i = 0; i < cases.Count; i++) {
            var item = cases[i];
            program.Append($$"""
                {
                    byte[{{item.Input.Length + 2}}] input = [(byte)90,{{Bytes(item.Input)}},(byte)170];
                    byte[{{item.Input.Length + 2}}] original = [(byte)90,{{Bytes(item.Input)}},(byte)170];
                    byte[5] output = [(byte)165,(byte)165,(byte)165,(byte)165,(byte)165];
                    nuint consumed = 999; nuint written = 999;
                    if (Gzip::DecompressMember((byte*)input + 1, {{item.Input.Length}}, (byte*)output + 1, 3,
                        ref consumed, ref written) != Gzip::Status::{{item.Status}} || consumed != {{item.Consumed}} || written != {{item.Written}}) return {{i + 1}};
                    if (output[0] != (byte)165 || output[4] != (byte)165) return 90;
                    if (written == 3 && (output[1] != (byte)97 || output[2] != (byte)98 || output[3] != (byte)99)) return 91;
                    if (written == 0 && (output[1] != (byte)165 || output[2] != (byte)165 || output[3] != (byte)165)) return 92;
                    for (nuint j = 0; j < {{item.Input.Length + 2}}; j++) if (input[j] != original[j]) return 93;
                }
                """);
        }
        program.Append("return 0; }\n");
    }

    private static void AppendLargeExtra(StringBuilder program, byte[] small)
    {
        byte[] header = new byte[65547];
        new byte[] { 31, 139, 8, 6, 0, 0, 0, 0, 0, 255, 255, 255 }.CopyTo(header, 0);
        // One complete unknown extra subfield: two-byte ID, two-byte length, payload.
        header[12] = 65; header[13] = 66; header[14] = 251; header[15] = 255;
        for (int i = 16; i < header.Length; i++) header[i] = unchecked((byte)(i * 17 + 3));
        uint crc = Crc(header);
        byte[] tail = new byte[] { (byte)crc, (byte)(crc >> 8) }.Concat(small.AsSpan(10).ToArray()).ToArray();
        byte[] member = header.Concat(tail).ToArray();
        VerifyHost(member, [65, 0, 255]);
        program.Append($$"""
            int LargeExtra() nothrow {
                byte[{{member.Length + 1}}] input;
                byte[{{tail.Length}}] tail = [{{Bytes(tail)}}];
                input[0] = (byte)31; input[1] = (byte)139; input[2] = (byte)8; input[3] = (byte)6;
                for (nuint i = 4; i < 9; i++) input[i] = (byte)0;
                input[9] = (byte)255; input[10] = (byte)255; input[11] = (byte)255;
                input[12] = (byte)65; input[13] = (byte)66; input[14] = (byte)251; input[15] = (byte)255;
                for (nuint i = 16; i < 65547; i++) input[i] = (byte)(i * 17 + 3);
                for (nuint i = 0; i < {{tail.Length}}; i++) input[65547 + i] = tail[i];
                input[{{member.Length}}] = (byte)170;
                byte[5] output = [(byte)165,(byte)165,(byte)165,(byte)165,(byte)165];
                nuint consumed = 999; nuint written = 999;
                if (Gzip::DecompressMember(input, 65546, (byte*)output + 1, 3, ref consumed, ref written) !=
                    Gzip::Status::NeedInput || consumed != 12 || written != 0) return 1;
                if (Gzip::DecompressMember(input, {{member.Length + 1}}, (byte*)output + 1, 3, ref consumed, ref written) !=
                    Gzip::Status::Done || consumed != {{member.Length}} || written != 3) return 2;
                if (output[0] != (byte)165 || output[4] != (byte)165 || output[1] != (byte)65 || output[2] != (byte)0 || output[3] != (byte)255) return 3;
                for (nuint i = 16; i < 65547; i++) if (input[i] != (byte)(i * 17 + 3)) return 4;
                if (input[{{member.Length}}] != (byte)170) return 5;
                return 0;
            }
            """);
    }

    private static byte[] Encode(byte[] plain, CompressionLevel level)
    {
        using var target = new MemoryStream();
        using (var encoder = new GZipStream(target, level, leaveOpen: true)) encoder.Write(plain);
        byte[] member = target.ToArray();
        Assert.True(member.Length >= 18, "The host writer did not produce a complete gzip member.");
        return member;
    }
    private static void VerifyHost(byte[] member, byte[] plain)
    {
        using var source = new MemoryStream(member);
        using var decoder = new GZipStream(source, CompressionMode.Decompress);
        using var restored = new MemoryStream(); decoder.CopyTo(restored);
        Assert.Equal(plain, restored.ToArray());
    }
    private static byte[] WithHeader(byte[] member, byte flags, bool empty)
    {
        var header = new List<byte> { 31, 139, 8, flags, 1, 2, 3, 4, 123, 222 };
        if ((flags & 4) != 0) header.AddRange(empty ? new byte[] { 0, 0 } : new byte[] { 7, 0, 65, 66, 3, 0, 255, 0, 128 });
        if ((flags & 8) != 0) header.AddRange(empty ? new byte[] { 0 } : new byte[] { 65, 255, 0 });
        if ((flags & 16) != 0) header.AddRange(empty ? new byte[] { 0 } : new byte[] { 66, 128, 0 });
        if ((flags & 2) != 0) { uint crc = Crc(header.ToArray()); header.Add((byte)crc); header.Add((byte)(crc >> 8)); }
        return header.Concat(member.AsSpan(10).ToArray()).ToArray();
    }
    private static uint Crc(byte[] input)
    {
        uint state = uint.MaxValue;
        foreach (byte value in input) {
            state ^= value;
            for (int i = 0; i < 8; i++) state = (state & 1) == 0 ? state >> 1 : (state >> 1) ^ 0xedb88320u;
        }
        return ~state;
    }
}
