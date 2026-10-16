using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class OperatorSignatureTests
{
    [Fact]
    public void MatchingOperatorsOnBothOperandTypesAreAmbiguous()
        => Assert.Throws<TypeCheckException>(() => Compiler.Check("""
            struct Left { public static int operator +(Left a, Right b) => 1; }
            struct Right { public static int operator +(Left a, Right b) => 2; }
            int main() { Left a = default; Right b = default; return a + b; }
            """));

    [Theory]
    [InlineData("Number", "Number")]
    [InlineData("Number", "Alias")]
    [InlineData("Number", "readonly(Number)")]
    [InlineData("int", "Number")]
    public void DuplicateOperatorSignaturesCannotDifferByReturnTypeAliasesOrReadonly(string result, string secondOperand)
        => Assert.Throws<TypeCheckException>(() => Compiler.Check($$"""
            alias Alias = Number;
            struct Number {
                public static Number operator +(Number a, Number b) => default;
                public static {{result}} operator +({{secondOperand}} a, Number b) => default;
            }
            """));

    [Theory]
    [InlineData("a + a")]
    [InlineData("-a")]
    [InlineData("a += a")]
    public void PrivateOperatorsCannotBeInvokedOutsideTheirOwner(string expression)
        => Assert.Throws<TypeCheckException>(() => Compiler.Check($$"""
            struct Number {
                private static Number operator +(Number a, Number b) => default;
                private static Number operator -(Number a) => default;
            }
            int main() { Number a = default; {{expression}}; return 0; }
            """));

    [Theory]
    [InlineData("const")]
    [InlineData("readonly")]
    [InlineData("virtual")]
    [InlineData("override")]
    [InlineData("abstract")]
    [InlineData("ref")]
    public void UnsupportedOperatorDeclarationModifiersAreDiagnosed(string modifier)
        => Assert.Throws<TypeCheckException>(() => Compiler.Check($$"""
            struct Number { public {{modifier}} Number operator +(Number a, Number b) => default; }
            """));

    [Theory]
    [InlineData("ref Number b")]
    [InlineData("const Number b")]
    [InlineData("this Number b")]
    [InlineData("Number b = default")]
    public void UnsupportedOperatorOperandContractsAreDiagnosed(string operand)
        => Assert.Throws<TypeCheckException>(() => Compiler.Check($$"""
            struct Number { public static Number operator +(Number a, {{operand}}) => default; }
            """));

    [Fact]
    public void PrivateOperatorsRemainUsableInsideTheirOwner()
        => Assert.Equal(0, CompilerTestHelper.Run("""
            struct Number {
                public int Value;
                public Number(int value) { Value = value; }
                private static inline Number operator +(Number a, Number b) => new Number(a.Value + b.Value);
                private static Number operator -(Number a) => new Number(-a.Value);
                public static int Exercise() {
                    Number a = new Number(20); Number b = new Number(22);
                    a += b;
                    return (-a).Value;
                }
            }
            int main() { if (Number::Exercise() != -42) return 1; return 0; }
            """).ExitCode);

    [Fact]
    public void ReadonlyReturnTypesRemainDistinctFromTheRejectedMethodModifier()
        => Compiler.Check("""
            struct Number {
                public static readonly(Number) operator +(Number a, Number b) => default;
            }
            int main() { Number a = default; readonly(Number) b = a + a; return 0; }
            """);
}
