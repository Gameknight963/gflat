using gflat.diagnostics;

namespace gflat.Tests;

public sealed class RemainingDiagnosticPresentationTests
{
    [Theory]
    [InlineData("namespace Lib{enum E{A}} int main()=>Lib::E::Missing;", "Enum 'Lib::E' does not contain member 'Missing'", "Lib::E::Missing")]
    [InlineData("namespace Lib{enum E{A}} int main()=>Lib::E.A;", "Enum member 'Lib::E::A' must be accessed with '::', not '.'", "Lib::E.A")]
    [InlineData("namespace Lib{enum E{A}} int main()=>Lib::E.A();", "Enum member 'Lib::E::A' must be accessed with '::', not '.'", "Lib::E.A")]
    [InlineData("namespace Lib{class Base{public Base(int x){}} class Child:Base{}} int main(){Lib::Child value=new Lib::Child();return 0;}", "Base class 'Lib::Base' requires an explicit constructor call", "new Lib::Child()")]
    [InlineData("namespace Lib{struct Box<T>{public T* item;}} int main(){Lib::Box<int> value=new Lib::Box<int>();return 0;}", "Field 'Lib::Box<int>.item' requires an initializer or constructor assignment", "new Lib::Box<int>()")]
    [InlineData("namespace Lib{struct A{} struct B{}} void F(Lib::A* p){p.~Lib::B();} int main()=>0;", "Destructor type 'Lib::B' must match the pointed-to type 'Lib::A'", "p.~Lib::B()")]
    [InlineData("namespace Lib{enum E{A}} int main(){Lib::E value=Lib::E::A;return value[0];}", "Cannot index non-array and non-pointer type 'Lib::E'", "value[0]")]
    [InlineData("namespace Lib{interface I{}} void F(Lib::I* value){value[0];} int main()=>0;", "Interface 'Lib::I' has no indexer", "value[0]")]
    [InlineData("namespace Lib{struct Box<T>{public T value;}} int main(){Lib::Box<int> a=new Lib::Box<int>();return (int)a;}", "Cannot cast 'Lib::Box<int>' to 'int'", "(int)a")]
    [InlineData("namespace Lib{struct Family<T>{public struct Item{public T value;}}} int main(){immutable(Lib::Family<int>::Item) a=new Lib::Family<int>::Item();return (int)a;}", "Cannot cast 'immutable(Lib::Family<int>::Item)' to 'int'", "(int)a")]
    [InlineData("namespace Lib{int F<T>()=>0;} const int value=Lib::F<int>(); int main()=>0;", "Const variable 'value' initializer must be a compile-time constant: Function 'Lib::F<int>' is not marked 'const' and cannot be evaluated at compile time", "const int value=Lib::F<int>();")]
    [InlineData("namespace Lib{struct Box<T>{public int Read()=>0;}} const int F(){Lib::Box<int> value=new Lib::Box<int>();return value.Read();} const int result=F(); int main()=>0;", "Const variable 'result' initializer must be a compile-time constant: Function 'Lib::Box<int>::Read' is not marked 'const' and cannot be evaluated at compile time", "const int result=F();")]
    public void DiagnosticsKeepReadableTypesAndOriginalExpressionSpans(string source, string expected, string offending)
    {
        var diagnostics = Compiler.Analyze([new SourceFile("remaining-presentation.gf", source)], new() { NoStdLib = true });
        var error = Assert.Single(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(expected, error.Message);
        Assert.DoesNotContain('$', error.Message);
        Assert.Equal("remaining-presentation.gf", error.FilePath);
        Assert.Equal(offending, source.Substring(error.Span.Start, error.Span.Length));
    }
}
