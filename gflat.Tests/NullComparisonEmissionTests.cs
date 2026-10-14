namespace gflat.Tests;

public sealed class NullComparisonEmissionTests
{
    [Theory]
    [InlineData("int*? p = null; if (null != p) return 1; if (null == p) return 0; return 2;")]
    [InlineData("int value = 42; int*? p = &value; if (null == p) return 1; if (null != p) return 0; return 2;")]
    [InlineData("int(int)*? p = null; if (null != p) return 1; p = &Identity; if (null == p) return 2; return 0;")]
    [InlineData("I*? p = null; if (null != p) return 1; S value = new S(); p = &value; if (null == p) return 2; return 0;")]
    [InlineData("if (null != null) return 1; if (null == null) return 0; return 2;")]
    public void NullOnLeftUsesTheOtherOperandsRuntimeRepresentation(string body)
    {
        var result = CompilerTestHelper.Run("int Identity(int value) => value; interface I { int Get(); } struct S : I { public int Get() => 42; } int main() { " + body + " }");
        Assert.Equal(0, result.ExitCode);
    }
}
