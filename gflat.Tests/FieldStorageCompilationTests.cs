namespace gflat.Tests;

public sealed class FieldStorageCompilationTests
{
    [Theory]
    [InlineData("int counter = 1; int main() => 0;")]
    [InlineData("namespace State { int counter; } int main() => 0;")]
    [InlineData("struct S { public static int counter; } int main() => 0;")]
    [InlineData("class C { public static int counter = 1; } int main() => 0;")]
    [InlineData("class C { struct S { public static int counter; } } int main() => 0;")]
    [InlineData("struct S<T> { public static T value; } int main() => 0;")]
    public void StorageDeclarationsCompile(string source) => Compiler.Emit(source);
}
