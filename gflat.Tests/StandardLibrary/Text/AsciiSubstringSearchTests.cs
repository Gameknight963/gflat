namespace gflat.Tests.Std.Text;

public sealed class AsciiSubstringSearchTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FoldedHostSpanOracleCoversCountedBinarySubrangesAndFirstLastMatches(int optimization)
    {
        List<(byte[] Data, byte[] Pattern)> cases = [([], []), ([], [65]), ([65], []), ([65], [65, 66]),
            ("abABaB"u8.ToArray(), "Ab"u8.ToArray()), ("prefix-tail-END"u8.ToArray(), "end"u8.ToArray()),
            ([120, 65, 0, 98, 89, 97, 0, 66], [97, 0, 66]), ([0, 0, 0], [0, 0]),
            ([192], [224]), ([192, 224, 192], [192]), ([128, 65, 255, 97, 128], [128, 97, 255]),
            ([195, 132], [195, 164]), ("aaaaab"u8.ToArray(), "AAAb"u8.ToArray()),
            ("baaaab"u8.ToArray(), "AAAc"u8.ToArray()), ("AAAaaa"u8.ToArray(), "aAa"u8.ToArray()),
            ([255, 128, 0, 65], [255, 128, 0, 97]), ([97, 98], [98, 165])];
        var random = new Random(829151);
        for (int row = 0; row < 24; row++)
        {
            byte[] data = new byte[random.Next(0, 41)]; random.NextBytes(data);
            byte[] pattern;
            if (row % 3 == 0 && data.Length != 0)
            {
                int start = random.Next(data.Length);
                pattern = data.Skip(start).Take(random.Next(0, data.Length - start + 1))
                    .Select(value => value is >= 65 and <= 90 or >= 97 and <= 122 ? (byte)(value ^ 32) : value).ToArray();
            }
            else { pattern = new byte[random.Next(0, 15)]; random.NextBytes(pattern); }
            cases.Add((data, pattern));
        }
        var packet = new List<byte>();
        foreach (var row in cases)
        {
            byte[] data = row.Data.Select(Fold).ToArray(), pattern = row.Pattern.Select(Fold).ToArray();
            int first = ((ReadOnlySpan<byte>)data).IndexOf((ReadOnlySpan<byte>)pattern);
            int last = ((ReadOnlySpan<byte>)data).LastIndexOf((ReadOnlySpan<byte>)pattern);
            if (first < 0) first = data.Length; if (last < 0) last = data.Length;
            packet.AddRange([(byte)data.Length, (byte)pattern.Length, (byte)first, (byte)last]);
            packet.AddRange(row.Data); packet.AddRange(row.Pattern);
        }
        string hex = Convert.ToHexString(packet.ToArray());
        Run($$"""
            using Standard::Text;
            int Digit(char value) nothrow { if (value <= '9') return value - '0'; return value - 'A' + 10; }
            int ReadByte(readonly(char)* data, ref nuint at) nothrow {
                int value = (Digit(data[at]) << 4) | Digit(data[at + 1]); at += 2; return value;
            }
            int main() nothrow {
                readonly(char)* packet = "{{hex}}"; nuint at = 0;
                for (int row = 0; row < {{cases.Count}}; row++) {
                    nuint length = (nuint)ReadByte(packet, ref at); nuint count = (nuint)ReadByte(packet, ref at);
                    nuint first = (nuint)ReadByte(packet, ref at); nuint last = (nuint)ReadByte(packet, ref at);
                    char[64] data; char[64] pattern; char[64] savedData; char[64] savedPattern;
                    for (nuint i = 0; i < 64; i++) { data[i] = (char)165; pattern[i] = (char)109; }
                    for (nuint i = 0; i < length; i++) data[i + 3] = (char)ReadByte(packet, ref at);
                    for (nuint i = 0; i < count; i++) pattern[i + 2] = (char)ReadByte(packet, ref at);
                    for (nuint i = 0; i < 64; i++) { savedData[i] = data[i]; savedPattern[i] = pattern[i]; }
                    if (Ascii::IndexOfIgnoreCase(&data[3], length, &pattern[2], count) != first) return 1;
                    if (Ascii::LastIndexOfIgnoreCase(&data[3], length, &pattern[2], count) != last) return 2;
                    for (nuint i = 0; i < 64; i++) if (data[i] != savedData[i] || pattern[i] != savedPattern[i]) return 3;
                }
                if (at != {{hex.Length}}) return 4;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PreflightsReadNoPointersAndEveryByteOverlapAndCountBoundaryKeepsItsContract(int optimization)
    {
        string toggled = string.Join(',', Enumerable.Range(0, 256)
            .Select(value => Fold((byte)value) == Fold((byte)(value ^ 32)) ? "(byte)0" : "(byte)1"));
        Run($$"""
            using Standard::Text;
            int main() nothrow {
                readonly(char)* nowhere = ((nuint)1).reinterpret<readonly(char)*>(); nuint maximum = (nuint)0 - (nuint)1;
                if (Ascii::IndexOfIgnoreCase(nowhere, 0, nowhere, 0) != 0 || Ascii::LastIndexOfIgnoreCase(nowhere, 0, nowhere, 0) != 0) return 1;
                if (Ascii::IndexOfIgnoreCase(nowhere, maximum, nowhere, 0) != 0 || Ascii::LastIndexOfIgnoreCase(nowhere, maximum, nowhere, 0) != maximum) return 2;
                if (Ascii::IndexOfIgnoreCase(nowhere, 0, nowhere, maximum) != 0 || Ascii::LastIndexOfIgnoreCase(nowhere, 0, nowhere, maximum) != 0) return 3;
                if (Ascii::IndexOfIgnoreCase(nowhere, 1, nowhere, maximum) != 1 || Ascii::LastIndexOfIgnoreCase(nowhere, 1, nowhere, maximum) != 1) return 4;
                if (Ascii::IndexOfIgnoreCase(nowhere, maximum - 1, nowhere, maximum) != maximum - 1 || Ascii::LastIndexOfIgnoreCase(nowhere, maximum - 1, nowhere, maximum) != maximum - 1) return 5;
                byte[256] toggled = [{{toggled}}];
                for (nuint i = 0; i < 256; i++) {
                    char value = (char)i; char pattern = (char)((uint)i ^ 32u); char savedPatternByte = pattern;
                    if (Ascii::IndexOfIgnoreCase(&value, 1, &value, 1) != 0 || Ascii::LastIndexOfIgnoreCase(&value, 1, &value, 1) != 0) return 6;
                    if (Ascii::IndexOfIgnoreCase(&value, 1, &pattern, 1) != (nuint)toggled[i] || Ascii::LastIndexOfIgnoreCase(&value, 1, &pattern, 1) != (nuint)toggled[i]) return 7;
                    if (value != (char)i || pattern != savedPatternByte) return 8;
                }
                char[12] data = ['x', 'A', 'b', 'a', 'B', 'a', 'B', (char)0, 'A', 'b', 'x', 'x'];
                char[12] saved = ['x', 'A', 'b', 'a', 'B', 'a', 'B', (char)0, 'A', 'b', 'x', 'x'];
                if (Ascii::IndexOfIgnoreCase(&data[1], 9, &data[3], 2) != 0 || Ascii::LastIndexOfIgnoreCase(&data[1], 9, &data[3], 2) != 7) return 9;
                if (Ascii::IndexOfIgnoreCase(&data[1], 9, &data[1], 9) != 0 || Ascii::LastIndexOfIgnoreCase(&data[1], 9, &data[1], 9) != 0) return 10;
                if (Ascii::IndexOfIgnoreCase(&data[1], 9, &data[6], 3) != 5 || Ascii::LastIndexOfIgnoreCase(&data[1], 9, &data[6], 3) != 5) return 11;
                if (Ascii::IndexOfIgnoreCase(&data[1], 9, &data[1], 10) != 9 || Ascii::LastIndexOfIgnoreCase(&data[1], 9, &data[1], 10) != 9) return 12;
                for (nuint i = 0; i < 12; i++) if (data[i] != saved[i]) return 13;
                return 0;
            }
            """, optimization);
    }

    private static byte Fold(byte value) => value is >= 97 and <= 122 ? (byte)(value - 32) : value;

    private static void Run(string program, int optimization)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Standard", "core", "Text", "ascii.gf");
        string ir = Compiler.Emit([new SourceFile(path, File.ReadAllText(path)), new SourceFile("ascii-substring-search.gf", program)],
            options: new() { NoStdLib = true, NoBuiltins = true, InternalizeFunctions = true });
        foreach (string forbidden in new[] { "invoke ", "personality", "__gflat_eh_", "gflat$Memory$", "@malloc", "@free", "__gflat_gc" })
            Assert.DoesNotContain(forbidden, ir);
        string[] definitions = ir.Split('\n').Where(line => line.StartsWith("define ") &&
            (line.Contains("$IndexOfIgnoreCase(") || line.Contains("$LastIndexOfIgnoreCase("))).ToArray();
        Assert.Equal(2, definitions.Length);
        Assert.All(definitions, line => Assert.Contains("nounwind", line));
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }
}
