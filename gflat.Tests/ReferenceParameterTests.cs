using gflat.CompileExceptions;

namespace gflat.Tests;

public class ReferenceParameterTests
{
    [Theory]
    [InlineData("void F(ref R borrowed) { R owned = move borrowed; }")]
    [InlineData("struct Box { public Box(ref R borrowed) { R owned = move borrowed; } }")]
    [InlineData("void F() { void(ref R)* action = (ref R borrowed) => { R owned = move borrowed; }; }")]
    [InlineData("void Consume(ref R borrowed) { R owned = move borrowed; } extension R { public void AttemptConsume() { Consume(ref *this); } }")]
    [InlineData("void F<T>(ref T borrowed) { T owned = move borrowed; } int main() { R value = new R(65); F(ref value); return 0; }")]
    public void OwnedValuesCannotBeConsumedThroughBorrowedParameterBindings(string declaration)
    {
        var error = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(OwnerDeclaration + declaration));
        Assert.Contains("Cannot consume a borrowed ref parameter", error.Message);
    }

    [Theory]
    [InlineData("void F(ref R borrowed) { { R borrowed = new R(66); R owned = move borrowed; } }", "R value = new R(65); F(ref value);", "BA")]
    [InlineData("void F(R owned) { R other = move owned; }", "R value = new R(65); F(move value);", "A")]
    public void MoveUsesTheBoundLocalOrOwnedParameterRatherThanAnOuterBorrow(string declaration, string body, string output)
    {
        var result = CompilerTestHelper.Run(OwnerDeclaration + declaration + " int main() { " + body + " return 0; }");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(output, result.StandardOutput);
    }

    private const string OwnerDeclaration = """
        extern int putchar(int value);
        struct R { private int value; public R(int n) { value = n; } ~R() { putchar(value); } }
        """;

    [Theory]
    [InlineData("void F(ref int n) { n += 1; }", "int n = 41; F(ref n); return n;")]
    [InlineData("int F(ref readonly(int) n) => n;", "int n = 42; return F(ref n);")]
    [InlineData("void F<T>(ref T a, ref T b) { T temporary = a; a = b; b = temporary; }", "int a = 1; int b = 42; F(ref a, ref b); return a;")]
    [InlineData("void F(ref int n) { n = 42; }", "int n = 0; void(ref int)* f = &F; f(ref n); return n;")]
    [InlineData("struct S { public void F(ref int n) { n = 42; } }", "int n = 0; S s = new S(); s.F(ref n); return n;")]
    [InlineData("class S { public S(ref int n) { n = 42; } }", "int n = 0; S s = new S(ref n); return n;")]
    [InlineData("interface I { void F(ref int n); } struct S : I { public void F(ref int n) { n = 42; } }", "int n = 0; S s = new S(); I* p = &s; p.F(ref n); return n;")]
    [InlineData("class B { public virtual void F(ref int n) { n = 1; } } class S : B { public override void F(ref int n) { n = 42; } }", "int n = 0; S s = new S(); B* p = &s; p.F(ref n); return n;")]
    [InlineData("void F(ref int n) { n = 42; }", "int[2] n = [1, 2]; F(ref n[1]); return n[1];")]
    [InlineData("void F(ref int[2] n) { n[1] = 42; }", "int[2] n = [1, 2]; F(ref n); return n[1];")]
    [InlineData("struct S { public int n; } void F(ref int n) { n = 42; }", "S s = new S(); F(ref s.n); return s.n;")]
    [InlineData("", "int n = 0; void(ref int)* f = (ref int v) => { v = 42; }; f(ref n); return n;")]
    public void ReferencesAliasCallerStorage(string declarations, string body)
        => Assert.Equal(42, CompilerTestHelper.Run(declarations + " int main() { " + body + " }").ExitCode);

    [Fact]
    public void BorrowDoesNotCopyOrDestroyAnOwningValue()
    {
        var result = CompilerTestHelper.Run("""
            extern int putchar(int c);
            struct R { public int n; public ~R() { putchar(68); } }
            void F(ref R r) { r.n = 42; }
            int main() { R r = new R(); F(ref r); putchar(65); return r.n; }
            """);
        Assert.Equal(42, result.ExitCode);
        Assert.Equal("AD", result.StandardOutput);
    }

    [Fact]
    public void ReplacingBorrowedOwnerDestroysOldValueButLeavesNewValueWithCaller()
    {
        var result = CompilerTestHelper.Run("""
            extern int putchar(int c);
            struct R { public int n; public R(int x) { n = x; } public ~R() { putchar(n); } }
            void Replace(ref R r) { r = new R(66); throw new Exception(); }
            int main() { R r = new R(65); try { Replace(ref r); } catch { putchar(67); } return 0; }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("ACB", result.StandardOutput);
    }

    [Fact]
    public void RefIndexerCanBePassedAsReference()
        => Assert.Equal(42, CompilerTestHelper.Run("""
            struct S { int n; public ref int this[int index] { get => n; } }
            void Set(ref int n) { n = 42; }
            int main() { S s = new S(); Set(ref s[0]); return s[0]; }
            """).ExitCode);

    [Fact]
    public void RefCatchBorrowsTheOriginalAndRethrowPreservesDynamicType()
    {
        Assert.Equal(42, CompilerTestHelper.Run("""
            class E : Exception { public int n; public E() { n = 41; } }
            int main() {
                try {
                    try { throw new E(); }
                    catch (ref Exception e) { e.code = 1; throw; }
                } catch (ref E e) { return e.n + e.code; }
                return 0;
            }
            """).ExitCode);
    }

    [Theory]
    [InlineData("void F(ref int n) {} int main() { F(ref 42); return 0; }")]
    [InlineData("void F(ref int n) {} int main() { int n = 0; F(n); return 0; }")]
    [InlineData("void F(int n) {} int main() { int n = 0; F(ref n); return 0; }")]
    [InlineData("void F(ref long n) {} int main() { int n = 0; F(ref n); return 0; }")]
    [InlineData("void F(ref int n) {} int main() { readonly(int) n = 0; F(ref n); return 0; }")]
    [InlineData("void F(ref readonly(int) n) { n = 42; }")]
    [InlineData("void F(ref int n) {} int main() { int n; F(ref n); return 0; }")]
    [InlineData("void F(ref int n) { int moved = move n; }")]
    [InlineData("struct S { public int P { get => 1; } } void F(ref int n) {} int main() { S s = new S(); F(ref s.P); return 0; }")]
    [InlineData("struct S { public int n; } void F(ref int n) {} int main() { F(ref new S().n); return 0; }")]
    [InlineData("void F(ref int n) {} int main() { void(int)* f = &F; return 0; }")]
    [InlineData("int main() { try { throw new Exception(); } catch(ref Exception e) { delete e; } return 0; }")]
    [InlineData("int main() { try { throw new Exception(); } catch(ref Exception e) { Exception other = move e; } return 0; }")]
    public void InvalidReferenceUsesAreDiagnosed(string code)
        => Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
}
