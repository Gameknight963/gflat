using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class NullableCastNarrowingTests
{
    [Theory]
    [InlineData("int* value = (int*)allocate();")]
    [InlineData("void* value = (void*)allocate();")]
    public void PointerCastsRequireExplicitNullNarrowing(string declaration)
        => Assert.Throws<TypeCheckException>(() => Compiler.Check(
            "extern \"C\" void*? allocate(); int main() { " + declaration + " return 0; }",
            options: new() { NoStdLib = true, NoBuiltins = true }));

    [Theory]
    [InlineData("int*? value = (int*?)allocate();")]
    [InlineData("int* value = (int*)allocate()!;")]
    [InlineData("void*? raw = allocate(); if (raw == null) return 1; int* value = (int*)raw;")]
    public void TypeChangesPreserveNullabilityOrUseAnExplicitProof(string declaration)
        => Compiler.Check("extern \"C\" void*? allocate(); int main() { " + declaration + " return 0; }",
            options: new() { NoStdLib = true, NoBuiltins = true });
}
