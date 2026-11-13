using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ReferenceArgumentQualifierTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ExplicitBorrowQualifiersPreserveStorageAndWorkInsideGenericFunctions(int optimization)
    {
        const string code = """
            int Read(ref readonly(int) value) nothrow => value;
            int ReadDeep(ref immutable(int) value) nothrow => value;
            void Change(ref int value) nothrow { value++; }
            int ReadGeneric<T>(ref T value) => Read(readonly ref value) + ReadDeep(immutable ref value);
            int main()
            {
                int value = 20;
                if (Read(readonly ref value) != 20 || ReadDeep(immutable ref value) != 20) return 1;
                if (ReadGeneric<int>(ref value) != 40) return 2;
                Change(ref value);
                readonly int frozen = 21;
                if (Read(ref frozen) != 21 || Read(readonly ref frozen) != 21 || value != 21) return 3;
                return 0;
            }
            """;
        var result = CompilerTestHelper.Run(code, optimization);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData("readonly")]
    [InlineData("immutable")]
    public void RestrictedBorrowsCannotBindToMutableParameters(string qualifier)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(
            "void Change(ref int value) { value++; } int main() { int value = 1; Change(" + qualifier +
            " ref value); return 0; }", options: new() { NoStdLib = true }));
        Assert.Contains("cannot pass", error.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeepBorrowProtectsPointeesAndShallowBorrowStillAllowsIndirectMutation()
    {
        const string prefix = "struct View { public int* Data; public View(int* data) { Data = data; } } ";
        Compiler.Check(prefix + "void Write(ref readonly(View) view) { *view.Data = 42; } " +
            "int main() { int value = 1; View view = new(&value); Write(readonly ref view); return 0; }",
            options: new() { NoStdLib = true });
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(prefix +
            "void Write(ref immutable(View) view) { *view.Data = 42; }", options: new() { NoStdLib = true }));
        Assert.Contains("readonly", error.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExplicitQualifiersStillRequireExistingStorage()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(
            "void Read(ref readonly(int) value) {} int Make() => 1; int main() { Read(readonly ref Make()); return 0; }",
            options: new() { NoStdLib = true }));
        Assert.Contains("existing storage", error.Description);
    }
}
