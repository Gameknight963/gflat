namespace gflat.Tests;

public sealed class LambdaTemporaryAddressTests
{
    [Theory]
    [InlineData("() => 42")]
    [InlineData("static () => 42")]
    public void LambdaValuesHaveAddressableFunctionPointerSlots(string lambda)
        => Assert.Equal(42, CompilerTestHelper.Run("int main(){int()** slot=&(" + lambda + ");return (*slot)();}").ExitCode);

    [Theory]
    [InlineData("(int value) => value+1")]
    [InlineData("static (int value) => value+1")]
    public void AddressedLambdaSlotsPreserveParameterSignatures(string lambda)
        => Assert.Equal(42, CompilerTestHelper.Run("int main(){int(int)** slot=&(" + lambda + ");return (*slot)(41);}").ExitCode);

    [Fact]
    public void WritingThroughTheAddressReplacesTheFunctionPointerValue()
        => Assert.Equal(42, CompilerTestHelper.Run("int main(){int()** slot=&(() => 1);*slot=static () => 42;return (*slot)();}").ExitCode);
}
