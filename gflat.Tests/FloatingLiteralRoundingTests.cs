namespace gflat.Tests;

public sealed class FloatingLiteralRoundingTests
{
    [Theory]
    [InlineData("1.00000005960464477539062500001f", "1.00000011920928955078125f")]
    [InlineData("1.00000005960464477539062499999f", "1.0f")]
    [InlineData("1.000000059604644775390625f", "1.0f")]
    [InlineData("1.000000178813934326171875f", "1.0000002384185791015625f")]
    [InlineData("-1.00000005960464477539062500001F", "-1.00000011920928955078125f")]
    public void FloatLiteralsRoundDirectlyFromDecimalToBinary32(string literal, string expected)
    {
        Assert.Equal(0, CompilerTestHelper.Run($$"""
            const float folded = {{literal}};
            int main() {
                float value = {{literal}};
                if (value != {{expected}} || folded != {{expected}}) return 1;
                return 0;
            }
            """).ExitCode);
    }
}
