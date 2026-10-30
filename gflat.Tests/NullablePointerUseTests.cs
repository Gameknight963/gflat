using gflat.CompileExceptions;

namespace gflat.Tests;

public class NullablePointerUseTests
{
    [Theory]
    [InlineData("int*? p = null; return *p;")]
    [InlineData("int*? p = null; return p[0];")]
    [InlineData("int*? p = null; p++; return 0;")]
    [InlineData("S*? p = null; return p.value;")]
    [InlineData("S*? p = null; return p.Get();")]
    [InlineData("int()*? f = null; return f();")]
    [InlineData("int()*? f = null; void* p = f; return 0;")]
    [InlineData("void*? p = null; int()* f = p; return 0;")]
    public void NullableUseRequiresCheckedCast(string body)
        => Assert.Throws<TypeCheckException>(() => Compiler.Check(
            "struct S { public int value; public int Get() => value; } int main() { " + body + " }"));

    [Fact]
    public void CheckedRawAndFunctionPointersWork()
        => Assert.Equal(42, CompilerTestHelper.Run("""
            int F() => 40;
            int main() { int n = 2; int*? p = &n; int* q = (int*)p;
                int()*? f = &F; int()* g = (int()*)f; return *q + g(); }
            """).ExitCode);

    [Theory]
    [InlineData("int*? p = null; int* q = (int*)p; return *q;")]
    [InlineData("int()*? p = null; int()* q = (int()*)p; return q();")]
    public void CheckedNullCastsTrap(string body)
        => Assert.NotEqual(0, CompilerTestHelper.Run("int main() { " + body + " }").ExitCode);

    [Fact]
    public void FunctionPointerReturnTypesCannotSilentlyChangeCallingConvention()
        => Assert.Throws<TypeCheckException>(() => Compiler.Check(
            "int F() => 1; int main() { long()* f = &F; return 0; }"));

    [Theory]
    [InlineData("(int*)(char)0")]
    [InlineData("(int*)(extralong)0")]
    [InlineData("(int()*)(extralong)0")]
    [InlineData("(int*)((extralong)1 << 64)")]
    [InlineData("(int*)-((extralong)1 << 64)")]
    public void EveryIntegerRepresentationRejectsKnownNullAsNonNullable(string expression)
        => Assert.Contains("null or zero", Assert.Throws<TypeCheckException>(() => Compiler.Check(
            "int main() { " + expression + "; return 0; }")).Message);

    [Fact]
    public void IntegerZeroCanStillProduceNullablePointers()
        => Assert.Equal(0, CompilerTestHelper.Run("""
            int main() {
                int*? first = ((nuint)(char)0).reinterpret<int*?>();
                int*? second = ((nuint)(extralong)0).reinterpret<int*?>();
                int*? truncated = ((nuint)((extralong)1 << 64)).reinterpret<int*?>();
                if (first == null && second == null && truncated == null) return 0;
                return 1;
            }
            """).ExitCode);
}
