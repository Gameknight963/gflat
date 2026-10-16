using gflat.ast;
using gflat.diagnostics;
using gflat.symbols;

namespace gflat.Tests;

public sealed class HierarchyDiagnosticNamesTests
{
    [Theory]
    [InlineData(
        "namespace Game { abstract class Base { public abstract int Read(); } class Derived : Base {} } int main()=>0;",
        "Class 'Game::Derived' must implement abstract method 'Game::Base.Read' or be declared abstract")]
    [InlineData(
        "namespace Game { class Base { public int value; } class Derived : Base { public int value; } } int main()=>0;",
        "Class 'Game::Derived' cannot declare field 'value' because it is already declared in a base class")]
    [InlineData(
        "namespace Game { class A {} class B {} class D : A, B {} } int main()=>0;",
        "Class 'Game::D' cannot inherit from multiple classes ('Game::A' and 'Game::B')")]
    [InlineData(
        "namespace Game { static class Base {} class Derived : Base {} } int main()=>0;",
        "Cannot inherit from static class 'Game::Base'")]
    [InlineData(
        "namespace Game { class Thing { public override int Read()=>0; } } int main()=>0;",
        "Method 'Read' in class 'Game::Thing' is marked override but does not override any virtual or abstract method in a base class")]
    [InlineData(
        "namespace Game { class Base { public virtual int Read()=>0; } class Derived : Base { public virtual int Read()=>0; } } int main()=>0;",
        "Method 'Read' in class 'Game::Derived' hides base virtual method without 'override' keyword")]
    [InlineData(
        "namespace Game { class Outer<T> { public class Inner { public override int Read()=>0; } } } int main() { Game::Outer<int>::Inner value = new Game::Outer<int>::Inner(); return 0; }",
        "Method 'Read' in class 'Game::Outer<int>::Inner' is marked override but does not override any virtual or abstract method in a base class")]
    [InlineData(
        "namespace Game { struct Box<T> { public T value; } class Base { public virtual Game::Box<int> Read()=>new Game::Box<int>(); } class Derived<T> : Base { public override Game::Box<T> Read()=>new Game::Box<T>(); } } int main() { Game::Derived<long> item = new Game::Derived<long>(); return 0; }",
        "Overriding method 'Read' in class 'Game::Derived<long>' has return type 'Game::Box<long>' which does not match base method return type 'Game::Box<int>'")]
    [InlineData(
        "namespace Game { struct Box<T> { public T value; } class Base { public virtual int Read(Game::Box<int> value)=>0; } class Derived<T> : Base { public override int Read(Game::Box<T> value)=>0; } } int main() { Game::Derived<long> item = new Game::Derived<long>(); return 0; }",
        "Parameter 'value' of overriding method 'Read' has type 'Game::Box<long>' which does not match base parameter type 'Game::Box<int>'")]
    public void RawCompilerDiagnosticsUseSourceTypeNames(string source, string expected)
    {
        var diagnostics = Compiler.Analyze([new SourceFile("hierarchy.gf", source)], new() { NoStdLib = true });
        var error = Assert.Single(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal(expected, error.Message);
        Assert.DoesNotContain("$", error.Message);
    }

    [Fact]
    public void StandaloneSymbolTableFormattingDoesNotRequireCompilerCallbacks()
    {
        var symbols = new SymbolTable();
        Assert.Equal("Owner", symbols.DisplayName("Owner"));
        Assert.Equal("int*", symbols.DisplayTypeName(new PointerTypeExpression(new NamedTypeExpression("int", null, 0), false, 0)));
        symbols.NameFormatter = name => "source:" + name;
        symbols.TypeFormatter = _ => "source-type";
        Assert.Equal("source:Owner", symbols.DisplayName("Owner"));
        Assert.Equal("source-type", symbols.DisplayTypeName(new NamedTypeExpression("int", null, 0)));
    }
}
