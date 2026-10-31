using System.IO.Compression;
using System.Text;

namespace gflat.Tests.Std.Compression;

public sealed class DeflateTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RawStreamsMatchIndependentPayloadsAndStayWithinCallerBuffers(int optimization)
    {
        var cases = new List<(string Name, byte[] Compressed, byte[] Plain)>();
        void Valid(string name, byte[] compressed, byte[] plain, bool checkHost = true)
        {
            if (checkHost)
            {
                using var source = new MemoryStream(compressed);
                using var stream = new DeflateStream(source, CompressionMode.Decompress);
                using var restored = new MemoryStream();
                stream.CopyTo(restored);
                Assert.Equal(plain, restored.ToArray());
            }
            cases.Add((name, compressed, plain));
        }
        Valid("empty fixed with nonzero unused padding", [0x03, 0xfc], []);
        Valid("known fixed abc", [0x4b, 0x4c, 0x4a, 0x06, 0x00], Encoding.ASCII.GetBytes("abc"));
        Valid("stored counted zeros and high bytes", Stored([0, 255, 65, 0, 128]), [0, 255, 65, 0, 128]);
        Valid("empty distance alphabet", LiteralDynamic(1, false), [(byte)'A']);
        Valid("singleton end code and empty distances", LiteralDynamic(1, true), []);
        // RFC1951 permits 1..32 declared distance lengths. zlib's normal header
        // parser rejects >30, even when the reserved symbols have zero lengths.
        Valid("RFC distance count 32 with unused reserved slots", LiteralDynamic(32, false), [(byte)'A'], false);
        Valid("repeat16 crosses literal distance boundary", CrossBoundaryRepeat(), new byte[4]);
        Valid("repeat17 resets previous length before repeat16", ResetPrevious(17), [0]);
        Valid("repeat18 resets previous length before repeat16", ResetPrevious(18), [0]);
        Valid("complete literal tree reaches depth15", MaximumDepth(),
            Enumerable.Range(0, 14).Select(value => (byte)value).Concat(new byte[] { 13, 13, 13 }).ToArray());
        Valid("overlapping distance one maximum match", FixedMatch(285, 0, 0, 0, true), Enumerable.Repeat((byte)'A', 259).ToArray());
        // Deliberate zlib-compatible tolerance, outside the RFC table's unique
        // assignment of length258 to code285. Do not let host policy define it.
        Valid("tolerated duplicate length258", FixedMatch(284, 31, 0, 0, true), Enumerable.Repeat((byte)'A', 259).ToArray(), false);
        Valid("fixed to unaligned stored transition", FixedThenStored(), [(byte)'A', 0, 255, 66]);
        byte[] window = Enumerable.Range(0, 32768).Select(index => (byte)index).ToArray();
        var far = new Bits();
        WriteStored(far, window, false);
        far.Little(3, 3); // Final fixed block.
        far.Fixed(285); far.Code(29, 5); far.Little(8191, 13); far.Fixed(256);
        byte[] farStream = far.Finish();
        using (var source = new MemoryStream(farStream))
        using (var decoder = new DeflateStream(source, CompressionMode.Decompress))
        using (var restored = new MemoryStream())
        {
            decoder.CopyTo(restored);
            Assert.Equal(window.Concat(window.Take(258)).ToArray(), restored.ToArray());
        }

        var random = new Random(731951);
        byte[][] payloads = [
            [0], [255], Enumerable.Range(0, 256).Select(index => (byte)index).ToArray(),
            Encoding.UTF8.GetBytes("caller-owned buffers\0preserve counted bytes\n"),
            Enumerable.Repeat((byte)19, 1024).ToArray(),
            Enumerable.Range(0, 1024).Select(index => (byte)(index % 31)).ToArray(),
            Enumerable.Range(0, 1024).Select(_ => (byte)random.Next(256)).ToArray()
        ];
        foreach (CompressionLevel level in new[] { CompressionLevel.NoCompression, CompressionLevel.Fastest, CompressionLevel.SmallestSize })
        foreach (byte[] plain in payloads)
        {
            using var target = new MemoryStream();
            using (var encoder = new DeflateStream(target, level, leaveOpen: true)) encoder.Write(plain);
            Valid("host " + level + " length " + plain.Length, target.ToArray(), plain);
        }

        var program = new StringBuilder("using Standard::Compression;\n");
        for (int index = 0; index < cases.Count; index++) AppendValid(program, index, cases[index]);
        AppendFarHistory(program, farStream.AsSpan(32773).ToArray());
        AppendInvalidCases(program);
        program.Append("int main() nothrow {\n");
        program.Append("nuint emptyRead = 99; nuint emptyWritten = 99; "
            + "if (Deflate::Decompress(((nuint)1).reinterpret<readonly(byte)*>(), 0, ((nuint)1).reinterpret<byte*>(), 0, "
            + "ref emptyRead, ref emptyWritten) != Deflate::Status::NeedInput || emptyRead != 0 || emptyWritten != 0) return 99;\n");
        for (int index = 0; index < cases.Count; index++) program.Append($"if (Case{index}() != 0) return {100 + index};\n");
        program.Append("int invalid = InvalidCases(); if (invalid != 0) return 200 + invalid; "
            + "int far = FarHistory(); if (far != 0) return 230 + far; return 0; }\n");
        string ir = Compiler.Emit(Sources(program.ToString()), options: new() {
            NoStdLib = true, NoBuiltins = true, InternalizeFunctions = true
        });
        Assert.Equal((false, false), NativeToolchain.RequiredNativeRuntimes(ir));
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryLengthAndDistanceCodeMatchesIndependentEndpointFixtures(int optimization)
    {
        const int historyLength = 32768;
        const uint seed = 0x9e3779b9u;
        byte[] history = new byte[historyLength];
        uint state = seed;
        for (int index = 0; index < history.Length; index++)
        {
            state = unchecked(state * 1664525u + 1013904223u);
            history[index] = (byte)(state >> 24);
        }
        var prefixWriter = new Bits();
        WriteStored(prefixWriter, history, false);
        byte[] prefix = prefixWriter.Finish();
        Assert.Equal(historyLength + 5, prefix.Length);

        var rows = new List<(byte[] Tail, int Length, int Distance)>();
        void AddRow(int lengthCode, int lengthWidth, int lengthExtra, int length,
            int distanceCode, int distanceWidth, int distanceExtra, int distance)
        {
            var bits = new Bits();
            bits.Little(3, 3);
            bits.Fixed(lengthCode);
            bits.Little((uint)lengthExtra, lengthWidth);
            bits.Code((uint)distanceCode, 5);
            bits.Little((uint)distanceExtra, distanceWidth);
            bits.Fixed(256);
            byte[] tail = bits.Finish();
            byte[] expected = new byte[historyLength + length];
            history.CopyTo(expected, 0);
            // Forward-copy reference, independent of the native fixture's
            // modulo lookup into its unchanged input history.
            for (int index = 0; index < length; index++)
                expected[historyLength + index] = expected[historyLength + index - distance];
            using var input = new MemoryStream(prefix.Concat(tail).ToArray());
            using var decoder = new DeflateStream(input, CompressionMode.Decompress);
            using var restored = new MemoryStream();
            decoder.CopyTo(restored);
            Assert.True(expected.AsSpan().SequenceEqual(restored.ToArray()),
                $"Host oracle: length code {lengthCode} extra {lengthExtra}; distance code {distanceCode} extra {distanceExtra}");
            rows.Add((tail, length, distance));
        }

        // Derive the ranges from their widening rules, without copying the
        // implementation's length/distance base or extra-bit tables.
        // Code284's maximum field also exercises the documented duplicate258
        // tolerance; the separate code285 row covers its canonical encoding.
        int nextLength = 3;
        for (int code = 257; code <= 284; code++)
        {
            int width = code < 265 ? 0 : (code - 261) / 4;
            AddRow(lengthCode: code, lengthWidth: width, lengthExtra: 0, length: nextLength,
                distanceCode: 2, distanceWidth: 0, distanceExtra: 0, distance: 3);
            int maximum = (1 << width) - 1;
            if (maximum != 0)
                AddRow(lengthCode: code, lengthWidth: width, lengthExtra: maximum, length: nextLength + maximum,
                    distanceCode: 2, distanceWidth: 0, distanceExtra: 0, distance: 3);
            nextLength += 1 << width;
        }
        AddRow(lengthCode: 285, lengthWidth: 0, lengthExtra: 0, length: 258,
            distanceCode: 2, distanceWidth: 0, distanceExtra: 0, distance: 3);
        int nextDistance = 1;
        for (int code = 0; code < 30; code++)
        {
            int width = code < 4 ? 0 : code / 2 - 1;
            AddRow(lengthCode: 285, lengthWidth: 0, lengthExtra: 0, length: 258,
                distanceCode: code, distanceWidth: width, distanceExtra: 0, distance: nextDistance);
            int maximum = (1 << width) - 1;
            if (maximum != 0)
                AddRow(lengthCode: 285, lengthWidth: 0, lengthExtra: 0, length: 258,
                    distanceCode: code, distanceWidth: width, distanceExtra: maximum, distance: nextDistance + maximum);
            nextDistance += 1 << width;
        }
        Assert.Equal(105, rows.Count);
        Assert.Equal(32769, nextDistance);

        int[] selectedDistances = rows.Select(row => row.Distance).Distinct().Order().ToArray();
        string[] prefixes = selectedDistances.Select(distance => Convert.ToHexString(
            Enumerable.Range(0, 3).Select(index => history[historyLength - distance + index % distance]).ToArray())).ToArray();
        // A byte(i) pattern would hide distance errors differing by256. Check
        // that this history actually distinguishes every selected match prefix.
        Assert.Equal(selectedDistances.Length, prefixes.Distinct(StringComparer.Ordinal).Count());
        var offsets = new List<int>();
        int offset = 0;
        foreach (var row in rows) { offsets.Add(offset); offset += row.Tail.Length; }
        byte[] tails = rows.SelectMany(row => row.Tail).ToArray();
        int maximumTail = rows.Max(row => row.Tail.Length);
        const int maximumOutput = historyLength + 258;
        string program = $$"""
            using Standard::Compression;
            int main() nothrow {
                byte[{{prefix.Length + maximumTail + 4}}] input;
                input[0] = (byte)90; input[1] = (byte)0;
                input[2] = (byte)0; input[3] = (byte)128; input[4] = (byte)255; input[5] = (byte)127;
                uint state = {{seed}}u;
                for (nuint index = 0; index < 32768; index++) {
                    state = state * 1664525u + 1013904223u;
                    input[index + 6] = (byte)(state >> 24);
                }
                nuint[{{selectedDistances.Length}}] selectedDistances = [{{string.Join(",", selectedDistances)}}];
                for (nuint left = 0; left < {{selectedDistances.Length}}; left++) {
                    for (nuint right = left + 1; right < {{selectedDistances.Length}}; right++) {
                        bool same = true;
                        for (nuint index = 0; index < 3; index++) {
                            nuint a = selectedDistances[left]; nuint b = selectedDistances[right];
                            if (input[32774 - a + index % a] != input[32774 - b + index % b]) same = false;
                        }
                        if (same) return 11;
                    }
                }
                byte[{{tails.Length}}] tails = [{{Array(tails)}}];
                nuint[{{rows.Count}}] offsets = [{{string.Join(",", offsets)}}];
                nuint[{{rows.Count}}] sizes = [{{string.Join(",", rows.Select(row => row.Tail.Length))}}];
                nuint[{{rows.Count}}] lengths = [{{string.Join(",", rows.Select(row => row.Length))}}];
                nuint[{{rows.Count}}] distances = [{{string.Join(",", rows.Select(row => row.Distance))}}];
                byte[{{maximumOutput + 2}}] output;
                for (nuint row = 0; row < {{rows.Count}}; row++) {
                    for (nuint index = 0; index < sizes[row]; index++) input[32774 + index] = tails[offsets[row] + index];
                    nuint compressed = 32773 + sizes[row];
                    input[compressed + 1] = (byte)170; input[compressed + 2] = (byte)0; input[compressed + 3] = (byte)255;
                    for (nuint attempt = 0; attempt < 2; attempt++) {
                        for (nuint index = 0; index < {{maximumOutput + 2}}; index++) output[index] = (byte)165;
                        nuint capacity = 32768 + lengths[row];
                        if (attempt == 0) capacity--;
                        nuint consumed = 999; nuint written = 999;
                        Deflate::Status status = Deflate::Decompress((byte*)input + 1, compressed + 3, (byte*)output + 1,
                            capacity, ref consumed, ref written);
                        if (attempt == 0) {
                            if (status != Deflate::Status::OutputTooSmall || written != 32768) return 100 + (int)row;
                        } else if (status != Deflate::Status::Done || written != 32768 + lengths[row] || consumed != compressed) return 100 + (int)row;
                        if (consumed > compressed || written > capacity) return 12;
                        if (output[0] != (byte)165 || input[0] != (byte)90 || input[compressed + 1] != (byte)170) return 13;
                        for (nuint index = 0; index < 32768; index++) if (output[index + 1] != input[index + 6]) return 14;
                        if (attempt != 0) {
                            for (nuint index = 0; index < lengths[row]; index++) {
                                byte expected = input[32774 - distances[row] + index % distances[row]];
                                if (output[32769 + index] != expected) return 100 + (int)row;
                            }
                        }
                        for (nuint index = written + 1; index < {{maximumOutput + 2}}; index++) if (output[index] != (byte)165) return 15;
                    }
                }
                state = {{seed}}u;
                for (nuint index = 0; index < 32768; index++) {
                    state = state * 1664525u + 1013904223u;
                    if (input[index + 6] != (byte)(state >> 24)) return 16;
                }
                return 0;
            }
            """;
        string ir = Compiler.Emit(Sources(program), options: new() {
            NoStdLib = true, NoBuiltins = true, InternalizeFunctions = true
        });
        Assert.Equal((false, false), NativeToolchain.RequiredNativeRuntimes(ir));
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }

    [Fact]
    public void DecoderDeclaresNoAllocationOrExceptionRuntimeServices()
    {
        string ir = Compiler.Emit(Sources("""
            using Standard::Compression;
            int main() nothrow {
                byte[2] input = [3, 0]; byte storage = 0;
                nuint consumed = 0; nuint written = 0;
                if (Deflate::Decompress(input, 2, &storage, 0, ref consumed, ref written) != Deflate::Status::Done) return 1;
                return 0;
            }
            """), options: new() { NoStdLib = true, NoBuiltins = true, InternalizeFunctions = true });
        Assert.Equal((false, false), NativeToolchain.RequiredNativeRuntimes(ir));
        foreach (string forbidden in new[] { "@malloc", "@calloc", "@realloc", "gflat$Memory$Allocate", "personality", "@__gflat_eh_" })
            Assert.DoesNotContain(forbidden, ir);
    }

    private static SourceFile[] Sources(string program) => [
        new("Deflate.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard/core/Compression/Deflate.gf"))),
        new("deflate-caller-buffers.gf", program)
    ];

    private static string Array(byte[] bytes) => string.Join(",", bytes.Select(value => "(byte)" + value));

    private static void AppendValid(StringBuilder program, int index, (string Name, byte[] Compressed, byte[] Plain) sample)
    {
        int size = sample.Plain.Length + 5;
        int[] capacities = sample.Plain.Length <= 12
            ? Enumerable.Range(0, sample.Plain.Length + 3).ToArray()
            : new[] { 0, 1, 2, sample.Plain.Length - 1, sample.Plain.Length, sample.Plain.Length + 3 };
        program.Append($$"""
            // {{sample.Name}}
            int Case{{index}}() nothrow {
                byte[{{sample.Compressed.Length + 4}}] input = [(byte)90,{{Array(sample.Compressed)}},(byte)170,(byte)0,(byte)255];
                byte[{{Math.Max(1, sample.Plain.Length)}}] expected = [{{Array(sample.Plain.Length == 0 ? new byte[1] : sample.Plain)}}];
                byte[{{size}}] output;
                nuint[{{capacities.Length}}] capacities = [{{string.Join(",", capacities)}}];
                for (nuint c = 0; c < {{capacities.Length}}; c++) {
                    nuint capacity = capacities[c];
                    for (nuint j = 0; j < {{size}}; j++) output[j] = (byte)165;
                    nuint consumed = 999; nuint written = 999;
                    Deflate::Status status = Deflate::Decompress((byte*)input + 1, {{sample.Compressed.Length + 3}},
                        (byte*)output + 1, capacity, ref consumed, ref written);
                    if (capacity < {{sample.Plain.Length}}) {
                        if (status != Deflate::Status::OutputTooSmall) return 1;
                    } else {
                        if (status != Deflate::Status::Done || consumed != {{sample.Compressed.Length}} || written != {{sample.Plain.Length}}) return 2;
                    }
                    if (consumed > {{sample.Compressed.Length}} || written > capacity || written > {{sample.Plain.Length}}) return 3;
                    if (output[0] != (byte)165 || input[0] != (byte)90 || input[{{sample.Compressed.Length + 1}}] != (byte)170) return 4;
                    for (nuint j = 0; j < written; j++) if (output[j + 1] != expected[j]) return 5;
                    for (nuint j = written + 1; j < {{size}}; j++) if (output[j] != (byte)165) return 6;
                }
            """);
        if (sample.Compressed.Length <= 40)
        {
            program.Append($$"""
                for (nuint prefix = 0; prefix < {{sample.Compressed.Length}}; prefix++) {
                    for (nuint j = 0; j < {{size}}; j++) output[j] = (byte)165;
                    nuint consumed = 999; nuint written = 999;
                    if (Deflate::Decompress((byte*)input + 1, prefix, (byte*)output + 1, {{sample.Plain.Length + 3}},
                        ref consumed, ref written) != Deflate::Status::NeedInput) return 7;
                    if (consumed > prefix || written > {{sample.Plain.Length}} || output[0] != (byte)165) return 8;
                    for (nuint j = 0; j < written; j++) if (output[j + 1] != expected[j]) return 9;
                    for (nuint j = written + 1; j < {{size}}; j++) if (output[j] != (byte)165) return 10;
                }
                """);
        }
        program.Append("return 0; }\n");
    }

    private static void AppendInvalidCases(StringBuilder program)
    {
        var invalid = new List<(byte[] Bytes, string Status)> { ([7], "InvalidData"), ([1, 1, 0, 0, 0, 42], "InvalidData") };
        foreach (uint reserved in new[] { 30u, 31u })
        {
            var bits = new Bits(); bits.Little(5, 3); bits.Little(reserved, 5);
            invalid.Add((bits.Finish(), "InvalidData"));
        }
        foreach (int reserved in new[] { 286, 287 })
        {
            var bits = new Bits(); bits.Little(3, 3); bits.Fixed(reserved);
            invalid.Add((bits.Finish(), "InvalidData"));
        }
        foreach (uint reserved in new[] { 30u, 31u })
            invalid.Add((FixedMatch(257, 0, reserved, 0, true, false), "InvalidData"));
        invalid.Add((FixedMatch(257, 0, 0, 0, false), "InvalidData"));
        invalid.Add((DynamicFailure(0), "InvalidData")); // repeat16 before any length
        invalid.Add((DynamicFailure(1), "InvalidData")); // repeat runs exceed combined alphabet
        invalid.Add((DynamicFailure(2), "InvalidData")); // missing end-of-block code
        invalid.Add((DynamicFailure(3), "InvalidData")); // oversubscribed literal tree
        invalid.Add((DynamicFailure(4), "InvalidData")); // incomplete non-singleton literal tree
        invalid.Add((DynamicFailure(5), "InvalidData")); // singleton literal missing branch
        invalid.Add((DynamicFailure(6), "InvalidData")); // match with empty distance alphabet
        invalid.Add((DynamicFailure(7), "InvalidData")); // oversubscribed code-length tree
        invalid.Add((DynamicFailure(8), "InvalidData")); // incomplete singleton code-length tree
        invalid.Add((DynamicFailure(9), "InvalidData")); // missing branch of singleton distance tree
        program.Append("int InvalidCases() nothrow { byte[600] output; nuint consumed = 0; nuint written = 0;\n");
        for (int index = 0; index < invalid.Count; index++)
        {
            var sample = invalid[index];
            program.Append($$"""
                { byte[{{sample.Bytes.Length}}] input = [{{Array(sample.Bytes)}}];
                  for (nuint j = 0; j < 600; j++) output[j] = (byte)165;
                  if (Deflate::Decompress(input, {{sample.Bytes.Length}}, (byte*)output + 1, 598, ref consumed, ref written)
                      != Deflate::Status::{{sample.Status}}) return {{index + 1}};
                  if (consumed > {{sample.Bytes.Length}} || written > 598 || output[0] != (byte)165 || output[599] != (byte)165) return 100;
                  for (nuint j = written + 1; j < 600; j++) if (output[j] != (byte)165) return 101;
                }
                """);
        }
        program.Append("return 0; }\n");
    }

    private static void AppendFarHistory(StringBuilder program, byte[] tail)
    {
        int sourceLength = 32773 + tail.Length;
        program.Append($$"""
            // Build the large deterministic history at runtime, avoiding tens of
            // thousands of literal stores in the compiler fixture's IR.
            int FarHistory() nothrow {
                byte[{{sourceLength + 4}}] input;
                input[0] = (byte)90; input[1] = (byte)0;
                input[2] = (byte)0; input[3] = (byte)128; input[4] = (byte)255; input[5] = (byte)127;
                for (nuint i = 0; i < 32768; i++) input[6 + i] = (byte)i;
                byte[{{tail.Length}}] tail = [{{Array(tail)}}];
                for (nuint i = 0; i < {{tail.Length}}; i++) input[32774 + i] = tail[i];
                input[{{sourceLength + 1}}] = (byte)170; input[{{sourceLength + 2}}] = (byte)0; input[{{sourceLength + 3}}] = (byte)255;
                byte[33031] output;
                nuint[7] capacities = [0, 1, 32767, 32768, 33025, 33026, 33029];
                for (nuint c = 0; c < 7; c++) {
                    for (nuint j = 0; j < 33031; j++) output[j] = (byte)165;
                    nuint consumed = 999; nuint written = 999;
                    Deflate::Status status = Deflate::Decompress((byte*)input + 1, {{sourceLength + 3}}, (byte*)output + 1,
                        capacities[c], ref consumed, ref written);
                    if (capacities[c] < 33026) {
                        if (status != Deflate::Status::OutputTooSmall) return 1;
                    } else if (status != Deflate::Status::Done || consumed != {{sourceLength}} || written != 33026) return 2;
                    if (consumed > {{sourceLength}} || written > capacities[c] || written > 33026) return 3;
                    if (output[0] != (byte)165 || input[0] != (byte)90 || input[{{sourceLength + 1}}] != (byte)170) return 4;
                    for (nuint j = 0; j < written; j++) if (output[j + 1] != (byte)j) return 5;
                    for (nuint j = written + 1; j < 33031; j++) if (output[j] != (byte)165) return 6;
                }
                return 0;
            }
            """);
    }

    private static byte[] Stored(byte[] plain)
    {
        var bits = new Bits(); WriteStored(bits, plain, true); return bits.Finish();
    }
    private static void WriteStored(Bits bits, byte[] plain, bool final)
    {
        bits.Little(final ? 1u : 0u, 3); bits.Align();
        bits.Little((uint)plain.Length, 16); bits.Little((uint)(plain.Length ^ 65535), 16);
        foreach (byte value in plain) bits.Little(value, 8);
    }
    private static byte[] FixedThenStored()
    {
        var bits = new Bits(); bits.Little(2, 3); bits.Fixed('A'); bits.Fixed(256);
        WriteStored(bits, [0, 255, 66], true); return bits.Finish();
    }
    private static byte[] FixedMatch(int length, uint lengthExtra, uint distance, uint distanceExtra, bool literal, bool end = true)
    {
        var bits = new Bits(); bits.Little(3, 3);
        if (literal) bits.Fixed('A');
        bits.Fixed(length);
        int[] lengths = [0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0];
        bits.Little(lengthExtra, lengths[length - 257]); bits.Code(distance, 5);
        int extra = 0; if (distance >= 4 && distance <= 29) extra = (int)(distance / 2 - 1);
        bits.Little(distanceExtra, extra);
        if (end) bits.Fixed(256);
        return bits.Finish();
    }
    private static byte[] LiteralDynamic(int distanceCount, bool onlyEnd)
    {
        int[] lengths = new int[257 + distanceCount];
        if (!onlyEnd) lengths['A'] = 1;
        lengths[256] = 1;
        var bits = new Bits(); DynamicHeader(bits, 257, distanceCount, lengths);
        if (!onlyEnd) bits.Code(0, 1);
        bits.Code(onlyEnd ? 0u : 1u, 1);
        return bits.Finish();
    }
    private static byte[] CrossBoundaryRepeat()
    {
        var bits = new Bits(); int[] codes = new int[19];
        codes[0] = 2; codes[1] = 2; codes[2] = 2; codes[16] = 3; codes[18] = 3;
        DynamicPrefix(bits, 258, 4, codes);
        bits.Symbol(1, codes); bits.Symbol(18, codes); bits.Little(127, 7);
        bits.Symbol(18, codes); bits.Little(106, 7); // 255 absent literal lengths
        bits.Symbol(2, codes); bits.Symbol(16, codes); bits.Little(2, 2); // five 2s cross the boundary
        bits.Code(0, 1); bits.Code(3, 2); bits.Code(0, 2); bits.Code(2, 2);
        return bits.Finish();
    }
    private static byte[] ResetPrevious(int zeroCode)
    {
        var bits = new Bits(); int[] codes = new int[19];
        codes[0] = 2; codes[1] = 2; codes[16] = 2; codes[17] = 3; codes[18] = 3;
        DynamicPrefix(bits, 257, 1, codes);
        bits.Symbol(1, codes);
        int initial = zeroCode == 17 ? 3 : 11;
        bits.Symbol(zeroCode, codes); bits.Little(0, zeroCode == 17 ? 3 : 7);
        bits.Symbol(16, codes); bits.Little(0, 2);
        int rest = 255 - initial - 3;
        bits.Symbol(18, codes); bits.Little(127, 7); rest -= 138;
        bits.Symbol(18, codes); bits.Little((uint)(rest - 11), 7);
        bits.Symbol(1, codes); bits.Symbol(0, codes);
        bits.Code(0, 1); bits.Code(1, 1);
        return bits.Finish();
    }
    private static byte[] MaximumDepth()
    {
        int[] lengths = new int[259];
        for (int symbol = 0; symbol < 14; symbol++) lengths[symbol] = symbol + 1;
        lengths[256] = 15; lengths[257] = 15; lengths[258] = 1;
        var bits = new Bits(); DynamicHeader(bits, 258, 1, lengths);
        int[] literalLengths = lengths[..258];
        for (int symbol = 0; symbol < 14; symbol++) bits.Symbol(symbol, literalLengths);
        bits.Symbol(257, literalLengths); bits.Code(0, 1); bits.Symbol(256, literalLengths);
        return bits.Finish();
    }
    private static byte[] DynamicFailure(int kind)
    {
        var bits = new Bits(); int[] lengths = new int[258];
        if (kind is 7 or 8)
        {
            int[] codes = new int[19];
            if (kind == 7) { codes[16] = 1; codes[17] = 1; codes[18] = 1; }
            else codes[0] = 1;
            DynamicPrefix(bits, 257, 1, codes);
        }
        else if (kind is 0 or 1)
        {
            int[] codes = new int[19]; codes[0] = 2; codes[1] = 2; codes[16] = 2; codes[18] = 2;
            DynamicPrefix(bits, 257, 1, codes);
            if (kind == 0) { bits.Symbol(16, codes); bits.Little(0, 2); }
            else { bits.Symbol(18, codes); bits.Little(127, 7); bits.Symbol(18, codes); bits.Little(127, 7); }
        }
        else
        {
            if (kind == 2) { lengths['A'] = 1; lengths['B'] = 1; }
            if (kind == 3) { lengths['A'] = 1; lengths['B'] = 1; lengths[256] = 1; }
            if (kind == 4) { lengths['A'] = 2; lengths[256] = 2; }
            if (kind == 5) lengths[256] = 1;
            if (kind == 6) { lengths[256] = 1; lengths[257] = 1; }
            if (kind == 9) { lengths['A'] = 1; lengths[256] = 2; lengths[257] = 2; }
            if (kind is 6 or 9)
            {
                System.Array.Resize(ref lengths, 259);
                if (kind == 9) lengths[258] = 1;
                DynamicHeader(bits, 258, 1, lengths);
                if (kind == 6) bits.Code(1, 1);
                else { bits.Code(0, 1); bits.Code(3, 2); bits.Code(1, 1); }
            }
            else { DynamicHeader(bits, 257, 1, lengths); if (kind == 5) bits.Code(1, 1); }
        }
        return bits.Finish();
    }
    private static void DynamicHeader(Bits bits, int literalCount, int distanceCount, int[] lengths)
    {
        // A complete tree able to encode all literal code lengths directly.
        int[] codes = new int[19];
        for (int i = 0; i < 16; i++) codes[i] = 4;
        DynamicPrefix(bits, literalCount, distanceCount, codes);
        foreach (int length in lengths) bits.Symbol(length, codes);
    }
    private static void DynamicPrefix(Bits bits, int literalCount, int distanceCount, int[] codeLengths)
    {
        int[] order = [16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15];
        bits.Little(5, 3); bits.Little((uint)(literalCount - 257), 5); bits.Little((uint)(distanceCount - 1), 5);
        bits.Little(15, 4);
        foreach (int symbol in order) bits.Little((uint)codeLengths[symbol], 3);
    }
    private sealed class Bits
    {
        private readonly List<byte> bytes = [];
        private uint pending;
        private int count;
        public void Little(uint value, int width)
        {
            for (int bit = 0; bit < width; bit++)
            {
                pending |= ((value >> bit) & 1) << count;
                if (++count == 8) { bytes.Add((byte)pending); pending = 0; count = 0; }
            }
        }
        public void Code(uint value, int width)
        {
            for (int bit = width - 1; bit >= 0; bit--) Little((value >> bit) & 1, 1);
        }
        public void Fixed(int symbol)
        {
            if (symbol <= 143) Code((uint)(48 + symbol), 8);
            else if (symbol <= 255) Code((uint)(400 + symbol - 144), 9);
            else if (symbol <= 279) Code((uint)(symbol - 256), 7);
            else Code((uint)(192 + symbol - 280), 8);
        }
        public void Symbol(int symbol, int[] lengths)
        {
            int targetWidth = lengths[symbol]; uint code = 0;
            for (int width = 1; width <= targetWidth; width++)
            {
                code <<= 1;
                if (width == targetWidth)
                {
                    for (int i = 0; i < symbol; i++) if (lengths[i] == width) code++;
                    Code(code, width); return;
                }
                code += (uint)lengths.Count(length => length == width);
            }
            throw new InvalidOperationException("Symbol has no code");
        }
        public void Align() { if (count != 0) { bytes.Add((byte)pending); pending = 0; count = 0; } }
        public byte[] Finish() { Align(); return bytes.ToArray(); }
    }
}
