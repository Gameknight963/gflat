using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class SuffixedIntegerLiteralDiagnosticsTests
{
    [Theory]
    [InlineData("9223372036854775809l")]
    [InlineData("9223372036854775808l")]
    [InlineData("-9223372036854775809l")]
    [InlineData("4294967296u")]
    [InlineData("18446744073709551616ul")]
    [InlineData("0x100000000u")]
    [InlineData("0x10000000000000000ul")]
    [InlineData("0x10000000000000000l")]
    public void ImpossibleSuffixedValuesFailDuringChecking(string expression)
    {
        string source = "int main() { return (int)" + expression + "; }";
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(source));
        Assert.Contains("literal", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(expression.TrimStart('-'), source.Substring(error.Span.Start, error.Span.Length));
    }

    [Fact]
    public void SignedMinimumAndUnsignedNegationKeepTheirExistingSemantics()
        => Assert.Equal(0, CompilerTestHelper.Run("""
            int main() {
                long minimum = -(9223372036854775808l);
                if (minimum != -9223372036854775808l) return 1;
                uint wrapped = -1u;
                if (wrapped != 4294967295u) return 2;
                ulong maximum = 18446744073709551615ul;
                if (maximum != 18446744073709551615ul) return 3;
                return 0;
            }
            """).ExitCode);

    [Fact]
    public void UnsuffixedLiteralsKeepContextualWidening()
        => Assert.Equal(0, CompilerTestHelper.Run("""
            int main() {
                long signed = 2147483648;
                nuint native = 4294967295;
                extralong wide = 18446744073709551616;
                if (signed != 2147483648l || native != 4294967295ul || wide <= 0) return 1;
                return 0;
            }
            """).ExitCode);

    [Fact]
    public void ExistingHexadecimalBitPatternsKeepTheirSignedInterpretation()
        => Assert.Equal(0, CompilerTestHelper.Run("""
            int main() {
                long all = 0xffffffffffffffffl;
                long minimum = 0x8000000000000000l;
                int bits = 0xffffffff;
                if (all != -1l || minimum != -9223372036854775808l || bits != -1) return 1;
                return 0;
            }
            """).ExitCode);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CliRejectsImpossibleSuffixesWithoutAnInternalCompilerError(bool check)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat_suffix_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string source = Path.Combine(directory, "main.gf"), output = Path.Combine(directory, "main.ll");
            File.WriteAllText(source, "int main() { return (int)9223372036854775809l; }");
            string[] arguments = check ? [source, "--no-stdlib", "--check"] : [source, "--no-stdlib", "--emit-ir", "-o", output];
            Assert.Equal(1, Program.Main(arguments));
            Assert.False(File.Exists(output));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void EnumConstantEvaluationAlsoChecksExplicitSuffixWidths()
        => Assert.Throws<TypeCheckException>(() => Compiler.Check("enum Code : ulong { Value = 18446744073709551615l }"));

    [Theory]
    [InlineData("nuint value = 4294967296u;")]
    [InlineData("extralong value = 18446744073709551616ul;")]
    [InlineData("long minimum = -9223372036854775808l; long invalid = 9223372036854775808l;")]
    public void WiderTargetsAndOtherUnaryExpressionsDoNotWaiveSuffixBounds(string statements)
        => Assert.Throws<TypeCheckException>(() => Compiler.Check("int main() { " + statements + " return 0; }"));

    [Fact]
    public void BackendReportsUnrepresentableUncontextualizedHexWithoutCrashing()
    {
        string source = "int main() { return (int)0x10000000000000000; }";
        Compiler.Check(source);
        Assert.Throws<TypeCheckException>(() => Compiler.Emit(source));
    }
}
