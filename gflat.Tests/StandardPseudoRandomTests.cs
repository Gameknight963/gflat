namespace gflat.Tests;

public class StandardPseudoRandomTests
{
    private static void Run(string code)
    {
        var random = new SourceFile("PseudoRandom.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core/PseudoRandom.gf")));
        Assert.Equal(0, CompilerTestHelper.Run([random, new SourceFile("main.gf", code)]).ExitCode);
    }

    [Fact]
    public void SeedZeroMatchesSplitMix64ReferenceVectors()
        => Run("""
            using std;
            int main() {
                PseudoRandom random = new PseudoRandom(0ul);
                if (random.NextULong() != 16294208416658607535ul) return 1;
                if (random.NextULong() != 0x6e789e6aa1b965f4ul) return 2;
                if (random.NextULong() != 0x06c45d188009454ful) return 3;
                if (random.NextULong() != 0xf88bb8a8724c81ecul) return 4;
                if (random.NextULong() != 0x1b39896a51a8749bul) return 5;
                if (random.NextULong() != 0x53cb9f0c747ea2eaul) return 6;
                return 0;
            }
            """);

    private static ulong Next(ref ulong state)
    {
        state = unchecked(state + 0x9e3779b97f4a7c15UL);
        ulong value = state;
        value = unchecked((value ^ (value >> 30)) * 0xbf58476d1ce4e5b9UL);
        value = unchecked((value ^ (value >> 27)) * 0x94d049bb133111ebUL);
        return value ^ (value >> 31);
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(3UL)]
    [InlineData(0x8000000000000001UL)]
    [InlineData(ulong.MaxValue)]
    public void BoundedValuesMatchRejectionSamplingReference(ulong bound)
    {
        ulong state = 0;
        ulong threshold = unchecked(0UL - bound) % bound;
        var expected = Enumerable.Range(0, 64).Select(_ =>
        {
            ulong value;
            do value = Next(ref state); while (value < threshold);
            return value % bound;
        }).ToArray();
        Run($$"""
            using std;
            int main() {
                ulong[64] expected = [{{string.Join(",", expected.Select(n => $"{n}ul"))}}];
                PseudoRandom random = new PseudoRandom(0ul);
                for (int i = 0; i < 64; i++) if (random.NextULong({{bound}}ul) != expected[i]) return 1;
                return 0;
            }
            """);
    }

    [Fact]
    public void CopiedGeneratorsProduceIndependentIdenticalStreams()
        => Run("""
            using std;
            int main() {
                PseudoRandom original = new PseudoRandom(18446744073709551615ul);
                original.NextULong();
                PseudoRandom copy = original;
                for (int i = 0; i < 1000; i++) if (original.NextULong() != copy.NextULong()) return 1;
                original.NextULong();
                if (original.NextULong() == copy.NextULong()) return 2;
                return 0;
            }
            """);

    [Fact]
    public void InvalidBoundsThrowWithoutAdvancingAndSignedRangesHandleExtremes()
        => Run("""
            using std;
            int main() {
                PseudoRandom random = new PseudoRandom(42ul); PseudoRandom copy = random;
                try { random.NextULong(0ul); return 1; } catch (ref Exception error) {}
                try { random.NextUInt(0u); return 2; } catch (ref Exception error) {}
                try { random.NextInt(0); return 3; } catch (ref Exception error) {}
                try { random.NextInt(-1); return 4; } catch (ref Exception error) {}
                try { random.NextInt(5, 5); return 5; } catch (ref Exception error) {}
                try { random.NextInt(5, -5); return 6; } catch (ref Exception error) {}
                if (random.NextULong() != copy.NextULong()) return 7;
                for (int i = 0; i < 10000; i++) {
                    if (random.NextInt(-2147483647 - 1, -2147483647) != -2147483647 - 1) return 8;
                    if (random.NextInt(2147483646, 2147483647) != 2147483646) return 9;
                    if (random.NextInt(-10, -1) >= -1) return 10;
                    if (random.NextUInt(3u) >= 3u) return 11;
                    if (random.NextInt(-2147483647 - 1, 2147483647) == 2147483647) return 12;
                }
                return 0;
            }
            """);

    [Fact]
    public void FillUsesLittleEndianWordsAndPreservesCallerBounds()
        => Run("""
            using std;
            int main() {
                PseudoRandom random = new PseudoRandom(0ul); PseudoRandom expected = random;
                char[19] bytes;
                for (int i = 0; i < 19; i++) bytes[i] = 'Z';
                random.Fill(&bytes[1], 17);
                if (bytes[0] != 'Z' || bytes[18] != 'Z') return 1;
                ulong word = 0ul;
                for (int i = 0; i < 17; i++) {
                    if (i % 8 == 0) word = expected.NextULong();
                    if ((byte)bytes[i + 1] != (byte)(word >> ((i % 8) * 8))) return 2;
                }
                if (random.NextULong() != expected.NextULong()) return 3;
                random.Fill(bytes, 0);
                if (random.NextULong() != expected.NextULong()) return 4;
                return 0;
            }
            """);

    [Fact]
    public void UIntAndDoubleDrawsUseDocumentedBitsAndRanges()
        => Run("""
            using std;
            int main() {
                PseudoRandom ints = new PseudoRandom(0ul);
                if (ints.NextUInt() != 0xe220a839u) return 1;
                PseudoRandom values = new PseudoRandom(0ul); PseudoRandom words = values;
                double value = 0.0d;
                double expected = 0.0d;
                for (int i = 0; i < 10000; i++) {
                    value = values.NextDouble();
                    expected = (double)(words.NextULong() >> 11) / 9007199254740992.0d;
                    if (value != expected) return 2;
                    if (value < 0.0d || value >= 1.0d) return 3;
                }
                return 0;
            }
            """);
}
