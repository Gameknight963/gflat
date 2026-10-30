namespace gflat.Tests.Std.Text;

public sealed class ByteSearchAnyExceptTests
{
    private static (int First, int Last) Oracle(byte[] data, int start, int length,
        byte[] pattern, int patternStart, int count)
    {
        int first = length, last = length;
        for (int index = 0; index < length; index++)
        {
            bool member = false;
            for (int item = 0; item < count; item++)
                if (data[start + index] == pattern[patternStart + item]) { member = true; break; }
            if (!member) { if (first == length) first = index; last = index; }
        }
        ReadOnlySpan<byte> input = data.AsSpan(start, length);
        ReadOnlySpan<byte> set = pattern.AsSpan(patternStart, count);
        int netFirst = input.IndexOfAnyExcept(set), netLast = input.LastIndexOfAnyExcept(set);
        Assert.Equal(first, netFirst < 0 ? length : netFirst);
        Assert.Equal(last, netLast < 0 ? length : netLast);
        return (first, last);
    }

    private static string Bytes(IEnumerable<byte> values)
        => string.Join(',', values.Select(value => "(byte)" + value));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryByteAndDuplicateUnsortedSetsMatchNaiveAndDotNetOracles(int optimization)
    {
        byte[] input = Enumerable.Range(0, 256).Concat(Enumerable.Range(0, 256).Reverse()).Select(value => (byte)value).ToArray();
        var rows = new List<int>();
        for (int value = 0; value < 256; value++)
        {
            byte[] pattern = [(byte)value, (byte)value, (byte)(value ^ 255)];
            var one = Oracle(input, 0, input.Length, pattern, 0, 1);
            var many = Oracle(input, 0, input.Length, pattern, 0, 3);
            rows.AddRange([one.First, one.Last, many.First, many.Last]);
        }
        Run($$"""
            using Standard::Text;
            int main() nothrow {
                byte[514] input; byte[3] pattern;
                input[0] = (byte)90; input[513] = (byte)165;
                for (nuint i = 0; i < 256; i++) { input[i + 1] = (byte)i; input[i + 257] = (byte)(255 - i); }
                int[1024] rows = [{{string.Join(',', rows)}}];
                for (int value = 0; value < 256; value++) {
                    pattern[0] = (byte)value; pattern[1] = (byte)value; pattern[2] = (byte)(value ^ 255);
                    nuint row = (nuint)value * 4;
                    for (nuint count = 1; count <= 2; count++) {
                        if (ByteSearch::IndexOfAnyExcept(&input[1], 512, pattern, count) != (nuint)rows[row]) return 1;
                        if (ByteSearch::LastIndexOfAnyExcept(&input[1], 512, pattern, count) != (nuint)rows[row + 1]) return 2;
                    }
                    if (ByteSearch::IndexOfAnyExcept(&input[1], 512, pattern, 3) != (nuint)rows[row + 2]) return 3;
                    if (ByteSearch::LastIndexOfAnyExcept(&input[1], 512, pattern, 3) != (nuint)rows[row + 3]) return 4;
                    if (pattern[0] != (byte)value || pattern[1] != (byte)value || pattern[2] != (byte)(value ^ 255)) return 5;
                    if (ByteSearch::IndexOfAnyExcept(&input[(nuint)value + 1], 1, pattern, 1) != 1 || ByteSearch::LastIndexOfAnyExcept(&input[(nuint)value + 1], 1, pattern, 1) != 1) return 8;
                    if (ByteSearch::IndexOfAnyExcept(&input[(nuint)value + 1], 1, &pattern[2], 1) != 0 || ByteSearch::LastIndexOfAnyExcept(&input[(nuint)value + 1], 1, &pattern[2], 1) != 0) return 9;
                }
                for (nuint i = 0; i < 256; i++) if (input[i + 1] != (byte)i || input[i + 257] != (byte)(255 - i)) return 6;
                if (input[0] != (byte)90 || input[513] != (byte)165) return 7;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SeededCountedSlicesAndOverlappingViewsPreserveEveryInputByte(int optimization)
    {
        var random = new Random(586224);
        byte[] input = new byte[256], pattern = new byte[320];
        random.NextBytes(input); random.NextBytes(pattern);
        var disjoint = new List<int>(); var overlap = new List<int>();
        for (int trial = 0; trial < 32; trial++)
        {
            int start = random.Next(input.Length + 1), length = random.Next(input.Length - start + 1);
            int setStart = random.Next(pattern.Length + 1), count = random.Next(pattern.Length - setStart + 1);
            var result = Oracle(input, start, length, pattern, setStart, count);
            disjoint.AddRange([start, length, setStart, count, result.First, result.Last]);
        }
        for (int trial = 0; trial < 24; trial++)
        {
            int start = random.Next(input.Length + 1), length = random.Next(input.Length - start + 1);
            int setStart = trial % 4 == 0 ? start : random.Next(input.Length + 1);
            int count = trial % 4 == 0 ? length : random.Next(input.Length - setStart + 1);
            var result = Oracle(input, start, length, input, setStart, count);
            overlap.AddRange([start, length, setStart, count, result.First, result.Last]);
        }
        Run($$"""
            using Standard::Text;
            int main() nothrow {
                byte[256] input = [{{Bytes(input)}}]; byte[320] pattern = [{{Bytes(pattern)}}];
                byte[256] inputBefore; byte[320] patternBefore;
                for (nuint i = 0; i < 256; i++) inputBefore[i] = input[i];
                for (nuint i = 0; i < 320; i++) patternBefore[i] = pattern[i];
                int[192] disjoint = [{{string.Join(',', disjoint)}}]; int[144] overlap = [{{string.Join(',', overlap)}}];
                for (nuint trial = 0; trial < 32; trial++) {
                    nuint row = trial * 6;
                    readonly(byte)* data = (byte*)input + disjoint[row]; readonly(byte)* set = (byte*)pattern + disjoint[row + 2];
                    nuint length = (nuint)disjoint[row + 1]; nuint count = (nuint)disjoint[row + 3];
                    if (ByteSearch::IndexOfAnyExcept(data, length, set, count) != (nuint)disjoint[row + 4]) return 1;
                    if (ByteSearch::LastIndexOfAnyExcept(data, length, set, count) != (nuint)disjoint[row + 5]) return 2;
                }
                for (nuint trial = 0; trial < 24; trial++) {
                    nuint row = trial * 6;
                    readonly(byte)* data = (byte*)input + overlap[row]; readonly(byte)* set = (byte*)input + overlap[row + 2];
                    nuint length = (nuint)overlap[row + 1]; nuint count = (nuint)overlap[row + 3];
                    if (ByteSearch::IndexOfAnyExcept(data, length, set, count) != (nuint)overlap[row + 4]) return 3;
                    if (ByteSearch::LastIndexOfAnyExcept(data, length, set, count) != (nuint)overlap[row + 5]) return 4;
                }
                for (nuint i = 0; i < 256; i++) if (input[i] != inputBefore[i]) return 5;
                for (nuint i = 0; i < 320; i++) if (pattern[i] != patternBefore[i]) return 6;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EmptySidesAll256ValuesAndGuardedExtentsHaveExactSentinels(int optimization)
        => Run("""
            using Standard::Text;
            int main() nothrow {
                readonly(byte)* invalid = ((nuint)1).reinterpret<readonly(byte)*>(); nuint maximum = (nuint)0 - (nuint)1;
                if (ByteSearch::IndexOfAnyExcept(invalid, 0, invalid, maximum) != 0) return 1;
                if (ByteSearch::LastIndexOfAnyExcept(invalid, 0, invalid, maximum) != 0) return 2;
                if (ByteSearch::IndexOfAnyExcept(invalid, maximum, invalid, 0) != 0) return 3;
                if (ByteSearch::LastIndexOfAnyExcept(invalid, maximum, invalid, 0) != maximum - 1) return 4;
                if (ByteSearch::IndexOfAnyExcept(invalid, 0, invalid, 0) != 0 || ByteSearch::LastIndexOfAnyExcept(invalid, 0, invalid, 0) != 0) return 5;
                if (ByteSearch::IndexOfAnyExcept(invalid, 1, invalid, 0) != 0 || ByteSearch::LastIndexOfAnyExcept(invalid, 1, invalid, 0) != 0) return 6;
                if (ByteSearch::IndexOfAny(invalid, maximum, invalid, 0) != maximum || ByteSearch::LastIndexOfAny(invalid, maximum, invalid, 0) != maximum) return 7;
                byte[5] input = [(byte)0, (byte)17, (byte)0, (byte)17, (byte)0];
                byte[3] pattern = [(byte)0, (byte)17, (byte)0];
                if (ByteSearch::IndexOfAnyExcept(&input[1], 3, &pattern[1], 1) != 1) return 8;
                if (ByteSearch::LastIndexOfAnyExcept(&input[1], 3, &pattern[1], 1) != 1) return 9;
                if (ByteSearch::IndexOfAnyExcept(&input[1], 1, &pattern[1], 1) != 1 || ByteSearch::LastIndexOfAnyExcept(&input[1], 1, &pattern[1], 1) != 1) return 10;
                byte[260] all; for (nuint i = 0; i < 260; i++) all[i] = (byte)i;
                if (ByteSearch::IndexOfAnyExcept(input, 5, all, 260) != 5 || ByteSearch::LastIndexOfAnyExcept(input, 5, all, 260) != 5) return 11;
                if (ByteSearch::IndexOfAnyExcept(all, 260, all, 256) != 260 || ByteSearch::LastIndexOfAnyExcept(all, 260, all, 256) != 260) return 12;
                byte[4] zeros = [(byte)128, (byte)0, (byte)255, (byte)0]; byte[3] set = [(byte)0, (byte)255, (byte)0];
                if (ByteSearch::IndexOfAnyExcept(zeros, 4, set, 3) != 0 || ByteSearch::LastIndexOfAnyExcept(zeros, 4, set, 3) != 0) return 13;
                byte[5] expected = [(byte)0, (byte)17, (byte)0, (byte)17, (byte)0];
                for (nuint i = 0; i < 5; i++) if (input[i] != expected[i]) return 14;
                for (nuint i = 0; i < 260; i++) if (all[i] != (byte)i) return 15;
                if (pattern[0] != (byte)0 || pattern[1] != (byte)17 || pattern[2] != (byte)0) return 16;
                return 0;
            }
            """, optimization);

    private static void Run(string program, int optimization)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Standard", "core", "Text", "ByteSearch.gf");
        string ir = Compiler.Emit([new SourceFile(path, File.ReadAllText(path)), new SourceFile("byte-search-any-except.gf", program)],
            options: new() { NoStdLib = true, NoBuiltins = true, InternalizeFunctions = true });
        foreach (string forbidden in new[] { "invoke ", "personality", "__gflat_eh_", "gflat$Memory$", "@malloc", "@free", "__gflat_gc" })
            Assert.DoesNotContain(forbidden, ir);
        string[] methods = ir.Split('\n').Where(line => line.StartsWith("define ") &&
            (line.Contains("$IndexOfAnyExcept(") || line.Contains("$LastIndexOfAnyExcept("))).ToArray();
        Assert.Equal(2, methods.Length);
        Assert.All(methods, line => Assert.Contains("nounwind", line));
        var result = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
        Assert.True(result.ExitCode == 0, $"Byte any-except exit {result.ExitCode}: {result.StandardError}\nProgram:\n{program}");
    }
}
