using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class DuplicateBaseTypeTests
{
    [Theory]
    [InlineData("interface I {} struct S : I, I {} int main()=>0;", "I")]
    [InlineData("interface I {} class S : I, I {} int main()=>0;", "I")]
    [InlineData("namespace App { interface I {} struct S : I, App::I {} } int main()=>0;", "App::I")]
    [InlineData("namespace App { interface I {} struct S<T> : I, App::I {} } int main(){App::S<int> value=new App::S<int>();return 0;}", "App::I")]
    [InlineData("static interface I {} static class S : I, I {} int main()=>0;", "I")]
    [InlineData("static interface I {} static class S<T> : I, I {public static int Read()=>0;} int main()=>S<int>::Read();", "I")]
    [InlineData("class Base {} class S : Base, Base {} int main()=>0;", "Base")]
    public void DuplicateDirectBasesAreDiagnosedBeforeEmission(string source, string type)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(source));
        Assert.Equal($"Duplicate base type '{type}'", error.Description);
    }

    [Fact]
    public void ExplicitInterfaceAlreadyInheritedFromBaseIsAllowed()
        => Assert.Equal(42, CompilerTestHelper.Run("""
            interface I { int Read(); }
            class Base : I { public virtual int Read()=>1; }
            class Derived : Base, I { public override int Read()=>42; }
            int main(){Derived value=new Derived();I* view=&value;return view.Read();}
            """).ExitCode);

    [Fact]
    public void DistinctInterfacesWithTheSameSimpleNameRemainDistinct()
        => Assert.Equal(42, CompilerTestHelper.Run("""
            namespace A {public interface I {int Read();}}
            namespace B {public interface I {int Read();}}
            struct S : A::I, B::I {public int Read()=>42;}
            int main(){S value=new S();A::I* a=&value;B::I* b=&value;return a.Read()+b.Read()-42;}
            """).ExitCode);
}
