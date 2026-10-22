using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class VoidStorageTests
{
    [Theory]
    [InlineData("void F(void value) {}")]
    [InlineData("extern void F(void value);")]
    [InlineData("struct S { public S(void value) {} }")]
    [InlineData("interface I { void F(void value); }")]
    [InlineData("struct S { public static S operator +(S a, void b) => new S(); }")]
    [InlineData("int main() { void value; return 0; }")]
    [InlineData("void value;")]
    [InlineData("class S { public void value; }")]
    [InlineData("struct S { public void value; }")]
    [InlineData("static class S { public static void value; }")]
    [InlineData("int main() { void[2] values; return 0; }")]
    [InlineData("alias Empty = void; int main() { Empty value; return 0; }")]
    [InlineData("int main() { void[2]*? values = null; return 0; }")]
    [InlineData("int main() { default(void); return 0; }")]
    [InlineData("void F() {} int main() { void value = F(); return 0; }")]
    [InlineData("void Take(void(void)* callback) {}")]
    [InlineData("void Read(void* data) { data[0]; }")]
    public void VoidCannotOccupyValueStorage(string source)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(source));
        Assert.Contains("void", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VoidReturnsAndOpaquePointersRemainSupported()
    {
        Assert.Equal(0, CompilerTestHelper.Run("""
            alias Nothing = void;
            Nothing NoResult() {}
            void Call(void()* callback, immutable(void*) opaque) { callback(); }
            int main() {
                int value = 42;
                void* address = (void*)&value;
                void** slot = &address;
                immutable(void*) view = *slot;
                Call(NoResult, view);
                if (*(int*)address != 42) return 1;
                return 0;
            }
            """).ExitCode);
    }
}
