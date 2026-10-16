using gflat.CompileExceptions;
using gflat.comptime;

namespace gflat.Tests;

public sealed class ConstantNullTests
{
    [Theory]
    [InlineData("const int*? None = null; int main() { if (None == null) return 0; return 1; }")]
    [InlineData("int main() { const int*? None = null; if (None == null) return 0; return 1; }")]
    [InlineData("struct Values { public const int*? None = null; } int main() { if (Values::None == null) return 0; return 1; }")]
    [InlineData("const int*? Make() => null; const int*? None = Make(); int main() { if (None == null) return 0; return 1; }")]
    public void NullablePointerNullIsAConstant(string source)
        => Assert.Equal(0, CompilerTestHelper.Run(source).ExitCode);

    [Theory]
    [InlineData("(readonly(char)*?)\"hi\" != null")]
    [InlineData("null != (readonly(char)*?)\"hi\"")]
    [InlineData("!((readonly(char)*?)\"hi\" == null)")]
    [InlineData("!(null == (readonly(char)*?)\"hi\")")]
    [InlineData("typeof(int*).ElementType != null")]
    [InlineData("null != typeof(int*).ElementType")]
    [InlineData("!(typeof(int*).ElementType == null)")]
    [InlineData("!(null == typeof(int*).ElementType)")]
    public void KnownStaticAddressesCompareAsNonNull(string expression)
        => Assert.Equal(0, CompilerTestHelper.Run("const bool Value = " + expression + "; int main() { if (Value) return 0; return 1; }").ExitCode);

    [Theory]
    [InlineData("const int* Value = null;")]
    [InlineData("const int Value = null;")]
    [InlineData("int Read(int* value = null) => 0;")]
    public void NullStillRequiresANullableDestination(string source)
        => Assert.Throws<TypeCheckException>(() => Compiler.Check(source + " int main() => 0;"));

    [Fact]
    public void SymbolicAddressesHaveSupportedNonNullEquality()
    {
        ConstValue[] addresses = [new ConstValue.String(""), new ConstValue.Pointer("int", new ConstValue.Integer(1))];
        foreach (var address in addresses)
        {
            Assert.True(ConstValue.TryEqual(address, new ConstValue.Integer(0), out bool forward));
            Assert.False(forward);
            Assert.True(ConstValue.TryEqual(new ConstValue.Integer(0), address, out bool reverse));
            Assert.False(reverse);
        }
    }
}
