using gflat.CompileExceptions;

namespace gflat.Tests;

public class IndexerTests
{
    private const string Buffer = """
        struct Buffer<T> {
            T* data;
            public Buffer(T* p) { data = p; }
            public T this[ulong i] { readonly get { return data[i]; } set { data[i] = value; } }
        }
        """;

    [Fact]
    public void GenericIndexerSupportsReadsWritesAndCompoundUpdates()
        => Assert.Equal(42, CompilerTestHelper.Run(Buffer + """
            int main() { int[2] a = [20, 1]; Buffer<int> b = new Buffer<int>(a);
                b[1] = 20; b[1] += 1; int old = b[0]++; return old + b[1] + 1; }
            """).ExitCode);

    [Fact]
    public void ReadonlyGetterWorksOnReadonlyView()
        => Assert.Equal(42, CompilerTestHelper.Run(Buffer + "int main() { int[1] a = [42]; readonly(Buffer<int>) b = new Buffer<int>(a); return b[0]; }").ExitCode);

    [Fact]
    public void MultipleIndicesAreEvaluatedOnceInOrder()
    {
        var result = CompilerTestHelper.Run("""
            extern int printf(readonly(char)* text, ...);
            int Row() { printf("R"); return 1; }
            int Column() { printf("C"); return 2; }
            int Value() { printf("V"); return 3; }
            struct Grid {
                public int this[int row, int column] {
                    get { printf("G"); return row + column; }
                    set { printf("S"); }
                }
            }
            Grid* Receiver(Grid* g) { printf("O"); return g; }
            int main() { Grid g = new Grid(); (*Receiver(&g))[Row(), Column()] += Value(); return 0; }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("ORCGVS", result.StandardOutput);
    }

    [Fact]
    public void VirtualIndexerDispatchesThroughAnInterface()
        => Assert.Equal(42, CompilerTestHelper.Run("""
            interface I { int this[int i] { get; set; } }
            class Base : I { public virtual int this[int i] { get => 0; set {} } }
            class Derived : Base { int n; public override int this[int i] { get => n + i; set { n = value; } } }
            int main() { Derived d = new Derived(); Base* b = &d; I* p = b; p[1] = 40; return p[2]; }
            """).ExitCode);

    [Fact]
    public void ExpressionBodiedAndSetOnlyIndexersWork()
        => Assert.Equal(42, CompilerTestHelper.Run("""
            struct Reader { public int this[int i] => i + 20; }
            struct Writer { public int n; public int this[int i] { set => n = value + i; } }
            int main() { Reader r = new Reader(); Writer w = new Writer(); w[2] = r[20]; return w.n; }
            """).ExitCode);

    [Fact]
    public void RestrictedSetterWorksInsideItsType()
        => Assert.Equal(42, CompilerTestHelper.Run("""
            struct S { int n; public int this[int i] { get => n; private set => n = value; }
                public void Fill() { (*this)[0] = 42; } }
            int main() { S s = new S(); s.Fill(); return s[0]; }
            """).ExitCode);

    [Fact]
    public void RawPointerIndexingKeepsItsOriginalMeaning()
        => Assert.Equal(42, CompilerTestHelper.Run("struct S { public int n; public int this[int i] => 0; } int main() { S s = new S(); s.n = 42; S* p = &s; return p[0].n; }").ExitCode);

    [Fact]
    public void ReceiverIsDestroyedIfAnIndexThrowsBeforeTheValue()
    {
        var result = CompilerTestHelper.Run("""
            extern int printf(readonly(char)* text, ...);
            struct S { public int this[int i] { get => i; set { printf("S"); } } ~S() { printf("D"); } }
            S Make() { printf("M"); return new S(); }
            int Index() throws { printf("I"); throw new* Exception(); }
            int Value() { printf("V"); return 1; }
            int main() { try { Make()[Index()] = Value(); } catch { printf("C"); } return 0; }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("MIDC", result.StandardOutput);
    }

    [Fact]
    public void ThrowingAccessorsUseNormalExceptionHandling()
    {
        var result = CompilerTestHelper.Run("""
            extern int printf(readonly(char)* text, ...);
            struct S { public int this[int i] {
                get throws { printf("G"); throw new* Exception(); }
                set throws { printf("S"); throw new* Exception(); }
            } }
            int main() { S s = new S(); try { int n = s[0]; } catch { printf("C"); }
                try { s[0] = 1; } catch { printf("C"); } return 0; }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("GCSC", result.StandardOutput);
    }

    [Fact]
    public void ManagedReceiversAndInterfaceViewsSupportIndexers()
        => Assert.Equal(42, CompilerTestHelper.Run("""
            extern void*? malloc(ulong n);
            void*? __gflat_gc_alloc(ulong n) => malloc(n);
            interface I { int this[int i] { get; set; } }
            struct S : I { int n; public int this[int i] { get => n + i; set => n = value; } }
            int main() { S^ s = new^ S(); s[0] = 40; I^ p = s; return p[2]; }
            """).ExitCode);

    [Fact]
    public void PointerReturnedByIndexerRetainsAccessToOriginalStorage()
        => Assert.Equal(42, CompilerTestHelper.Run("""
            struct S { int* p; public S(int* q) { p = q; } public int* this[int i] => p + i; }
            int main() { int n = 0; S s = new S(&n); *s[0] = 42; return n; }
            """).ExitCode);

    [Fact]
    public void ValueReturningIndexerDoesNotExposeMutableAggregateStorage()
        => Assert.Equal(42, CompilerTestHelper.Run("""
            struct E { public int n; }
            struct S { public E this[int i] { get { E e = new E(); e.n = i; return e; } } }
            int main() { S s = new S(); return s[42].n; }
            """).ExitCode);

    [Fact]
    public void WritesCheckEveryIndexForDefiniteAssignment()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(
            "struct S { public int this[int i, int j] { set {} } } int main() { S s = new S(); int n; s[0, n] = 1; return 0; }"));
        Assert.Contains("unassigned", error.Message);
    }

    [Theory]
    [InlineData("struct S { public int this[int i] { get; set; } }")]
    [InlineData("struct S { public static int this[int i] => i; }")]
    [InlineData("struct S { public int this[int i] => i; public int this[long i] => 0; }")]
    [InlineData("struct S { public int this[] => 1; }")]
    [InlineData("struct S { public int this[int value] { get => value; set {} } }")]
    [InlineData("struct S { public int this[int i] => i; } int main() { S s = new S(); s[0] = 1; return 0; }")]
    [InlineData("struct S { public int this[int i] { set {} } } int main() { S s = new S(); return s[0]; }")]
    [InlineData("struct S { public int this[int i] { get => i; private set {} } } int main() { S s = new S(); s[0] = 1; return 0; }")]
    [InlineData("struct S { public int this[int i] => i; } int main() { readonly(S) s = new S(); return s[0]; }")]
    [InlineData("struct S { public int this[int i] => i; } int main() { S s = new S(); int* p = &s[0]; return 0; }")]
    [InlineData("struct S { public int this[int i] => i; } int main() { S s = new S(); return s[0, 1]; }")]
    [InlineData("int main() { int[2] a = [1, 2]; return a[0, 1]; }")]
    [InlineData("struct E { public int n; } struct S { public E this[int i] => new E(); } int main() { S s = new S(); s[0].n = 1; return 0; }")]
    [InlineData("struct E { public int n; } struct S { public E this[int i] => new E(); } int main() { S s = new S(); int* p = &s[0].n; return 0; }")]
    [InlineData("struct E { public int[1] n; } struct S { public E this[int i] => new E(); } int main() { S s = new S(); s[0].n[0] = 1; return 0; }")]
    [InlineData("struct E { public int[1] n; } struct S { public E this[int i] => new E(); } int main() { S s = new S(); int* p = &s[0].n[0]; return 0; }")]
    [InlineData("struct S { public readonly int this[int i] { get => i; } public S() {} } int main() { S s = new S(); s[0]++; return 0; }")]
    [InlineData("interface I { int this[int i] { get; } } int main() { I*? p = null; return p[0]; }")]
    [InlineData("interface I { int Get(); } struct S : I { public int Get() => 1; } int main() { S s = new S(); I* p = &s; p[0]; return 0; }")]
    [InlineData("struct S { public int this[int i] => i; } int main() { S s = new S(); const int n = s[0]; return n; }")]
    public void InvalidIndexersAndUsesAreRejected(string code)
        => Assert.Throws<TypeCheckException>(() => Compiler.Check(code));
}
