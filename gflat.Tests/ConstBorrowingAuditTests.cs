using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ConstBorrowingAuditTests
{
    [Fact]
    public void ScalarPointerZeroIndexHasTheSameMeaningAsDereference()
        => Assert.Equal(42, CompilerTestHelper.Run("""
            extension int {
                public const int Add(int amount) { this[0] += amount; return this[0]; }
            }
            const int Fold() { int value = 40; value.Add(2); return value; }
            const int answer = Fold();
            int main() => answer;
            """).ExitCode);

    [Fact]
    public void ConstantCStringElementCanSupplyAReadonlyScalarReceiver()
        => Assert.Equal(42, CompilerTestHelper.Run("""
            extension char { public const readonly int Read() => (int)*this; }
            const int Fold() { readonly(char)* text = "*"; return text[0].Read(); }
            const int answer = Fold();
            int main() => answer;
            """).ExitCode);

    [Fact]
    public void ReadonlyPointerSlotsStillAllowMutatingTheirMutablePointee()
        => Assert.Equal(42, CompilerTestHelper.Run("""
            extension int { public int Add(int amount) { *this += amount; return *this; } }
            int main() { int value = 40; readonly(int*) pointer = &value; pointer.Add(2); return value; }
            """).ExitCode);

    [Theory]
    [InlineData("readonly(int)* pointer = &value;")]
    [InlineData("immutable(int)* pointer = &value;")]
    public void ProtectedPointeesCannotCallAMutableExtension(string declaration)
        => Assert.Throws<TypeCheckException>(() => Compiler.Check("""
            extension int { public int Add() { *this += 2; return *this; } }
            int main() { int value = 40;
            """ + declaration + " return pointer.Add(); }"));

    [Fact]
    public void PointerDepthIsNotSilentlyFlattenedForExtensionLookup()
        => Assert.Throws<TypeCheckException>(() => Compiler.Check("""
            extension int { public readonly int Read() => *this; }
            int main() { int value = 42; int* first = &value; int** second = &first; return second.Read(); }
            """));
}
