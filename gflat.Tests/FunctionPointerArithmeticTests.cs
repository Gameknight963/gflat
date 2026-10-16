using gflat.CompileExceptions;
using Xunit;

namespace gflat.Tests;

public class FunctionPointerArithmeticTests
{
    [Theory]
    [InlineData("callback++;")]
    [InlineData("--callback;")]
    [InlineData("callback += 1;")]
    [InlineData("callback -= 1;")]
    [InlineData("int()* next = 1 + callback;")]
    [InlineData("long difference = callback - callback;")]
    public void CodeAddressesDoNotPretendToBeArrays(string operation)
    {
        string source = "int F() => 42; int main() { int()* callback = F; " + operation + " return 0; }";
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Emit(source));
        Assert.Contains("function pointers", error.Message);
    }
}
