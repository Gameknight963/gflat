using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class TemporaryAddressTests
{
    [Theory]
    [InlineData("42", "int", "42")]
    [InlineData("3.5f", "float", "3.5f")]
    [InlineData("'x'", "char", "'x'")]
    [InlineData("true", "bool", "true")]
    [InlineData("sizeof(long)", "int", "8")]
    [InlineData("alignof(long)", "int", "8")]
    public void ConcreteTemporaryValuesHaveScopedAddressableStorage(string value, string type, string expected)
        => Assert.Equal(0, CompilerTestHelper.Run(
            "int main() { " + type + "* pointer = &(" + value + "); if (*pointer != " + expected + ") return 1; return 0; }").ExitCode);

    [Theory]
    [InlineData("nameof(int)", "'i'")]
    [InlineData("\"hello\"", "'h'")]
    public void StringPointerValuesHaveTheirOwnAddressableSlot(string value, string expected)
        => Assert.Equal(0, CompilerTestHelper.Run(
            "int main() { readonly(char)** pointer = &(" + value + "); if ((*pointer)[0] != " + expected + ") return 1; return 0; }").ExitCode);

    [Fact]
    public void TypedNullPointerTemporaryHasConcreteStorage()
        => Assert.Equal(0, CompilerTestHelper.Run("""
            int main() { int*?* pointer = &((int*?)null); if (*pointer != null) return 1; return 0; }
            """).ExitCode);

    [Theory]
    [InlineData("void Nothing() {} int main() { &Nothing(); return 0; }", "void")]
    [InlineData("extern void Nothing(); int main() { &Nothing(); return 0; }", "void")]
    [InlineData("int main() { &null; return 0; }", "null")]
    public void ValuelessExpressionsCannotHaveTheirAddressTaken(string source, string type)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(source));
        Assert.Contains(type, error.Description);
        Assert.Contains("value storage", error.Description);
    }
}
