using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class InlineLayoutCycleTests
{
    [Theory]
    [InlineData("struct Loop { Loop value; }")]
    [InlineData("struct A { B value; } struct B { A value; }")]
    [InlineData("struct A { B value; } class B { A value; }")]
    [InlineData("struct Loop { Loop[1] values; }")]
    [InlineData("class Base { Derived value; } class Derived : Base {}")]
    [InlineData("alias Self = Loop; struct Loop { Self value; }")]
    [InlineData("namespace N { alias Self = Loop; struct Loop { Self value; } }")]
    public void UnusedInfiniteValuesAreRejectedBeforeLifetimeAnalysis(string declaration)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(declaration + " int main()=>0;"));
        Assert.Contains("Inline value layout is recursive", error.Description);
        Assert.True(error.Span.Length > 0);
    }

    [Theory]
    [InlineData("sizeof(Loop)")]
    [InlineData("alignof(Loop)")]
    public void RecursiveLayoutQueriesAreDiagnosed(string query)
    {
        Assert.Throws<TypeCheckException>(() => Compiler.Check(
            "struct Loop { int[" + query + "] value; } int main()=>0;"));
    }

    [Fact]
    public void CycleDiagnosticIdentifiesClosingField()
    {
        const string text = "struct A { B child; } struct B { A parent; }";
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(new SourceFile("cycle.gf", text)));
        Assert.Contains("A.child -> B.parent -> A", error.Description);
        Assert.Equal("A parent;", text.Substring(error.Span.Start, error.Span.Length));
    }

    [Fact]
    public void ExpandingDistinctSpecializationsHaveBoundedDiagnostic()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check("""
            struct A<T> { public B<T*> child; }
            struct B<T> { public A<T*> child; }
            int main() => sizeof(A<int>);
            """));
        Assert.Contains("compiler nesting limit of 64", error.Description);
        Assert.True(error.Line > 0);
    }

    [Fact]
    public void IndirectAndStaticRecursionAndFiniteGenericValuesRemainValid()
    {
        Assert.Equal(0, CompilerTestHelper.Run("""
            struct Raw { public Raw*? next; }
            class Managed { public Managed^? next; }
            struct Static { public static Static singleton; public int value; }
            struct Box<T> { public T value; }
            int main() {
                Box<Box<int>> finite = default;
                finite.value.value = 42;
                if (finite.value.value != 42 || sizeof(Raw) != sizeof(void*)) return 1;
                if (sizeof(Managed^) != sizeof(void*) || sizeof(Static) != sizeof(int)) return 2;
                return 0;
            }
            """).ExitCode);
    }

    [Fact]
    public void SpecializationLimitCountsNestedWorkRatherThanAllInstantiations()
    {
        var declarations = new System.Text.StringBuilder("struct Box<T> { public T value; } ");
        for (int i = 1; i <= 80; i++) declarations.Append($"Box<int[{i}]>*? p{i}; ");
        Compiler.Check(declarations + " int main()=>0;");
    }
}
