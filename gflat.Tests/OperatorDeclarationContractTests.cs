using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class OperatorDeclarationContractTests
{
    [Theory]
    [InlineData("*", 1)]
    [InlineData("+", 1)]
    [InlineData("/", 1)]
    [InlineData("&", 1)]
    [InlineData("==", 1)]
    [InlineData("!", 2)]
    public void OperatorDeclarationsRequireAnAritySupportedByExpressions(string symbol, int count)
    {
        string parameters = count == 1 ? "Number a" : "Number a, Number b";
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check($$"""
            struct Number {
                public static Number operator {{symbol}}({{parameters}}) => new Number();
            }
            """));
        Assert.Contains("parameter", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AllOperatorParameterTypesAreValidated()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check("""
            struct Number {
                public static Number operator +(Number a, MissingType b) => new Number();
            }
            """));
        Assert.Contains("MissingType", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnaryAndBinaryMinusRemainDistinctOverloads()
    {
        Assert.Equal(0, CompilerTestHelper.Run("""
            struct Number {
                public int Value;
                public Number(int value) { Value = value; }
                public static Number operator -(Number a) => new Number(-a.Value);
                public static Number operator -(Number a, Number b) => new Number(a.Value - b.Value);
            }
            int main() {
                Number a = new Number(7);
                Number b = new Number(2);
                Number negated = -a;
                Number difference = a - b;
                if (negated.Value != -7 || difference.Value != 5) return 1;
                return 0;
            }
            """).ExitCode);
    }
}
