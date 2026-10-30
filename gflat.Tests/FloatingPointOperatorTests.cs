using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class FloatingPointOperatorTests
{
    [Theory]
    [InlineData("float", "f")]
    [InlineData("double", "d")]
    public void ArithmeticComparisonsAndMutationUseTheFloatingType(string type, string suffix)
    {
        string code = $$"""
            const bool Check() {
                {{type}} value = 3.5{{suffix}};
                if (value + 0.5{{suffix}} != 4.0{{suffix}}) return false;
                if (value - 0.5{{suffix}} != 3.0{{suffix}}) return false;
                if (value * 2.0{{suffix}} != 7.0{{suffix}}) return false;
                if (value / 0.5{{suffix}} != 7.0{{suffix}}) return false;
                if (value % 2.0{{suffix}} != 1.5{{suffix}}) return false;
                if (!(value < 4.0{{suffix}}) || !(value <= 3.5{{suffix}}) || !(value > 3.0{{suffix}}) || !(value >= 3.5{{suffix}})) return false;
                if (-value != -3.5{{suffix}}) return false;
                if (value++ != 3.5{{suffix}} || ++value != 5.5{{suffix}}) return false;
                if (value-- != 5.5{{suffix}} || --value != 3.5{{suffix}}) return false;
                value += 0.5{{suffix}};
                value *= 2.0{{suffix}};
                value -= 1.0{{suffix}};
                value /= 2.0{{suffix}};
                value %= 2.0{{suffix}};
                return value == 1.5{{suffix}};
            }
            const bool folded = Check();
            int main() { if (!folded || !Check()) return 1; return 0; }
            """;
        Assert.Equal(0, CompilerTestHelper.Run(code).ExitCode);
    }

    [Fact]
    public void MixedFloatingWidthsWidenAndAliasesRetainFloatingOperations()
    {
        var result = CompilerTestHelper.Run("""
            alias Real = double;
            const bool Check() {
                float single = 1.5f;
                Real wide = 2.25d;
                if (single + wide != 3.75d || wide + single != 3.75d) return false;
                if (!(single < wide) || !(wide > single)) return false;
                wide += single;
                if (wide != 3.75d) return false;
                return -wide == -3.75d;
            }
            const bool folded = Check();
            int main() { if (!folded || !Check()) return 1; return 0; }
            """);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void FloatOperandsRoundBeforeBeingWidenedToDouble()
    {
        var result = CompilerTestHelper.Run("""
            const bool Check() { return 0.1f != 0.1d && 0.1d != 0.1f; }
            const bool folded = Check();
            int main() { if (!folded || !Check()) return 1; return 0; }
            """);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void LiteralsKeepTheirPrecisionAndNegationPreservesSignedZero()
    {
        var result = CompilerTestHelper.Run("""
            int main() {
                float single = 0.1f;
                double wide = 0.1d;
                if ((single).reinterpret<uint>() != 0x3dcccccdu) return 1;
                if ((wide).reinterpret<ulong>() != 0x3fb999999999999aul) return 2;
                double zero = 0.0d;
                double negativeZero = -zero;
                if ((negativeZero).reinterpret<ulong>() != 0x8000000000000000ul) return 3;
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void FloatMutationRoundsAtFloatPrecisionDuringConstantEvaluation()
    {
        var result = CompilerTestHelper.Run("""
            const bool Check() {
                float value = 16777216.0f;
                value++;
                if (value != 16777216.0f) return false;
                value += 1.0f;
                return value == 16777216.0f;
            }
            const bool folded = Check();
            int main() { if (!folded || !Check()) return 1; return 0; }
            """);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void NaNAndInfinityComparisonsFollowIeeeRules()
    {
        var result = CompilerTestHelper.Run("""
            const bool Check() {
                double zero = 0.0d;
                double nan = zero / zero;
                double infinity = 1.0d / zero;
                if (nan == nan || !(nan != nan)) return false;
                if (nan < zero || nan <= zero || nan > zero || nan >= zero) return false;
                if (zero < nan || zero <= nan || zero > nan || zero >= nan) return false;
                if (infinity != infinity || !(infinity == infinity)) return false;
                return infinity > 1.0d;
            }
            const bool folded = Check();
            int main() { if (!folded || !Check()) return 1; return 0; }
            """);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData("1.0f & 2.0f")]
    [InlineData("1.0d | 2.0d")]
    [InlineData("1.0f << 1")]
    [InlineData("1.0f + 1")]
    public void UnsupportedFloatingOperatorsProduceCompilerDiagnostics(string expression)
        => Assert.Throws<TypeCheckException>(() => Compiler.Check("void F() { " + expression + "; }"));
}
