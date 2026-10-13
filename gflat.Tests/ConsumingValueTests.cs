namespace gflat.Tests;

public class ConsumingValueTests
{
    private const string Resource = """
        extern int printf(readonly(char)* text, ...);
        struct R {
            public int n;
            public R(int value) { n = value; }
            ~R() { printf(c"%d", n); }
        }
        """;

    [Theory]
    [InlineData("R a = new R(1); R b = move a; return a.n;")]
    [InlineData("R a = new R(1); R b = move a; R c = move a; return 0;")]
    [InlineData("R a = new R(1); for (int i = 0; i < 2; i++) { R b = move a; } return 0;")]
    [InlineData("R a = new R(1); for (int i = 0; i < 2; i++) { R b = move a; continue; } return 0;")]
    [InlineData("R a = new R(1); R b = move a; a.n = 2; return 0;")]
    [InlineData("R a = new R(1); defer printf(c\"%d\", a.n); R b = move a; return 0;")]
    [InlineData("R a = new R(1); try { R b = move a; throw new Exception(); } catch { return a.n; } return 0;")]
    [InlineData("Box b = new Box(); R x = move b.item; return 0;")]
    public void MovedSourcesCannotBeRead(string body)
        => Assert.Throws<gflat.CompileExceptions.TypeCheckException>(() => CompilerTestHelper.Check(Resource + "struct Box { public R item = new R(1); } int main() {" + body + "}"));

    [Theory]
    [InlineData("R a = new R(1); { R b = move a; } a = new R(2); return 0;", "12")]
    [InlineData("R a = new R(1); for (int i = 0; i < 2; i++) { R b = move a; a = new R(i + 2); } return 0;", "123")]
    [InlineData("R a = new R(1); if (true) { R b = move a; } return 0;", "1")]
    [InlineData("R a = new R(1); if (false) { R b = move a; } a = new R(2); return 0;", "12")]
    [InlineData("R a = new R(1); try { R b = move a; throw new Exception(); } catch { a = new R(2); } return 0;", "12")]
    [InlineData("R a; a = new R(1); R b = move a; return 0;", "1")]
    public void CleanupFollowsInitialization(string body, string expected)
    {
        var result = CompilerTestHelper.Run(Resource + "int main() {" + body + "}");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expected, result.StandardOutput);
    }

    private const string Copyable = """
        extern int printf(readonly(char)* text, ...);
        struct R {
            public int n;
            public R(int value) { n = value; }
            public copy R(readonly(R)* source) {
                if (source.n == 9) throw new Exception();
                n = source.n;
                printf(c"C%d", n);
            }
            ~R() { printf(c"D%d", n); }
        }
        """;

    [Theory]
    [InlineData("R a = new R(1); R b = a; b.n = 2; return 0;", "C1D2D1")]
    [InlineData("R a = new R(1); R b = move a; return 0;", "D1")]
    [InlineData("R a = new R(1); R b = new R(2); b = a; return 0;", "C1D2D1D1")]
    [InlineData("R a = new R(1); a = a; return 0;", "C1D1D1")]
    [InlineData("R a = new R(9); R b = new R(2); try { b = a; } catch {} return 0;", "D2D9")]
    [InlineData("R[2] a = [new R(1), new R(9)]; try { R[2] b = a; } catch {} return 0;", "C1D1D9D1")]
    [InlineData("Pair a = new Pair(); try { Pair b = a; } catch {} return 0;", "C1D1D9D1")]
    public void CopyingDuplicatesAndCleansPartialResults(string body, string expected)
    {
        var result = CompilerTestHelper.Run(Copyable + "struct Pair { public R a; public R b; public Pair() { a = new R(1); b = new R(9); } } int main() {" + body + "}");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expected, result.StandardOutput);
    }

    [Fact]
    public void CopyArgumentsAndBorrowedReturnsUseCopyConstructor()
    {
        var result = CompilerTestHelper.Run(Copyable + """
            void Use(R value) { value.n = 2; }
            R Read(readonly(R)* value) { return *value; }
            int main() { R a = new R(1); Use(a); R b = Read(&a); return 0; }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("C1D2C1D1D1", result.StandardOutput);
    }

    [Theory]
    [InlineData("Bad b = new Bad();")]
    [InlineData("Bad* b = new* Bad();")]
    public void ThrowingConstructorDestroysOnlyCompletedFields(string creation)
    {
        var result = CompilerTestHelper.Run(Resource + """
            struct Bad {
                R a; R b;
                public Bad() { a = new R(1); throw new Exception(); }
                ~Bad() { printf(c"BAD"); }
            }
            int main() { try {
            """ + creation + "} catch {} return 0; }");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("1", result.StandardOutput);
    }
    [Theory]
    [InlineData("struct Holder { R item; public Holder() { item = new R(1); throw new Exception(); } }", "Holder a = new Holder();", "D1")]
    [InlineData("struct Holder { R item = new R(1); public Holder() { throw new Exception(); } }", "Holder a = new Holder();", "D1")]
    [InlineData("class Base { R item; public Base() { item = new R(1); } } class Derived : Base { R other; public Derived() { other = new R(2); throw new Exception(); } }", "Derived a = new Derived();", "D2D1")]
    [InlineData("class Base { R item; public Base() { item = new R(1); throw new Exception(); } } class Derived : Base { public Derived() {} }", "Derived a = new Derived();", "D1")]
    [InlineData("struct Holder { R item; public Holder(R value) { throw new Exception(); } }", "Holder a = new Holder(new R(1));", "D1")]
    [InlineData("struct Holder { R item; public Holder() { item = new R(1); } public copy Holder(readonly(Holder)* source) { item = source.item; throw new Exception(); } }", "Holder a = new Holder(); Holder b = a;", "C1D1D1")]
    public void ConstructionFailureCleansCompletedOwners(string types, string body, string expected)
    {
        var result = CompilerTestHelper.Run(Copyable + types + "int main() { try {" + body + "} catch {} return 0; }");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expected, result.StandardOutput);
    }

    [Fact]
    public void ClassCopyPreservesBaseCopyAndDerivedDispatch()
    {
        var result = CompilerTestHelper.Run(Copyable + """
            class Base { public R r; public Base() { r = new R(1); } public virtual int Get() { return 1; } }
            class Derived : Base { public override int Get() { return 2; } }
            int main() { Derived a = new Derived(); Derived b = a; if (b.Get() != 2) return 1; return 0; }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("C1D1D1", result.StandardOutput);
    }

    [Fact]
    public void ForeachCopiesAndConsumesEachIterationIndependently()
    {
        var result = CompilerTestHelper.Run(Copyable + """
            int main() {
                R[2] values = [new R(1), new R(2)];
                foreach (R item in values) { R taken = move item; continue; }
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("C1D1C2D2D2D1", result.StandardOutput);
    }

    [Fact]
    public void FailedConstructorFreesOnlyFreshHeapStorage()
    {
        var result = CompilerTestHelper.Run("""
            extern int printf(readonly(char)* text, ...);
            extern void* malloc(nuint size);
            extern void free(void* p);
            namespace Memory {
                public replace void*? Allocate(nuint size) { return malloc(size); }
                public replace void Free(void* p) { printf(c"F"); free(p); }
            }
            struct Bad { public Bad(Exception* failure) { throw *failure; } }
            int main() {
                Exception error = new Exception();
                try { Bad* value = new* Bad(&error); } catch {}
                Bad* storage = (Bad*)Memory::Allocate((nuint)sizeof(Bad))!;
                try { new* Bad(&error) at storage; } catch {}
                printf(c"P");
                Memory::Free((void*)storage);
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("FFFPF", result.StandardOutput);
    }

    [Theory]
    [InlineData("void Fail() { throw new Exception(); }", "R a = new R(1); try { R b = move a; Fail(); } catch { return a.n; } return 0;")]
    [InlineData("", "R a = new R(1); R bad = new R(9); try { R b = move a; R c = bad; } catch { return a.n; } return 0;")]
    [InlineData("struct Bad { public Bad() { throw new Exception(); } }", "R a = new R(1); try { R b = move a; Bad c = new Bad(); } catch { return a.n; } return 0;")]
    public void ExceptionalPathsDoNotResurrectMovedLocals(string declarations, string body)
        => Assert.Throws<gflat.CompileExceptions.TypeCheckException>(() => CompilerTestHelper.Check(Copyable + declarations + "int main() {" + body + "}"));

    [Fact]
    public void InheritedCopyConstructorsWithoutDestructorsAreNotSkipped()
    {
        var result = CompilerTestHelper.Run("""
            class Base { public int n; public Base() { n = 1; } public copy Base(readonly(Base)* p) { n = p.n + 1; } }
            class Derived : Base {}
            int main() { Derived a = new Derived(); Derived b = a; return b.n; }
            """);
        Assert.Equal(2, result.ExitCode);
    }

    [Theory]
    [InlineData("struct Secret { private copy Secret(readonly(Secret)* p) {} public Secret() {} }", "Secret a = new Secret(); Secret b = a;")]
    [InlineData("struct Secret { private copy Secret(readonly(Secret)* p) {} public Secret() {} }", "Secret a = new Secret(); Secret b = new Secret(&a);")]
    [InlineData("struct Secret { private copy Secret(readonly(Secret)* p) {} public Secret() {} } struct Holder { Secret s = new Secret(); }", "Holder a = new Holder(); Holder b = a;")]
    [InlineData("struct S { public copy S(S* p) {} }", "")]
    [InlineData("struct S { public copy S(readonly(S)*? p) {} }", "")]
    [InlineData("struct S { public copy S() {} }", "")]
    public void CopyContractsAreChecked(string types, string body)
        => Assert.Throws<gflat.CompileExceptions.TypeCheckException>(() => CompilerTestHelper.Check(types + "int main() {" + body + "return 0;}"));

    [Fact]
    public void EmptyValuesStillBecomeUninitializedWhenMoved()
        => Assert.Throws<gflat.CompileExceptions.TypeCheckException>(() => CompilerTestHelper.Check("struct E {} int main() { E a = new E(); E b = move a; E c = a; return 0; }"));

    [Fact]
    public void RawStorageRelocationDoesNotRunDestructorsOrCopyConstructors()
    {
        var result = CompilerTestHelper.Run(Copyable + """
            int main() {
                R* original = new* R(1);
                R* next = (R*)Memory::Allocate((nuint)sizeof(R))!;
                move *original at next;
                Memory::Free((void*)original);
                delete next;
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("D1", result.StandardOutput);
    }

}
