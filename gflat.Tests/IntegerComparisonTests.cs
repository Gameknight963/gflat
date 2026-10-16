using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class IntegerComparisonTests
{
    [Theory]
    [InlineData("long", "uint")]
    [InlineData("nint", "uint")]
    [InlineData("int", "ushort")]
    [InlineData("short", "ushort")]
    [InlineData("sbyte", "byte")]
    public void LosslessSignedAndUnsignedComparisonsUseTheCommonSignedType(string signed, string unsigned)
    {
        string source = $$"""
            const bool Check() {
                {{signed}} negative = -1;
                {{unsigned}} zero = 0;
                if (!(negative < zero) || !(zero > negative)) return false;
                if (!(negative <= zero) || !(zero >= negative)) return false;
                if (negative == zero || zero == negative) return false;
                if (!(negative != zero) || !(zero != negative)) return false;
                return true;
            }
            const bool folded = Check();
            int main() { if (!folded || !Check()) return 1; return 0; }
            """;
        Assert.Equal(0, CompilerTestHelper.Run(source).ExitCode);
    }

    [Theory]
    [InlineData("uint", "int")]
    [InlineData("ulong", "long")]
    [InlineData("nuint", "nint")]
    public void IncompatibleSignedAndUnsignedVariablesStillRequireCasts(string unsigned, string signed)
        => Assert.Throws<TypeCheckException>(() => Compiler.Check($"bool F({unsigned} a, {signed} b) => a < b;"));
}
