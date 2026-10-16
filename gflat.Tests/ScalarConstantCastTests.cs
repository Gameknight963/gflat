using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ScalarConstantCastTests
{
    [Theory]
    [InlineData("extralong", "(extralong)18446744073709551616", "(int)(Value >> 64) == 1")]
    [InlineData("extralong", "(extralong)0x10000000000000000", "(int)(Value >> 64) == 1")]
    [InlineData("extralong", "(extralong)-170141183460469231731687303715884105728", "(int)(Value >> 127) == -1")]
    [InlineData("bool", "(bool)true", "Value")]
    [InlineData("bool", "(readonly(bool))false", "!Value")]
    [InlineData("int*?", "(int*?)null", "Value == null")]
    [InlineData("readonly(char)*?", "(readonly(char)*?)null", "Value == null")]
    [InlineData("readonly(char)*", "(readonly(char)*)\"text\"", "Value[0] == 't'")]
    [InlineData("int", "(int)3.75", "Value == 3")]
    [InlineData("byte", "(byte)257", "Value == 1")]
    public void ConstantCastsMatchNativeCasts(string type, string expression, string condition)
    {
        string body = " int main() { if (" + condition + ") return 0; return 1; }";
        Assert.Equal(0, CompilerTestHelper.Run(type + " Value = " + expression + ";" + body).ExitCode);
        Assert.Equal(0, CompilerTestHelper.Run("const " + type + " Value = " + expression + ";" + body).ExitCode);
    }

    [Fact]
    public void AliasTargetContextAppliesInsideConstantMethod()
        => Assert.Equal(0, CompilerTestHelper.Run("alias Wide = extralong; const Wide Make() => (Wide)18446744073709551616; const Wide Value = Make(); int main() { return (int)(Value >> 64) - 1; }").ExitCode);

    [Fact]
    public void IdentityPointerCastPreservesConstantObjectStorage()
        => Assert.Equal(0, CompilerTestHelper.Run("struct Item { public int number; public Item(int n) { number=n; } } const int Read() { Item item = new Item(9); Item* pointer = &item; Item* same = (Item*)pointer; return same.number; } const int Value = Read(); int main() { return Value - 9; }").ExitCode);

    [Fact]
    public void NullCastStillCannotProduceNonNullablePointer()
        => Assert.Throws<TypeCheckException>(() => Compiler.Check("const int* Value = (int*)null;"));
}
