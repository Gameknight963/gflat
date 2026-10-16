namespace gflat.Tests;

public sealed class IntegerFloatingConversionTests
{
    [Theory]
    [InlineData("long", "18014399583223809l")]
    [InlineData("long", "-18014399583223809l")]
    [InlineData("long", "18014399583223808l")]
    [InlineData("ulong", "18014399583223809ul")]
    [InlineData("extralong", "((extralong)1 << 100) + ((extralong)1 << 76) + 1")]
    [InlineData("extralong", "-(((extralong)1 << 100) + ((extralong)1 << 76) + 1)")]
    public void IntegerToFloatRoundsOnceAtTheDestinationPrecision(string type, string value)
    {
        Assert.Equal(0, CompilerTestHelper.Run($$"""
            const float folded = (float)({{value}});
            float Convert({{type}} value) => (float)value;
            int main() {
                {{type}} value = {{value}};
                if (Convert(value) != folded) return 1;
                return 0;
            }
            """).ExitCode);
    }

    [Theory]
    [InlineData("((extralong)1 << 100) + ((extralong)1 << 47) - 1")]
    [InlineData("((extralong)1 << 100) + ((extralong)1 << 47)")]
    [InlineData("((extralong)1 << 100) + ((extralong)1 << 47) + 1")]
    [InlineData("((extralong)1 << 100) + 3 * ((extralong)1 << 47)")]
    public void WideIntegerToDoubleAgreesAroundRoundingTies(string value)
    {
        Assert.Equal(0, CompilerTestHelper.Run($$"""
            const double folded = (double)({{value}});
            double Convert(extralong value) => (double)value;
            int main() {
                extralong value = {{value}};
                if (Convert(value) != folded) return 1;
                return 0;
            }
            """).ExitCode);
    }
}
