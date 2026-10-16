using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ConstantIntegerOperatorTests
{
    [Theory]
    [InlineData("a + b", -13)]
    [InlineData("a - b", -33)]
    [InlineData("a * b", 26)]
    [InlineData("a / b", -2)]
    [InlineData("a % b", -3)]
    [InlineData("a & b", 8)]
    [InlineData("a | b", -21)]
    [InlineData("a ^ b", -29)]
    [InlineData("a >> 1u", -12)]
    [InlineData("a << 1u", -46)]
    [InlineData("a << 7u", -128)]
    [InlineData("-a", 23)]
    [InlineData("a + 1", -22)]
    public void CharacterArithmeticAgreesAtCompileTimeAndRuntime(string expression, int expected)
    {
        string source = $$"""
            const int Fold() { char a = (char)233; char b = (char)10; return (int)({{expression}}); }
            const int folded = Fold();
            int main() {
                char a = (char)233;
                char b = (char)10;
                if (folded != {{expected}} || (int)({{expression}}) != folded) return 1;
                return 0;
            }
            """;
        Assert.Equal(0, CompilerTestHelper.Run(source).ExitCode);
    }

    [Fact]
    public void NarrowIntegerMutationAndNegationWrapAtTheirDeclaredWidth()
    {
        var result = CompilerTestHelper.Run("""
            const bool Check() {
                char value = (char)127;
                if ((int)++value != -128) return false;
                if ((int)value-- != -128 || (int)value != 127) return false;
                value += (char)1;
                if ((int)value != -128) return false;
                sbyte minimum = -128;
                if ((int)-minimum != -128) return false;
                uint unsigned = 1u;
                return -unsigned == 4294967295u;
            }
            const bool folded = Check();
            int main() {
                char value = (char)127;
                sbyte minimum = -128;
                uint unsigned = 1u;
                if (!folded || (int)++value != -128 || (int)-minimum != -128 || -unsigned != 4294967295u) return 1;
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData("int", "-8", "2u", "-2")]
    [InlineData("uint", "2147483648u", "31ul", "1u")]
    [InlineData("long", "-8l", "2u", "-2l")]
    [InlineData("ulong", "9223372036854775808ul", "63u", "1ul")]
    public void SignedAndUnsignedShiftsAcceptIntegerCountTypes(string type, string value, string count, string expected)
    {
        var result = CompilerTestHelper.Run($$"""
            const {{type}} folded = {{value}} >> {{count}};
            int main() {
                {{type}} value = {{value}};
                if (folded != {{expected}} || value >> {{count}} != folded) return 1;
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData("1 << 32")]
    [InlineData("1 >> -1")]
    [InlineData("1ul << 64u")]
    [InlineData("1u >> 18446744073709551615ul")]
    public void InvalidConstantShiftCountsProduceDiagnostics(string expression)
        => Assert.Contains("shift", Assert.Throws<TypeCheckException>(() => Compiler.Check("const int invalid = (int)(" + expression + ");")).Message, StringComparison.OrdinalIgnoreCase);

    [Theory]
    [InlineData("-2147483648 / -1")]
    [InlineData("-2147483648 % -1")]
    [InlineData("(char)128 / (char)255")]
    public void UndefinedSignedDivisionOverflowIsDiagnosedDuringConstantEvaluation(string expression)
        => Assert.Contains("overflow", Assert.Throws<TypeCheckException>(() => Compiler.Check("const int invalid = (int)(" + expression + ");")).Message, StringComparison.OrdinalIgnoreCase);

    [Theory]
    [InlineData("int", "-2147483648", "/=")]
    [InlineData("int", "-2147483648", "%=")]
    [InlineData("char", "(char)128", "/=")]
    public void UndefinedCompoundDivisionOverflowIsDiagnosed(string type, string minimum, string operation)
        => Assert.Contains("overflow", Assert.Throws<TypeCheckException>(() => Compiler.Check($$"""
            const int Fold() { {{type}} value = {{minimum}}; value {{operation}} -1; return (int)value; }
            const int invalid = Fold();
            """)).Message, StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void MixedIntegerArithmeticUsesTheCheckedCommonType()
    {
        Assert.Equal(0, CompilerTestHelper.Run("""
            const int Fold() { int a = -2; ushort b = 3; return a + b; }
            const int folded = Fold();
            int main() { int a = -2; ushort b = 3; if (folded == 1 && a + b == folded) return 0; return 1; }
            """).ExitCode);
    }

    [Theory]
    [InlineData("byte", "256", "0")]
    [InlineData("sbyte", "255", "-1")]
    [InlineData("char", "233", "-23")]
    [InlineData("ushort", "65537", "1")]
    [InlineData("uint", "4294967295ul", "-1")]
    public void EnumCastsUseTheirUnderlyingRepresentationDuringConstantEvaluation(string underlying, string input, string expected)
    {
        var result = CompilerTestHelper.Run($$"""
            enum Code : {{underlying}} { Zero = 0 }
            const Code folded = (Code){{input}};
            const int integer = (int)folded;
            int main() {
                Code value = (Code){{input}};
                if (integer != {{expected}} || (int)value != integer) return 1;
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
    }
}
