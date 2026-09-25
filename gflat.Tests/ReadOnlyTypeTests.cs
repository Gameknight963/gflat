using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ReadOnlyTypeTests
{
    private const string Buffer = """
        struct Buffer {
            public int* data;
            public int count;
            public Buffer(int* p) { data = p; count = 1; }
            public void Set() { data[0] = 99; }
            public readonly int Get() => data[0];
            public readonly readonly(int)* Data() => data;
        }
        """;

    [Theory]
    [InlineData("view.count = 2;")]
    [InlineData("view.data = &x;")]
    [InlineData("*view.data = 2;")]
    [InlineData("view.data[0] = 2;")]
    [InlineData("view.data[0]++;")]
    [InlineData("int* p = view.data;")]
    [InlineData("int* p = &view.data[0];")]
    [InlineData("view.Set();")]
    [InlineData("view.Data()[0] = 2;")]
    [InlineData("Buffer copy = *view;")]
    public void ReadOnlyReceiverCannotExposeWritableStorage(string operation)
        => Assert.Throws<TypeCheckException>(() => Compiler.Check(Buffer +
            "int main() { int x = 42; Buffer b = new Buffer(&x); readonly(Buffer)* view = &b; " + operation + " return 0; }"));

    [Theory]
    [InlineData("data[0] = 0;")]
    [InlineData("*data = 0;")]
    [InlineData("this.data[0] = 0;")]
    [InlineData("int* p = data;")]
    public void ReadOnlyMethodsPreserveTransitiveQualification(string operation)
        => Assert.Throws<TypeCheckException>(() => Compiler.Check(
            "struct S { public int* data; public readonly void Change() { " + operation + " } }"));

    [Theory]
    [InlineData("readonly(int) x = 1; x = 2;")]
    [InlineData("int x = 1; readonly(int*) p = &x; p = &x;")]
    [InlineData("int x = 1; readonly(int*) p = &x; *p = 2;")]
    [InlineData("readonly(int[2]) a = [1, 2]; a[0] = 2;")]
    [InlineData("readonly(int)[2] a = [1, 2]; a[0] = 2;")]
    [InlineData("readonly(int) x = 1; int* p = &x;")]
    [InlineData("readonly(int[2]) a = [1, 2]; int* p = a;")]
    [InlineData("readonly(Buffer) b = default(Buffer); Buffer copy = (Buffer)b;")]
    [InlineData("int x = 1; int* p = &x; int** pp = &p; readonly(int)** unsafeView = pp;")]
    [InlineData("readonly(Buffer) b = default(Buffer); b.Set();")]
    public void InvalidWritesAndConversionsAreRejected(string body)
        => Assert.Throws<TypeCheckException>(() => Compiler.Check(Buffer + "int main() { " + body + " return 0; }"));

    [Fact]
    public void CollectionViewAndExplicitRawCastExecute()
        => Assert.Equal(43, CompilerTestHelper.Run(Buffer + """
            int main() {
                int x = 41; Buffer b = new Buffer(&x); readonly(Buffer)* view = &b;
                readonly(int)* p = view.Data();
                int* writable = (int*)p; *writable = 42;
                readonly(int) n = view.Get(); int copy = n; copy++;
                return copy;
            }
            """).ExitCode);

    [Fact]
    public void PointerToReadOnlyCanBeReassignedAndReadOnlyArraysCanBeCopied()
        => Assert.Equal(42, CompilerTestHelper.Run("""
            int main() {
                int x = 1; int y = 40; readonly(int)* p = &x; p = &y;
                readonly(int[2]) a = [1, 2]; int[2] b = a; b[0] = 2;
                return *p + b[0];
            }
            """).ExitCode);

    [Fact]
    public void ConstructorInitializesReadOnlyFieldsAndCleanupStillRuns()
        => Assert.Equal(42, CompilerTestHelper.Run("""
            struct S {
                public readonly(int) value;
                private int* counter;
                public S(int n, int* p) { value = n; counter = p; }
                public readonly int Get() => value;
                public ~S() { *counter = 42; }
            }
            int main() { int destroyed = 0; { readonly(S) s = new S(7, &destroyed); if (s.Get() != 7) return 1; } return destroyed; }
            """).ExitCode);

    [Fact]
    public void ReadOnlyMethodCanReturnUnrelatedMutableStorage()
        => Assert.Equal(42, CompilerTestHelper.Run("""
            struct S { public readonly int* Data(int* external) => external; }
            int main() { int x = 0; readonly(S) s = new S(); *s.Data(&x) = 42; return x; }
            """).ExitCode);

    [Fact]
    public void DeepReadOnlyPointerConversionIsSafe()
        => Assert.Equal(42, CompilerTestHelper.Run("""
            int main() { int x = 42; int* p = &x; readonly(int*)* pp = &p; return **pp; }
            """).ExitCode);

    [Fact]
    public void AliasesAndGenericArgumentsKeepTheirQualifiers()
    {
        Assert.Equal(42, CompilerTestHelper.Run("alias Read = readonly(int); struct Box<T> { public T value; public Box(T v) { value = v; } } int main() { Box<int> a = new Box<int>(1); a.value = 2; Box<Read> b = new Box<Read>(40); return a.value + b.value; }").ExitCode);
        Assert.Throws<TypeCheckException>(() => Compiler.Check("alias Read = readonly(int); struct Box<T> { public T value; public Box(T v) { value = v; } } int main() { Box<Read> b = new Box<Read>(42); b.value = 1; return 0; }"));
    }

    [Theory]
    [InlineData("view.inner.data[0] = 1;")]
    [InlineData("view.items[0][0] = 1;")]
    [InlineData("int* p = view.items[0];")]
    [InlineData("Buffer b = view.inner;")]
    public void NestedFieldsAndPointerArraysKeepReadOnly(string operation)
        => Assert.Throws<TypeCheckException>(() => Compiler.Check(Buffer +
            "struct Outer { public Buffer inner; public int*[2] items; } int main() { readonly(Outer) view = default(Outer); " + operation + " return 0; }"));

    [Theory]
    [InlineData("value = 2;")]
    [InlineData("this.value = 2;")]
    public void AliasedReadOnlyFieldsCannotBeChangedOutsideConstructor(string operation)
        => Assert.Throws<TypeCheckException>(() => Compiler.Check(
            "alias Read = readonly(int); struct S { public Read value; public S(int n) { this.value = n; } public void Change() { " + operation + " } }"));

    [Fact]
    public void PropertyReturnQualificationIsIndependentOfReceiver()
        => Assert.Equal(42, CompilerTestHelper.Run("""
            struct S {
                public int* data;
                public S(int* p) { data = p; }
                public readonly readonly(int)* Data => data;
            }
            int main() { int x = 42; readonly(S) s = new S(&x); return *s.Data; }
            """).ExitCode);

    [Fact]
    public void ManagedViewsPreserveTransitiveQualification()
    {
        const string source = """
            extern void*? malloc(nuint size);
            void*? __gflat_gc_alloc(nuint size) => malloc(size);
            struct S { public int^ data; public S(int^ p) { data = p; } }
            """;
        Assert.Equal(42, CompilerTestHelper.Run(source + "int main() { int^ p = new^ int(42); readonly(S)^ view = new^ S(p); readonly(int)^ q = view.data; return *q; }").ExitCode);
        Assert.Throws<TypeCheckException>(() => Compiler.Check(source + "int main() { int^ p = new^ int(42); readonly(S)^ view = new^ S(p); *view.data = 0; return 0; }"));
        Assert.Throws<TypeCheckException>(() => Compiler.Check(source + "int main() { int^ p = new^ int(42); readonly(S)^ view = new^ S(p); int^ q = (int^)view.data; return 0; }"));
    }
}
