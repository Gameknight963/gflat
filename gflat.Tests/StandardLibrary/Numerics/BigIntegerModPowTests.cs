using System.Globalization;
using System.Numerics;

namespace gflat.Tests.Std.Numerics;

public sealed class BigIntegerModPowTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SignedArbitraryExponentsMatchDotNetWithoutMutatingBorrowedOperands(int optimization)
    {
        List<(BigInteger Value, BigInteger Exponent, BigInteger Modulus)> cases =
        [
            (0, 0, 7), (0, 9, -7), (4, 13, 497), (-4, 13, 497), (-4, 12, -497),
            (7, 0, 1), (-7, 0, -1), (1, BigInteger.One << 96, 97),
            (-1, (BigInteger.One << 96) + 1, -97), (19, 17, 19),
            (4, (BigInteger.One << 96) + 1, 497), (-4, BigInteger.One << 96, -497),
            (999999999, 3, 1000000000), (1000000001, 4, -999999999)
        ];
        var random = new Random(138729);
        for (int i = 0; i < 24; i++)
        {
            BigInteger value = BigInteger.Pow(10, 25 + i % 5) + random.Next(1, 100000);
            BigInteger exponent = i < 8 ? i : (BigInteger.One << (33 + i)) + random.Next(1, 32);
            BigInteger modulus = BigInteger.Pow(10, 12 + i % 7) + random.Next(1, 1000);
            if (i % 2 != 0) value = -value;
            if (i % 3 == 0) modulus = -modulus;
            cases.Add((value, exponent, modulus));
        }
        string packet = string.Concat(cases.Select(row => string.Join("|", new[] {
            row.Value, row.Exponent, row.Modulus, BigInteger.ModPow(row.Value, row.Exponent, row.Modulus)
        }.Select(value => value.ToString(CultureInfo.InvariantCulture))) + "|"));
        Run(Counters + $$$"""
            BigInteger ReadInteger(readonly(char)* packet, ref nuint at) {
                nuint start = at;
                while (packet[at] != '|') at++;
                BigInteger value = BigInteger::Parse(packet + start, at - start);
                at++; return move value;
            }
            int main() {
                {
                    readonly(char)* packet = "{{{packet}}}"; nuint at = 0;
                    for (int row = 0; row < {{{cases.Count}}}; row++) {
                        BigInteger value = ReadInteger(packet, ref at);
                        BigInteger exponent = ReadInteger(packet, ref at);
                        BigInteger modulus = ReadInteger(packet, ref at);
                        BigInteger expected = ReadInteger(packet, ref at);
                        BigInteger savedValue = value; BigInteger savedExponent = exponent; BigInteger savedModulus = modulus;
                        immutable(BigInteger)* view = &value;
                        BigInteger actual = view.ModPow(&exponent, &modulus);
                        if (!actual.Equals(&expected) || !value.Equals(&savedValue) || !exponent.Equals(&savedExponent) || !modulus.Equals(&savedModulus)) return 1;
                        long small = 0l;
                        if (exponent.TryToLong(ref small) && small <= 13l) {
                            BigInteger power = value.Pow((uint)small);
                            BigInteger remainder = power.Remainder(&modulus);
                            if (!actual.Equals(&remainder)) return 2;
                        }
                        value = value.ModPow(&exponent, &modulus);
                        if (!value.Equals(&expected) || !exponent.Equals(&savedExponent) || !modulus.Equals(&savedModulus)) return 3;
                    }
                    if (at != {{{packet.Length}}}) return 4;
                }
                if (allocated != released) return 5;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AliasedOperandsAndEveryMeasuredAllocationFailurePreservePriorOwners(int optimization)
    {
        // Owners 0..3 initially contain base=17, exponent=13, modulus=19, output=333.
        (int A, int E, int M, int Output)[] modes =
            [(0, 1, 2, 3), (0, 0, 2, 3), (0, 1, 0, 3), (0, 2, 2, 3), (0, 0, 0, 3), (0, 1, 2, 0), (0, 1, 2, 1), (0, 1, 2, 2)];
        BigInteger[] initial = [17, 13, 19, 333];
        string indices = string.Join(",", modes.SelectMany(mode => new[] { mode.A, mode.E, mode.M, mode.Output }));
        string expected = string.Join(",", modes.Select(mode => BigInteger.ModPow(initial[mode.A], initial[mode.E], initial[mode.M]).ToString(CultureInfo.InvariantCulture) + "l"));
        Run(Counters + $$$"""
            int main() {
                int[32] indices = [{{{indices}}}]; long[8] expected = [{{{expected}}}];
                for (nuint mode = 0; mode < 8; mode++) {
                    int points = 0;
                    {
                        BigInteger a = new(17l); BigInteger e = new(13l); BigInteger m = new(19l); BigInteger output = new(333l);
                        BigInteger*[4] owners = [&a, &e, &m, &output];
                        int before = attempts;
                        *owners[(nuint)indices[mode * 4 + 3]] = owners[(nuint)indices[mode * 4]].ModPow(owners[(nuint)indices[mode * 4 + 1]], owners[(nuint)indices[mode * 4 + 2]]);
                        points = attempts - before;
                        for (nuint i = 0; i < 4; i++) {
                            long wanted = 333l; if (i == 0) wanted = 17l; else if (i == 1) wanted = 13l; else if (i == 2) wanted = 19l;
                            if (i == (nuint)indices[mode * 4 + 3]) wanted = expected[mode];
                            long actual = 0l; if (!owners[i].TryToLong(ref actual) || actual != wanted) return 1;
                        }
                    }
                    if (allocated != released) return 2;
                    for (int point = 0; point < points; point++) {
                        {
                            capture = true; BigInteger a = new(17l); capture = false; watch0 = captured;
                            capture = true; BigInteger e = new(13l); capture = false; watch1 = captured;
                            capture = true; BigInteger m = new(19l); capture = false; watch2 = captured;
                            capture = true; BigInteger output = new(333l); capture = false; watch3 = captured;
                            BigInteger*[4] owners = [&a, &e, &m, &output];
                            int balance = allocated - released; int oldDenied = denied; watchedFrees = 0; failAfter = point; bool caught = false;
                            try {
                                *owners[(nuint)indices[mode * 4 + 3]] = owners[(nuint)indices[mode * 4]].ModPow(owners[(nuint)indices[mode * 4 + 1]], owners[(nuint)indices[mode * 4 + 2]]);
                            }
                            catch (ref BadAllocException error) { caught = true; }
                            failAfter = -1;
                            if (!caught || denied != oldDenied + 1 || allocated - released != balance || watchedFrees != 0) return 3;
                            for (nuint i = 0; i < 4; i++) {
                                long wanted = 333l; if (i == 0) wanted = 17l; else if (i == 1) wanted = 13l; else if (i == 2) wanted = 19l;
                                long actual = 0l; if (!owners[i].TryToLong(ref actual) || actual != wanted) return 4;
                            }
                            watch0 = null; watch1 = null; watch2 = null; watch3 = null;
                        }
                        if (allocated != released) return 5;
                    }
                }
                {
                    BigInteger value = new(17l); BigInteger exponent = new(-1l); BigInteger zero = new(); BigInteger validModulus = new(19l); BigInteger target = new(333l);
                    int before = allocated; try { throw new Exception("control"); } catch { }
                    int exceptionAllocations = allocated - before;
                    for (int mode = 0; mode < 3; mode++) {
                        before = allocated; bool caught = false;
                        immutable(BigInteger)* modulus = &zero;
                        if (mode == 0) modulus = &validModulus;
                        try { target = value.ModPow(&exponent, modulus); }
                        catch (ref Exception error) {
                            if (mode < 2 && !CString::Equals(error.GetMessage(), "BigInteger exponent is negative")) return 6;
                            if (mode == 2 && !CString::Equals(error.GetMessage(), "BigInteger divisor is zero")) return 7;
                            caught = true;
                        }
                        long actual = 0l;
                        if (!caught || allocated - before != exceptionAllocations || !target.TryToLong(ref actual) || actual != 333l) return 8;
                        if (mode == 1) exponent = new BigInteger(3l);
                    }
                }
                if (allocated != released) return 9;
                return 0;
            }
            """, optimization);
    }

    private const string Counters = """
        using Standard; using Standard::Text; using Standard::Numerics;
        extern void*? malloc(nuint count); extern void free(void* value);
        int allocated = 0; int released = 0; int attempts = 0; int denied = 0; int failAfter = -1;
        bool capture = false; void*? captured = null;
        void*? watch0 = null; void*? watch1 = null; void*? watch2 = null; void*? watch3 = null; int watchedFrees = 0;
        namespace Memory {
            public replace void*? Allocate(nuint count) {
                attempts++;
                if (failAfter == 0) { failAfter = -1; denied++; return null; }
                if (failAfter > 0) failAfter--;
                void*? value = malloc(count);
                if (value != null) { allocated++; if (capture) captured = value; }
                return value;
            }
            public replace void Free(void* value) nothrow {
                if (value == watch0 || value == watch1 || value == watch2 || value == watch3) watchedFrees++;
                released++; free(value);
            }
        }
        """;

    private static void Run(string code, int optimization)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Standard"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("bigint-modpow.gf", code));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }
}
