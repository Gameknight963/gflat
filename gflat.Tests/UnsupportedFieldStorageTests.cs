using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class UnsupportedFieldStorageTests
{
    [Theory]
    [InlineData("int counter = 1; int main() => 0;", "Mutable global")]
    [InlineData("namespace State { int counter; } int main() => 0;", "Mutable global")]
    [InlineData("struct S { public static int counter; } int main() => 0;", "Static field")]
    [InlineData("class C { public static int counter = 1; } int main() => 0;", "Static field")]
    [InlineData("class C { struct S { public static int counter; } } int main() => 0;", "Static field")]
    [InlineData("struct S<T> { public static T value; } int main() => 0;", "Static field")]
    public void UnsupportedStorageProducesSourceErrorBeforeLowering(string source, string message)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Emit(source));
        Assert.Contains(message, error.Message);
    }
}
