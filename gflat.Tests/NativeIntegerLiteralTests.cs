using gflat.ast;
using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class NativeIntegerLiteralTests
{
    [Fact]
    public void UnsignedNativeOperatorsAcceptRepresentableLiteralsOnEitherSide()
    {
        var result = CompilerTestHelper.Run("""
            int main() {
                nuint count = 12;
                if (count == 0 || 0 == count || count != 12 || 12 != count) return 1;
                if (count < 12 || 12 > count || count > 12 || 12 < count) return 2;
                if (!(count >= 12) || !(12 <= count)) return 3;
                if (count + 1 != 13 || 1 + count != 13) return 4;
                if (count - 1 != 11 || 13 - count != 1) return 5;
                if (count * 2 != 24 || 2 * count != 24) return 6;
                if (count / 2 != 6 || 24 / count != 2) return 7;
                if (count % 5 != 2 || 13 % count != 1) return 8;
                if ((count & 7) != 4 || (7 & count) != 4) return 9;
                if ((count | 1) != 13 || (1 | count) != 13) return 10;
                if ((count ^ 3) != 15 || (3 ^ count) != 15) return 11;
                if ((count << 1) != 24 || (count >> 1) != 6) return 12;
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void ComparisonsPreserveNativeWidthAndSignednessAtTheBounds()
    {
        var result = CompilerTestHelper.Run("""
            int main() {
                nuint maximum = 18446744073709551615ul;
                if (maximum != 18446744073709551615ul || 18446744073709551615ul != maximum) return 1;
                if (!(maximum > 4294967295u) || !(4294967295u < maximum)) return 2;
                if (!(maximum > 0xffffffff) || !(0xffffffff < maximum)) return 7;
                if (maximum - 0xffffffff != 18446744069414584320ul) return 8;
                nint negative = -1;
                if (!(negative < 0ul) || !(0ul > negative)) return 3;
                nint upper = 9223372036854775807l;
                if (upper != 9223372036854775807ul || 9223372036854775807ul != upper) return 4;
                nint lower = (nint)(-9223372036854775807l - 1);
                if (lower != -9223372036854775808ul || -9223372036854775808ul != lower) return 5;
                if (negative / 1ul != -1 || negative % 2ul != -1) return 6;
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData("nuint count = 1; count == -1;")]
    [InlineData("nuint count = 1; -1 == count;")]
    [InlineData("nuint count = 1; count + -1;")]
    [InlineData("nuint count = 1; -1 * count;")]
    [InlineData("nint count = 1; count == 9223372036854775808ul;")]
    [InlineData("nint count = 1; 18446744073709551615ul + count;")]
    [InlineData("nuint count = 1; int other = 1; count == other;")]
    [InlineData("nuint count = 1; int other = 1; other + count;")]
    [InlineData("nint count = 1; ulong other = 1ul; count == other;")]
    public void IncompatibleLiteralsAndSignedVariablesStillRequireExplicitCasts(string body)
        => Assert.Throws<TypeCheckException>(() => Compiler.Check("void F() { " + body + " }"));

    [Fact]
    public void LiteralOperandsRetainTheirSourceTypesForEditorPresentation()
    {
        var (ast, checker) = Compiler.Check("bool F(nuint count) => count != 0;");
        var method = ast.Members.OfType<MethodDeclaration>().Single();
        var returned = Assert.IsType<ReturnStatement>(method.Body!.Statements.Single());
        var comparison = Assert.IsType<BinaryExpression>(returned.Value);
        Assert.Equal("int", Assert.IsType<NamedTypeExpression>(checker.GetType(comparison.Right)).Name);
        Assert.Equal("bool", Assert.IsType<NamedTypeExpression>(checker.GetType(comparison)).Name);
    }

    [Fact]
    public void ConstantEvaluationUsesTheSameNativeLiteralConversions()
    {
        var result = CompilerTestHelper.Run("""
            const bool Check() {
                nuint maximum = 18446744073709551615ul;
                nint negative = -1;
                return maximum > 0xffffffff && maximum - 1 == 18446744073709551614ul &&
                    negative < 0ul && negative / 1ul == -1;
            }
            const bool valid = Check();
            int main() { if (!valid) return 1; return 0; }
            """);
        Assert.Equal(0, result.ExitCode);
    }
}
