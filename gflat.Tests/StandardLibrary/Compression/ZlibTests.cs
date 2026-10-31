using System.IO.Compression;
using System.Text;

namespace gflat.Tests.Std.Compression;

public sealed class ZlibTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void StreamsValidateFramingDeclaredWindowsAndCallerStorage(int optimization)
    {
        var cases = new List<(byte[] Stream, byte[] Plain)>();
        byte[][] payloads = [[], [0], [0, 255, 128, 0, 65], Encoding.UTF8.GetBytes("zlib\0counted stream\n"),
            Enumerable.Repeat((byte)71, 258).ToArray(), Enumerable.Range(0, 256).Select(i => (byte)i).ToArray()];
        foreach (CompressionLevel level in new[] { CompressionLevel.NoCompression, CompressionLevel.Fastest, CompressionLevel.SmallestSize })
        foreach (byte[] plain in payloads) cases.Add((Encode(plain, level), plain));
        byte[] small = Encode([65, 0, 255], CompressionLevel.Fastest);
        for (int information = 0; information < 8; information++)
            cases.Add((Header(information, (information % 4) << 6).Concat(small.AsSpan(2).ToArray()).ToArray(), [65, 0, 255]));
        cases.Add(([120, 1, 3, 252, 0, 0, 0, 1], [])); // Nonzero final padding, checksum of empty output.
        foreach (var item in cases) VerifyHost(item.Stream, item.Plain);
        var program = new StringBuilder("using Standard::Compression;\n");
        for (int i = 0; i < cases.Count; i++) AppendValid(program, i, cases[i]);
        AppendFailures(program);
        AppendWindows(program);
        program.Append(GuardProgram);
        byte[] first = cases[1].Stream;
        program.Append($$"""
            int Consecutive() nothrow {
                byte[{{first.Length + small.Length + 1}}] source = [{{Bytes(first)}},{{Bytes(small)}},(byte)99];
                byte[4] output;
                nuint consumed = 99; nuint written = 99;
                if (Zlib::Decompress(source, {{first.Length + small.Length + 1}}, output, 4, ref consumed, ref written) !=
                    Zlib::Status::Done || consumed != {{first.Length}} || written != 1 || output[0] != (byte)0) return 1;
                if (Zlib::Decompress((byte*)source + consumed, {{small.Length + 1}}, output, 4, ref consumed, ref written) !=
                    Zlib::Status::Done || consumed != {{small.Length}} || written != 3) return 2;
                if (output[0] != (byte)65 || output[1] != (byte)0 || output[2] != (byte)255) return 3;
                return 0;
            }
            int main() nothrow {
            """);
        for (int i = 0; i < cases.Count; i++) program.Append($"if (Case{i}() != 0) return {i + 1};\n");
        program.Append("if (Failures() != 0) return 100; if (Windows() != 0) return 101; if (Consecutive() != 0) return 102; if (Guards() != 0) return 103; return 0; }");
        string ir = Compiler.Emit(Sources(program.ToString()), options: new() {
            NoStdLib = true, NoBuiltins = true, InternalizeFunctions = true
        });
        Assert.Equal((false, false), NativeToolchain.RequiredNativeRuntimes(ir));
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }

    [Fact]
    public void InternalWindowGuardsAndWrapperNeedNoRuntimeServices()
    {
        string ir = Compiler.Emit(Sources("using Standard::Compression;\n" + GuardProgram + "int main() nothrow => Guards();"),
            options: new() { NoStdLib = true, NoBuiltins = true, InternalizeFunctions = true });
        Assert.Equal((false, false), NativeToolchain.RequiredNativeRuntimes(ir));
        foreach (string forbidden in new[] { "@malloc", "@calloc", "@realloc", "gflat$Memory$Allocate", "personality", "@__gflat_eh_" })
            Assert.DoesNotContain(forbidden, ir);
    }

    private const string GuardProgram = """
        int Guards() nothrow {
            byte input = 0; byte output = 0;
            nuint consumed = 99; nuint written = 99;
            if (Zlib::Decompress(&input, 0, &output, 0, ref consumed, ref written) != Zlib::Status::NeedInput || consumed != 0 || written != 0) return 1;
            consumed = 99; written = 99;
            if (Deflate::DecompressWithWindow(((nuint)1).reinterpret<readonly(byte)*>(), 99,
                ((nuint)1).reinterpret<byte*>(), 99, 0, ref consumed, ref written) != Deflate::Status::InvalidData || consumed != 0 || written != 0) return 2;
            consumed = 99; written = 99;
            if (Deflate::DecompressWithWindow(((nuint)1).reinterpret<readonly(byte)*>(), 99,
                ((nuint)1).reinterpret<byte*>(), 99, 32769, ref consumed, ref written) != Deflate::Status::InvalidData || consumed != 0 || written != 0) return 3;
            if (input != (byte)0 || output != (byte)0) return 4;
            return 0;
        }
        """;

    private static SourceFile[] Sources(string program) => [
        new("BinaryPrimitives.gf", Read("Binary/BinaryPrimitives.gf")),
        new("Adler32.gf", Read("Hashing/Adler32.gf")),
        new("Deflate.gf", Read("Compression/Deflate.gf")),
        new("Zlib.gf", Read("Compression/Zlib.gf")),
        new("zlib-caller-buffers.gf", program)
    ];
    private static string Read(string relative) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard/core", relative));
    private static string Bytes(byte[] bytes) => string.Join(",", bytes.Select(value => "(byte)" + value));

    private static void AppendValid(StringBuilder program, int index, (byte[] Stream, byte[] Plain) item)
    {
        int size = item.Plain.Length + 5;
        int[] capacities = new[] { 0, 1, Math.Max(0, item.Plain.Length - 1), item.Plain.Length, item.Plain.Length + 2 }.Distinct().ToArray();
        program.Append($$"""
            int Case{{index}}() nothrow {
                byte[{{item.Stream.Length + 3}}] input = [(byte)90,{{Bytes(item.Stream)}},(byte)170,(byte)255];
                byte[{{item.Stream.Length + 3}}] original = [(byte)90,{{Bytes(item.Stream)}},(byte)170,(byte)255];
                byte[{{Math.Max(1, item.Plain.Length)}}] expected = [{{Bytes(item.Plain.Length == 0 ? new byte[1] : item.Plain)}}];
                byte[{{size}}] output;
                nuint[{{capacities.Length}}] capacities = [{{string.Join(",", capacities)}}];
                for (nuint c = 0; c < {{capacities.Length}}; c++) {
                    for (nuint j = 0; j < {{size}}; j++) output[j] = (byte)165;
                    nuint consumed = 999; nuint written = 999;
                    Zlib::Status status = Zlib::Decompress((byte*)input + 1, {{item.Stream.Length + 2}},
                        (byte*)output + 1, capacities[c], ref consumed, ref written);
                    if (capacities[c] < {{item.Plain.Length}}) {
                        if (status != Zlib::Status::OutputTooSmall) return 1;
                    } else {
                        if (status != Zlib::Status::Done || consumed != {{item.Stream.Length}} || written != {{item.Plain.Length}}) return 2;
                    }
                    if (consumed > {{item.Stream.Length}} || written > capacities[c] || written > {{item.Plain.Length}} || output[0] != (byte)165) return 3;
                    for (nuint j = 0; j < written; j++) if (output[j + 1] != expected[j]) return 4;
                    for (nuint j = written + 1; j < {{size}}; j++) if (output[j] != (byte)165) return 5;
                    for (nuint j = 0; j < {{item.Stream.Length + 3}}; j++) if (input[j] != original[j]) return 6;
                }
            """);
        if (item.Plain.Length <= 20)
            program.Append($$"""
                for (nuint prefix = 0; prefix < {{item.Stream.Length}}; prefix++) {
                    for (nuint j = 0; j < {{size}}; j++) output[j] = (byte)165;
                    nuint consumed = 999; nuint written = 999;
                    if (Zlib::Decompress((byte*)input + 1, prefix, (byte*)output + 1, {{item.Plain.Length + 2}},
                        ref consumed, ref written) != Zlib::Status::NeedInput || consumed > prefix || written > {{item.Plain.Length}}) return 7;
                    if (output[0] != (byte)165) return 8;
                    for (nuint j = 0; j < written; j++) if (output[j + 1] != expected[j]) return 9;
                    for (nuint j = written + 1; j < {{size}}; j++) if (output[j] != (byte)165) return 10;
                    for (nuint j = 0; j < {{item.Stream.Length + 3}}; j++) if (input[j] != original[j]) return 11;
                }
                """);
        program.Append("return 0; }\n");
    }

    private static void AppendFailures(StringBuilder program)
    {
        byte[] good = [120, 1, 75, 76, 74, 6, 0, 2, 77, 1, 39];
        VerifyHost(good, Encoding.ASCII.GetBytes("abc"));
        byte[] invalidMethod = Header(7, 0); invalidMethod[0] = 119; FixCheck(invalidMethod);
        byte[] invalidInformation = [136, 0]; FixCheck(invalidInformation);
        byte[] invalidCheck = (byte[])good.Clone(); invalidCheck[1] ^= 1;
        var cases = new List<(byte[] Input, string Status, int Consumed, int Written)> {
            ([], "NeedInput", 0, 0), ([120], "NeedInput", 0, 0),
            (invalidMethod, "InvalidData", 2, 0), (invalidInformation, "InvalidData", 2, 0),
            (invalidCheck, "InvalidData", 2, 0), ([120, 1], "NeedInput", 2, 0),
            ([120, 1, 7], "InvalidData", 3, 0)
        };
        byte[] dictionaryHeader = Header(7, 32);
        for (int available = 0; available < 4; available++)
            cases.Add((dictionaryHeader.Concat(new byte[available]).ToArray(), "NeedInput", 2, 0));
        cases.Add((dictionaryHeader.Concat(new byte[] { 0, 0, 0, 1 }).ToArray(), "UnsupportedDictionary", 6, 0));
        cases.Add((dictionaryHeader.Concat(new byte[] { 0, 0, 0, 0, 255, 255 }).ToArray(), "UnsupportedDictionary", 6, 0));
        for (int footer = 0; footer < 4; footer++) cases.Add((good[..(7 + footer)], "NeedInput", 7, 3));
        byte[] corrupt = (byte[])good.Clone(); corrupt[^1] ^= 1;
        cases.Add((corrupt, "InvalidData", good.Length, 3));
        program.Append("int Failures() nothrow {\n");
        for (int i = 0; i < cases.Count; i++) {
            var item = cases[i];
            program.Append($$"""
                {
                    byte[{{item.Input.Length + 2}}] input = [(byte)90,{{Bytes(item.Input)}}{{(item.Input.Length == 0 ? "" : ",")}}(byte)170];
                    byte[{{item.Input.Length + 2}}] original = [(byte)90,{{Bytes(item.Input)}}{{(item.Input.Length == 0 ? "" : ",")}}(byte)170];
                    byte[5] output = [(byte)165,(byte)165,(byte)165,(byte)165,(byte)165];
                    nuint consumed = 999; nuint written = 999;
                    if (Zlib::Decompress((byte*)input + 1, {{item.Input.Length}}, (byte*)output + 1, 3, ref consumed, ref written) !=
                        Zlib::Status::{{item.Status}} || consumed != {{item.Consumed}} || written != {{item.Written}}) return {{i + 1}};
                    if (output[0] != (byte)165 || output[4] != (byte)165) return 90;
                    if (written == 3 && (output[1] != (byte)97 || output[2] != (byte)98 || output[3] != (byte)99)) return 91;
                    if (written == 0 && (output[1] != (byte)165 || output[2] != (byte)165 || output[3] != (byte)165)) return 92;
                    for (nuint j = 0; j < {{item.Input.Length + 2}}; j++) if (input[j] != original[j]) return 93;
                }
                """);
        }
        program.Append("return 0; }\n");
    }

    private static void AppendWindows(StringBuilder program)
    {
        const int historyLength = 32768;
        byte[] history = new byte[historyLength];
        uint random = 0x9e3779b9u;
        for (int i = 0; i < history.Length; i++) { random = unchecked(random * 1664525u + 1013904223u); history[i] = (byte)(random >> 24); }
        var tails = new List<byte>();
        var rows = new List<(int Offset, int Length, int Header0, int Header1, int Distance, int RejectedAt, bool Valid)>();
        var seen = new Dictionary<string, int>();
        void Add(int information, int distance, bool dynamic)
        {
            byte[] match = Enumerable.Range(0, 3).Select(i => history[historyLength - distance + i]).ToArray();
            string key = Convert.ToHexString(match);
            if (seen.TryGetValue(key, out int previous)) Assert.Equal(previous, distance);
            else seen.Add(key, distance);
            var bits = new Bits();
            var code = DistanceCode(distance);
            if (dynamic) {
                bits.Little(5, 3); bits.Little(1, 5); bits.Little((uint)code.Symbol, 5); bits.Little(14, 4);
                int[] order = [16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1];
                int[] lengths = new int[19]; lengths[0] = 2; lengths[1] = 2; lengths[8] = 2; lengths[9] = 2;
                foreach (int symbol in order) bits.Little((uint)lengths[symbol], 3);
                for (int symbol = 0; symbol < 258; symbol++) bits.Symbol(symbol < 254 ? 8 : 9, lengths);
                for (int symbol = 0; symbol <= code.Symbol; symbol++) bits.Symbol(symbol == code.Symbol ? 1 : 0, lengths);
                // Literal tree: 254 symbols of width8 followed by four of width9.
                int[] literal = Enumerable.Repeat(8, 254).Concat(Enumerable.Repeat(9, 4)).ToArray();
                bits.Symbol(257, literal); bits.Code(0, 1);
                bits.Little((uint)code.Extra, code.Width);
                int rejected = (bits.Length + 7) / 8;
                bits.Symbol(256, literal);
                Save(rejected);
            } else {
                bits.Little(3, 3); bits.Fixed(257); bits.Code((uint)code.Symbol, 5); bits.Little((uint)code.Extra, code.Width);
                int rejected = (bits.Length + 7) / 8;
                bits.Fixed(256); Save(rejected);
            }
            void Save(int rejected)
            {
                byte[] tail = bits.Finish();
                byte[] plain = history.Concat(match).ToArray();
                byte[] raw = new byte[] { 0, 0, 128, 255, 127 }.Concat(history).Concat(tail).ToArray();
                using (var source = new MemoryStream(raw))
                using (var decoder = new DeflateStream(source, CompressionMode.Decompress))
                using (var restored = new MemoryStream()) { decoder.CopyTo(restored); Assert.Equal(plain, restored.ToArray()); }
                byte[] checksum = Encode(plain, CompressionLevel.NoCompression)[^4..];
                byte[] footerTail = tail.Concat(checksum).ToArray();
                byte[] header = Header(information, 0);
                bool valid = distance <= (1 << (information + 8));
                if (valid) VerifyHost(header.Concat(raw).Concat(checksum).ToArray(), plain);
                rows.Add((tails.Count, footerTail.Length, header[0], header[1], distance, 2 + 5 + historyLength + rejected, valid));
                tails.AddRange(footerTail);
            }
        }
        for (int information = 0; information < 8; information++) {
            int window = 1 << (information + 8);
            Add(information, window, false);
            if (information != 7) Add(information, window + 1, false);
        }
        Add(0, 256, true); Add(0, 257, true);
        int maxTail = rows.Max(row => row.Length);
        string Values(Func<(int Offset, int Length, int Header0, int Header1, int Distance, int RejectedAt, bool Valid), int> pick)
            => string.Join(",", rows.Select(pick));
        program.Append($$"""
            int Windows() nothrow {
                byte[{{32776 + maxTail + 1}}] input;
                byte[32773] output;
                byte[{{tails.Count}}] tails = [{{Bytes(tails.ToArray())}}];
                nuint[{{rows.Count}}] offsets = [{{Values(row => row.Offset)}}];
                nuint[{{rows.Count}}] lengths = [{{Values(row => row.Length)}}];
                byte[{{rows.Count}}] first = [{{Values(row => row.Header0)}}];
                byte[{{rows.Count}}] second = [{{Values(row => row.Header1)}}];
                nuint[{{rows.Count}}] distances = [{{Values(row => row.Distance)}}];
                nuint[{{rows.Count}}] rejectedAt = [{{Values(row => row.RejectedAt)}}];
                bool[{{rows.Count}}] valid = [{{string.Join(",", rows.Select(row => row.Valid ? "true" : "false"))}}];
                input[0] = (byte)90; input[3] = (byte)0; input[4] = (byte)0; input[5] = (byte)128;
                input[6] = (byte)255; input[7] = (byte)127;
                uint random = 0x9e3779b9u;
                for (nuint i = 0; i < 32768; i++) { random = random * 1664525u + 1013904223u; input[8 + i] = (byte)(random >> 24); }
                for (nuint row = 0; row < {{rows.Count}}; row++) {
                    input[1] = first[row]; input[2] = second[row];
                    for (nuint i = 0; i < lengths[row]; i++) input[32776 + i] = tails[offsets[row] + i];
                    input[32776 + lengths[row]] = (byte)170;
                    for (nuint attempt = 0; attempt < 2; attempt++) {
                        for (nuint i = 0; i < 32773; i++) output[i] = (byte)165;
                        nuint consumed = 999; nuint written = 999;
                        Zlib::Status status = Zlib::Decompress((byte*)input + 1, 32776 + lengths[row],
                            (byte*)output + 1, 32771 - attempt, ref consumed, ref written);
                        if (!valid[row]) {
                            if (status != Zlib::Status::InvalidData || written != 32768 || consumed != rejectedAt[row]) return 1;
                        } else if (attempt == 0) {
                            if (status != Zlib::Status::Done || written != 32771 || consumed != 32775 + lengths[row]) return 2;
                        } else {
                            if (status != Zlib::Status::OutputTooSmall || written != 32768 || consumed != rejectedAt[row]) return 3;
                        }
                        if (output[0] != (byte)165 || output[32772] != (byte)165) return 4;
                        for (nuint i = 0; i < 32768; i++) if (output[i + 1] != input[i + 8]) return 5;
                        for (nuint i = 32768; i < written; i++) if (output[i + 1] != input[8 + i - distances[row]]) return 6;
                        for (nuint i = written + 1; i < 32773; i++) if (output[i] != (byte)165) return 7;
                        random = 0x9e3779b9u;
                        for (nuint i = 0; i < 32768; i++) { random = random * 1664525u + 1013904223u; if (input[8 + i] != (byte)(random >> 24)) return 8; }
                        if (input[0] != (byte)90 || input[1] != first[row] || input[2] != second[row] || input[32776 + lengths[row]] != (byte)170) return 9;
                        for (nuint i = 0; i < lengths[row]; i++) if (input[32776 + i] != tails[offsets[row] + i]) return 10;
                    }
                }
                return 0;
            }
            """);
    }

    private static byte[] Header(int information, int flags)
    {
        byte[] header = [(byte)((information << 4) | 8), (byte)flags]; FixCheck(header); return header;
    }
    private static void FixCheck(byte[] header)
    {
        int highFlags = header[1] & 224;
        header[1] = (byte)(highFlags + ((31 - (((int)header[0] * 256 + highFlags) % 31)) % 31));
    }
    private static byte[] Encode(byte[] plain, CompressionLevel level)
    {
        using var target = new MemoryStream();
        using (var encoder = new ZLibStream(target, level, leaveOpen: true)) encoder.Write(plain);
        byte[] encoded = target.ToArray();
        return encoded.Length == 0 && plain.Length == 0 ? new byte[] { 120, 1, 3, 0, 0, 0, 0, 1 } : encoded;
    }
    private static void VerifyHost(byte[] stream, byte[] plain)
    {
        using var source = new MemoryStream(stream);
        using var decoder = new ZLibStream(source, CompressionMode.Decompress);
        using var restored = new MemoryStream(); decoder.CopyTo(restored); Assert.Equal(plain, restored.ToArray());
    }
    private static (int Symbol, int Extra, int Width) DistanceCode(int distance)
    {
        int first = 1;
        for (int symbol = 0; symbol < 30; symbol++) {
            int width = symbol < 4 ? 0 : symbol / 2 - 1;
            int count = 1 << width;
            if (distance < first + count) return (symbol, distance - first, width);
            first += count;
        }
        throw new ArgumentOutOfRangeException(nameof(distance));
    }
    private sealed class Bits
    {
        private readonly List<byte> bytes = [];
        private uint pending;
        private int count;
        public int Length { get; private set; }
        public void Little(uint value, int width)
        {
            for (int bit = 0; bit < width; bit++) {
                pending |= ((value >> bit) & 1) << count; Length++;
                if (++count == 8) { bytes.Add((byte)pending); pending = 0; count = 0; }
            }
        }
        public void Code(uint value, int width) { for (int bit = width - 1; bit >= 0; bit--) Little((value >> bit) & 1, 1); }
        public void Fixed(int symbol)
        {
            if (symbol <= 143) Code((uint)(48 + symbol), 8);
            else if (symbol <= 255) Code((uint)(400 + symbol - 144), 9);
            else if (symbol <= 279) Code((uint)(symbol - 256), 7);
            else Code((uint)(192 + symbol - 280), 8);
        }
        public void Symbol(int symbol, int[] lengths)
        {
            int target = lengths[symbol]; uint code = 0;
            for (int width = 1; width <= target; width++) {
                code <<= 1;
                if (width == target) {
                    for (int i = 0; i < symbol; i++) if (lengths[i] == width) code++;
                    Code(code, width); return;
                }
                code += (uint)lengths.Count(length => length == width);
            }
            throw new InvalidOperationException();
        }
        public byte[] Finish() { if (count != 0) { bytes.Add((byte)pending); pending = 0; count = 0; } return bytes.ToArray(); }
    }
}
