namespace gflat.Tests;

public sealed class CastOperandParsingTests
{
    [Theory]
    [InlineData("int value = (int)default(int); return value;", 0)]
    [InlineData("nuint value = (nuint)reflect(int).Size; if (value == 4) return 42; return 0;", 42)]
    [InlineData("immutable(Standard::TypeInfo)*? value = (immutable(Standard::TypeInfo)*?)Standard::Describe<int*>().ElementType; if (value!.Size == 4) return 42; return 0;", 42)]
    public void CastsAcceptExistingUnaryOperandForms(string body, int expected)
        => Assert.Equal(expected, CompilerTestHelper.Run(TypeDescriptionTestSources.Include("int main() { " + body + " }")).ExitCode);

    [Fact]
    public void ParenthesizedValuesStillSupportAdditionAndSubtraction()
        => Assert.Equal(42, CompilerTestHelper.Run("int main() { int value = 40; return (value) + 3 - (value) + 39; }").ExitCode);
}
