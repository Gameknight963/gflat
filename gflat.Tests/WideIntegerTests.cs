using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class WideIntegerTests
{
    [Theory]
    [InlineData("(extralong)2")]
    [InlineData("((extralong)1 << 96) - ((extralong)1 << 96) + 2")]
    [InlineData("(char)2")]
    public void IntegralConstantArrayBoundsUseTheirValueRatherThanTheirStorageWidth(string bound)
        => Assert.Equal(42, CompilerTestHelper.Run(
            "const extralong Count = " + bound + "; int main() { int[Count] data = [20, 22]; return data[0] + data[1]; }").ExitCode);

    [Theory]
    [InlineData("(extralong)0")]
    [InlineData("(extralong)-1")]
    [InlineData("(extralong)1 << 96")]
    public void WideArrayBoundsAreRangeCheckedBeforeNarrowing(string bound)
        => Assert.Contains("Array size must be between", Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(
            "const extralong Count = " + bound + "; int main() { int[Count] data; return 0; }")).Message);

    [Fact]
    public void WideConstantArithmeticDoesNotWrapAtSixtyFourBits()
    {
        Assert.Equal(0, CompilerTestHelper.Run("""
            const bool Fold() {
                extralong value = 9223372036854775807l;
                return value + value > 0;
            }
            const bool positive = Fold();
            int main() {
                extralong value = 9223372036854775807l;
                if (!positive || value + value <= 0) return 1;
                return 0;
            }
            """).ExitCode);
    }

    [Theory]
    [InlineData("value + value", 2)]
    [InlineData("value * 3", 3)]
    [InlineData("value - 1", 0)]
    [InlineData("value / 2", 0)]
    [InlineData("value % 3", 0)]
    [InlineData("value | 7", 1)]
    [InlineData("value & 7", 0)]
    [InlineData("value ^ value", 0)]
    [InlineData("-value", -1)]
    public void WideOperatorsAgreeAtCompileTimeAndRuntime(string expression, int expected)
    {
        Assert.Equal(0, CompilerTestHelper.Run($$"""
            const extralong Fold() { extralong value = (extralong)1 << 96; return {{expression}}; }
            const extralong folded = Fold();
            const int upper = (int)(folded >> 96);
            int main() {
                extralong value = (extralong)1 << 96;
                extralong result = {{expression}};
                if (result != folded || upper != {{expected}}) return 1;
                return 0;
            }
            """).ExitCode);
    }

    [Fact]
    public void WideMutationsAndSignedOverflowRetainTheirWidth()
    {
        Assert.Equal(0, CompilerTestHelper.Run("""
            const bool Fold() {
                extralong value = (extralong)1 << 126;
                value *= 2;
                if (value >= 0 || -value != value) return false;
                value++;
                value -= 1;
                return value == (extralong)1 << 127;
            }
            const bool folded = Fold();
            int main() { if (folded) return 0; return 1; }
            """).ExitCode);
    }

    [Fact]
    public void WideConversionsAndMixedComparisonsRetainHighBits()
    {
        Assert.Equal(0, CompilerTestHelper.Run("""
            const extralong unsigned = (extralong)18446744073709551615ul;
            const extralong higher = unsigned + 1;
            const bool ordered = higher > 18446744073709551615ul;
            const ulong low = (ulong)higher;
            const double floating = (double)((extralong)1 << 100);
            const extralong restored = (extralong)floating;
            const int high = (int)(restored >> 100);
            int main() {
                extralong value = (extralong)18446744073709551615ul;
                value++;
                if (!ordered || low != 0ul || high != 1 || value <= 18446744073709551615ul) return 1;
                return 0;
            }
            """).ExitCode);
    }

    [Fact]
    public void WideConstantIndicesWorkForBuffersAndStrings()
    {
        Assert.Equal(0, CompilerTestHelper.Run("""
            const int Get() { int[2] values = [20, 22]; extralong index = 1; return values[index]; }
            const int element = Get();
            const char byteValue = "ab"[(extralong)1];
            const readonly(char)* suffix = "ab" + (extralong)1;
            int main() { if (element == 22 && byteValue == 'b' && suffix[0] == 'b') return 0; return 1; }
            """).ExitCode);
    }

    [Theory]
    [InlineData("(extralong)1 << 128")]
    [InlineData("((extralong)1 << 127) / -1")]
    public void UndefinedWideConstantOperationsProduceDiagnostics(string expression)
        => Assert.Throws<TypeCheckException>(() => Compiler.Check("const extralong value = " + expression + ";"));
}
