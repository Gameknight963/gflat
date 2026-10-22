namespace gflat.Tests;

public class ValueModelTests
{
    private const string View = """
        struct View<T> {
            T* data;
            public View(T* p) { data = p; }
            public ref T this[int i] { immutable get => data[i]; }
        }
        """;

    [Fact]
    public void ReferenceIndexerBorrowsAndProjectsReadonly()
    {
        var result = CompilerTestHelper.Run(View + """
            int Read(immutable(View<int>)* v) { return (*v)[0]; }
            int main() {
                int[2] numbers = [1, 2];
                View<int> v = new View<int>(numbers);
                v[0] = 10;
                v[1]++;
                int* p = &v[0];
                if (*p != 10 || Read(&v) != 10 || numbers[1] != 3) return 1;
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData("void F(immutable(View<int>)* v) { (*v)[0] = 1; }")]
    [InlineData("int* F(immutable(View<int>)* v) { return &(*v)[0]; }")]
    [InlineData("void F(immutable(View<int*>)* v) { *(*v)[0] = 1; }")]
    public void ReadonlyReferenceIndexerCannotLeakMutableStorage(string source)
        => Assert.Throws<gflat.CompileExceptions.TypeCheckException>(() => CompilerTestHelper.Check(View + source + "int main() { return 0; }"));

    [Fact]
    public void ReferenceIndexerWorksWithOwningElements()
    {
        var result = CompilerTestHelper.Run(Resource + View + """
            int main() {
                R[2] items = [new R(1), new R(2)];
                View<R> v = new View<R>(items);
                R* raw = &v[0];
                R moved = move *raw;
                new* R(4) at raw;
                v[1] = new R(3);
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("2134", result.StandardOutput);
    }

    private const string Resource = """
        extern int printf(readonly(char)* text, ...);
        struct R {
            public int n;
            public R(int value) { n = value; }
            ~R() { printf("%d", n); }
        }
        """;

    [Theory]
    [InlineData("void Use(R value) {} int main() { R a = new R(1); Use(move a); a = new R(2); return 0; }", "12")]
    [InlineData("int main() { R a = new R(1); a = new R(2); return 0; }", "12")]
    [InlineData("R Pass(R value) { return value; } int main() { R a = Pass(new R(3)); return 0; }", "3")]
    [InlineData("struct F { public R Value => new R(4); } int main() { F f = new F(); R a = f.Value; return 0; }", "4")]
    [InlineData("int Fail() { throw new Exception(); } void Use(R a, int b) {} int main() { try { Use(new R(5), Fail()); } catch {} return 0; }", "5")]
    [InlineData("struct Box<T> { public T item; public Box(T value) { item = move value; } } int main() { Box<R> b = new Box<R>(new R(6)); return 0; }", "6")]
    [InlineData("struct Box { R item; public Box(bool b) { if (b) item = new R(1); item = new R(2); } } int main() { { Box a = new Box(true); } { Box b = new Box(false); } return 0; }", "122")]
    [InlineData("class Base { R item; public Base(R value) { item = move value; } } class Derived : Base { public Derived(R value) : base(move value) {} } int main() { Derived d = new Derived(new R(6)); return 0; }", "6")]
    // The target getter throws before the right-hand R(2) is constructed.
    [InlineData("int Fail() { throw new Exception(); } struct V { R item = new R(1); public ref R this[int i] { get { Fail(); return item; } } } int main() { V v = new V(); try { v[0] = new R(2); } catch {} return 0; }", "1")]
    [InlineData("void Fail(R value) { throw new Exception(); } int main() { try { Fail(new R(7)); } catch {} return 0; }", "7")]
    [InlineData("void Use(R a, R b) {} R Pass(R value) { return value; } int main() { Use(new R(1), Pass(new R(2))); return 0; }", "21")]
    [InlineData("struct F { public R Value => new R(4); } R Get() { F f = new F(); return f.Value; } int main() { R a = Get(); return 0; }", "4")]
    [InlineData("struct B { R item = new R(1); public R Item { set { item = move value; } } } int main() { B b = new B(); b.Item = new R(2); return 0; }", "12")]
    [InlineData("struct F { public R Value => new R(4); } int main() { F f = new F(); R a = new R(2); a = f.Value; return 0; }", "24")]
    public void OwningValuesCompose(string program, string expected)
    {
        var result = CompilerTestHelper.Run(Resource + program);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expected, result.StandardOutput);
    }

    [Theory]
    [InlineData("interface I {} struct R : I { ~R() { printf(\"D\"); } }", "I* p = new* R(); delete p;", "D")]
    [InlineData("interface I { int Get(); } struct R : I { public int Get() { return 2; } ~R() { printf(\"D\"); } }", "I* p = new* R(); if (p.Get() != 2) return 1; p.~I(); Memory::Free((void*)raw);", "D", true)]
    public void InterfaceDestruction(string types, string body, string expected, bool needsRaw = false)
    {
        if (needsRaw) body = body.Replace("I* p = new* R();", "R* raw = new* R(); I* p = raw;");
        var result = CompilerTestHelper.Run("extern int printf(readonly(char)* text, ...);" + types + "int main() {" + body + "return 0;}");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expected, result.StandardOutput);
    }

    [Fact]
    public void ErasingAClassToAnInterfacePreservesItsDynamicDestructor()
    {
        var result = CompilerTestHelper.Run("""
            extern int printf(readonly(char)* text, ...);
            interface I {}
            class Base : I {}
            class Derived : Base { ~Derived() { printf("D"); } }
            int main() { Base* baseView = new* Derived(); I* view = baseView; delete view; return 0; }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("D", result.StandardOutput);
    }
    [Fact]
    public void ReferencePropertiesDispatchThroughInterfacesAndBaseClasses()
    {
        var result = CompilerTestHelper.Run("""
            interface I { ref int Value { immutable get; } }
            class Base : I { protected int n = 1; public virtual immutable ref int Value => n; }
            class Derived : Base { private int other = 2; public override immutable ref int Value => other; }
            int main() {
                Base* p = new* Derived();
                I* i = p;
                i.Value = 9;
                immutable(I)* r = i;
                if (r.Value != 9 || p.Value != 9) return 1;
                delete i;
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData("struct X { public ref int P => 1; }")]
    [InlineData("struct X { public ref int P { get { int x = 1; return x; } } }")]
    public void ReferenceGettersRejectObviousDanglingReturns(string source)
        => Assert.Throws<gflat.CompileExceptions.TypeCheckException>(() => CompilerTestHelper.Check(source));

    [Fact]
    public void UninitializedOwningLocalsHaveNoCleanup()
        => Assert.Equal("", CompilerTestHelper.Run(Resource + "int main() { R value; return 0; }").StandardOutput);
    [Theory]
    [InlineData("void Use(R value) {} int main() { R a = new R(1); Use(a); return 0; }")]
    [InlineData("int main() { immutable(R) a = new R(1); R b = move a; return 0; }")]
    [InlineData("class Base {} class Derived : Base { ~Derived() {} } void Use(Base value) {} int main() { Use(new Derived()); return 0; }")]
    public void OwnershipCannotBeCopiedOrSlicedImplicitly(string source)
        => Assert.Throws<gflat.CompileExceptions.TypeCheckException>(() => CompilerTestHelper.Check(Resource + source));
}
