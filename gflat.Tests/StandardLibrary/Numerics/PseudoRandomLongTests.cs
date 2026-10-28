using System.Numerics;
using System.Text;

namespace gflat.Tests.Std.Numerics;

public sealed class PseudoRandomLongTests
{
    private const string Reader = """
        using std; using std::Text; using std::Numerics;
        int Digit(char value) nothrow {
            if (value <= '9') return value - '0';
            return value - 'A' + 10;
        }
        int ReadByte(readonly(char)* packet, ref nuint position) nothrow {
            int value = (Digit(packet[position]) << 4) | Digit(packet[position + 1]);
            position += 2;
            return value;
        }
        ulong ReadWord(readonly(char)* packet, ref nuint position) nothrow {
            ulong value = 0ul;
            for (int shift = 0; shift < 64; shift += 8)
                value = value | ((ulong)ReadByte(packet, ref position) << shift);
            return value;
        }
        """;

    private const string DeniedAllocator = """
        int attempts = 0;
        namespace Memory {
            public replace void*? Allocate(nuint count) { attempts++; return null; }
            public replace void Free(void* storage) nothrow {}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FullSignedDrawsUseAllBitsAndConsumeExactlyOneWord(int optimization)
    {
        ulong[] outputs = [0, 1, 0x7fffffffffffffff, 0x8000000000000000,
            ulong.MaxValue, 0x00000000ffffffff, 0xffffffff00000000];
        var seeds = new List<ulong> { 0, 42, ulong.MaxValue };
        foreach (ulong output in outputs)
        {
            ulong seed = SeedForOutput(output), state = seed;
            Assert.Equal(output, NextWord(ref state));
            seeds.Add(seed);
        }
        const int draws = 32;
        using var packet = new MemoryStream();
        using (var writer = new BinaryWriter(packet, Encoding.UTF8, leaveOpen: true))
        {
            foreach (ulong seed in seeds)
            {
                writer.Write(seed);
                ulong state = seed;
                for (int draw = 0; draw < draws; draw++)
                    writer.Write(unchecked((long)NextWord(ref state)));
                writer.Write(NextWord(ref state));
            }
        }
        string hex = Convert.ToHexString(packet.ToArray());
        Run(Reader + DeniedAllocator + $$$"""
            long Observe(PseudoRandom* random) nothrow => random.NextLong();
            int main() {
                readonly(char)* packet = "{{{hex}}}";
                nuint position = 0;
                for (int row = 0; row < {{{seeds.Count}}}; row++) {
                    PseudoRandom random = new(ReadWord(packet, ref position));
                    for (int draw = 0; draw < {{{draws}}}; draw++) {
                        long expected = (long)ReadWord(packet, ref position);
                        if (Observe(&random) != expected) return 1;
                    }
                    if (random.NextULong() != ReadWord(packet, ref position)) return 2;
                }
                if (position != {{{hex.Length}}} || attempts != 0) return 3;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SignedRangesMatchBigIntegerAndForcedRejectionStateOracles(int optimization)
    {
        (long Min, long Max)[] ranges = [
            (long.MinValue, long.MinValue + 1), (long.MaxValue - 1, long.MaxValue),
            (long.MinValue, long.MaxValue), (long.MinValue, 1), (long.MinValue, 0),
            (-1, long.MaxValue), (-7, long.MaxValue), (-10, -1), (-25, 25), (1, 10), (0, 1)
        ];
        var cases = new List<(bool UpperOnly, ulong Seed, long Min, long Max)>();
        foreach (var range in ranges)
            foreach (ulong seed in new[] { 0UL, 42UL, ulong.MaxValue })
                cases.Add((false, seed, range.Min, range.Max));
        foreach (long bound in new[] { 1L, 3L, 5000000000L, long.MaxValue })
            foreach (ulong seed in new[] { 0UL, 42UL, ulong.MaxValue })
                cases.Add((true, seed, 0, bound));

        int forcedRejections = 0;
        foreach (var range in new[] { (Min: long.MinValue, Max: 1L), (Min: long.MinValue, Max: long.MaxValue) })
        {
            BigInteger width = (BigInteger)range.Max - range.Min;
            ulong threshold = (ulong)((BigInteger.One << 64) % width);
            Assert.True(threshold > 0);
            foreach (ulong first in new[] { 0UL, threshold - 1, threshold, ulong.MaxValue }.Distinct())
            {
                ulong seed = SeedForOutput(first), firstState = seed;
                Assert.Equal(first, NextWord(ref firstState));
                ulong state = seed;
                _ = Bounded(ref state, range.Min, range.Max, out int draws);
                if (first < threshold)
                {
                    Assert.True(draws > 1);
                    forcedRejections++;
                }
                else Assert.Equal(1, draws);
                cases.Add((false, seed, range.Min, range.Max));
            }
        }
        Assert.True(forcedRejections >= 3);

        var random = new Random(193751);
        for (int sample = 0; sample < 32; sample++)
        {
            long first = random.NextInt64(long.MinValue, long.MaxValue);
            long second = random.NextInt64(long.MinValue, long.MaxValue);
            if (first == second) continue;
            byte[] seedBytes = new byte[8];
            random.NextBytes(seedBytes);
            cases.Add((false, BitConverter.ToUInt64(seedBytes), Math.Min(first, second), Math.Max(first, second)));
        }

        using var packet = new MemoryStream();
        using (var writer = new BinaryWriter(packet, Encoding.UTF8, leaveOpen: true))
        {
            foreach (var row in cases)
            {
                ulong state = row.Seed;
                long expected = Bounded(ref state, row.Min, row.Max, out _);
                writer.Write((byte)(row.UpperOnly ? 1 : 0));
                writer.Write(row.Seed);
                writer.Write(row.Min);
                writer.Write(row.Max);
                writer.Write(expected);
                writer.Write(NextWord(ref state));
            }
        }
        string hex = Convert.ToHexString(packet.ToArray());
        Run(Reader + DeniedAllocator + $$$"""
            int main() {
                readonly(char)* packet = "{{{hex}}}";
                nuint position = 0;
                for (int row = 0; row < {{{cases.Count}}}; row++) {
                    bool upperOnly = ReadByte(packet, ref position) != 0;
                    PseudoRandom random = new(ReadWord(packet, ref position));
                    long minimum = (long)ReadWord(packet, ref position);
                    long maximum = (long)ReadWord(packet, ref position);
                    long expected = (long)ReadWord(packet, ref position);
                    long actual = 0l;
                    if (upperOnly) actual = random.NextLong(maximum);
                    else actual = random.NextLong(minimum, maximum);
                    if (actual != expected || actual < minimum || actual >= maximum) return 1;
                    if (random.NextULong() != ReadWord(packet, ref position)) return 2;
                }
                if (position != {{{hex.Length}}} || attempts != 0) return 3;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InvalidRangesPreserveTargetsAndStateAndCopiesRemainIndependent(int optimization)
    {
        Run("""
            using std; using std::Text; using std::Numerics;
            int main() {
                PseudoRandom random = new(42ul);
                PseudoRandom copy = random;
                long minimum = -9223372036854775807l - 1l;
                long maximum = 9223372036854775807l;
                for (int operation = 0; operation < 8; operation++) {
                    long result = 99l;
                    bool caught = false;
                    try {
                        if (operation == 0) result = random.NextLong(0l);
                        else if (operation == 1) result = random.NextLong(-1l);
                        else if (operation == 2) result = random.NextLong(minimum);
                        else if (operation == 3) result = random.NextLong(5l, 5l);
                        else if (operation == 4) result = random.NextLong(5l, -5l);
                        else if (operation == 5) result = random.NextLong(minimum, minimum);
                        else if (operation == 6) result = random.NextLong(maximum, maximum);
                        else result = random.NextLong(maximum, minimum);
                    }
                    catch (ref Exception error) {
                        if (!CString::Equals(error.GetMessage(), "Random range must not be empty")) return 1;
                        caught = true;
                    }
                    if (!caught || result != 99l || random.NextULong() != copy.NextULong()) return 2;
                }
                for (int draw = 0; draw < 100; draw++)
                    if (random.NextLong() != copy.NextLong()) return 3;
                random.NextLong();
                if (random.NextULong() == copy.NextULong()) return 4;
                return 0;
            }
            """, optimization);
    }

    private static long Bounded(ref ulong state, long minimum, long maximum, out int draws)
    {
        BigInteger width = (BigInteger)maximum - minimum;
        Assert.True(width > 0 && width < (BigInteger.One << 64));
        BigInteger threshold = (BigInteger.One << 64) % width;
        ulong word;
        draws = 0;
        do
        {
            word = NextWord(ref state);
            draws++;
        } while ((BigInteger)word < threshold);
        BigInteger result = minimum + (BigInteger)word % width;
        Assert.True(result >= minimum && result < maximum);
        return (long)result;
    }

    private static ulong NextWord(ref ulong state)
    {
        state = unchecked(state + 0x9e3779b97f4a7c15UL);
        ulong value = state;
        value = unchecked((value ^ (value >> 30)) * 0xbf58476d1ce4e5b9UL);
        value = unchecked((value ^ (value >> 27)) * 0x94d049bb133111ebUL);
        return value ^ (value >> 31);
    }

    private static ulong SeedForOutput(ulong output)
    {
        static ulong UndoShift(ulong value, int shift)
        {
            ulong result = value;
            for (int offset = shift; offset < 64; offset += shift) result ^= value >> offset;
            return result;
        }
        static ulong Inverse(ulong odd)
        {
            ulong result = 1;
            for (int step = 0; step < 6; step++) result = unchecked(result * (2UL - odd * result));
            return result;
        }
        ulong state = UndoShift(output, 31);
        state = unchecked(state * Inverse(0x94d049bb133111ebUL));
        state = UndoShift(state, 27);
        state = unchecked(state * Inverse(0xbf58476d1ce4e5b9UL));
        return unchecked(UndoShift(state, 30) - 0x9e3779b97f4a7c15UL);
    }

    private static void Run(string program, int optimization)
    {
        var source = new SourceFile("PseudoRandom.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core/Numerics/PseudoRandom.gf")));
        var strings = new SourceFile("CString.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core/Text/CString.gf")));
        var result = CompilerTestHelper.Run([source, strings, new SourceFile("pseudo-random-long.gf", program)], optimization);
        Assert.True(result.ExitCode == 0, $"NextLong exit {result.ExitCode}: {result.StandardError}");
    }
}
