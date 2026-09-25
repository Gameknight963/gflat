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
}
