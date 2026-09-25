using gflat.CompileExceptions;
using gflat.diagnostics;

namespace gflat.Tests;

public class MultiFileTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ForwardReferencesAndReopenedNamespaces(bool reverse)
    {
        SourceFile[] files = [new("main.gf", "int main() => Helpers::Answer();"),
            new("helper.gf", "namespace Helpers { int Answer() => value(); }"),
            new("value.gf", "namespace Helpers { int value() => 42; }")];
        Assert.Equal(42, CompilerTestHelper.Run(reverse ? files.Reverse() : files).ExitCode);
    }

    [Fact]
    public void ImportsAreLocalToTheirFile()
    {
        SourceFile[] files = [new("a.gf", "using Helpers; int a() => Answer();"),
            new("b.gf", "int main() => Answer();"), new("helper.gf", "namespace Helpers { int Answer() => 42; }")];
        var bag = new DiagnosticBag();
        Assert.Throws<TypeCheckException>(() => Compiler.Check(files, bag));
        Assert.Equal("b.gf", bag.Items.First(d => d.Severity == DiagnosticSeverity.Error).FilePath);
    }

    [Fact]
    public void PreludeAndAllocatorAreInjectedOnce()
    {
        string ir = Compiler.Emit([new("a.gf", "int main() => helper();"), new("b.gf", "int helper() => 42;")]);
        Assert.Equal(1, ir.Split("define i8* @gflat$Allocator$Allocate(").Length - 1);
        Assert.Equal(1, ir.Split("%Attribute = type").Length - 1);
    }

    [Theory]
    [InlineData("class C {}", "class C {}")]
    [InlineData("int value() => 1;", "int value() => 2;")]
    public void DuplicateDiagnosticsReferToBothFiles(string a, string b)
    {
        var bag = new DiagnosticBag();
        Assert.Throws<TypeCheckException>(() => Compiler.Check([new("a.gf", a), new("b.gf", b)], bag));
        var error = bag.Items.First(d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal("b.gf", error.FilePath);
        Assert.Contains(error.RelatedLocations, span => span.Source?.Path == "a.gf");
    }

    [Fact]
    public void WeakReplacementSpansFiles()
        => Assert.Equal(42, CompilerTestHelper.Run([new("a.gf", "weak int value() => missing(); int main() => value();"), new("b.gf", "replace int value() => 42;")]).ExitCode);

    [Fact]
    public void AllocatorReplacementSpansFiles()
        => Assert.Equal(42, CompilerTestHelper.Run([new("main.gf", "int main() { if (Allocator::Allocate(1u) == null) return 42; return 0; }"), new("alloc.gf", "namespace Allocator { public replace void*? Allocate(nuint size) => null; public replace void Free(void* p) {} }")]).ExitCode);

    [Fact]
    public void GenericBodyUsesDefinitionImports()
        => Assert.Equal(42, CompilerTestHelper.Run([new("a.gf", "int main() => identity<int>(1);"), new("b.gf", "using Helpers; T identity<T>(T x) { Answer(); return (T)42; }"), new("helper.gf", "namespace Helpers { int Answer() => 1; }")]).ExitCode);

    [Fact]
    public void GenericErrorsPointToDefinition()
    {
        var bag = new DiagnosticBag();
        Assert.Throws<TypeCheckException>(() => Compiler.Check([new("a.gf", "int main() => bad<int>(1);"), new("b.gf", "T bad<T>(T x) => missing;")], bag));
        Assert.Equal("b.gf", bag.Items.First(d => d.Severity == DiagnosticSeverity.Error).FilePath);
    }

    [Fact]
    public void EmptyOrRepeatedInputIsRejected()
    {
        Assert.Throws<ArgumentException>(() => Compiler.Check(Array.Empty<SourceFile>()));
        Assert.Throws<ArgumentException>(() => Compiler.Check([new("a.gf", ""), new("a.gf", "")]));
    }

    [Fact]
    public void NamespacedTypesWithSameNameRemainDistinct()
        => Assert.Equal(42, CompilerTestHelper.Run([
            new("a.gf", "namespace A { public class C { public int value; public C() { value = 20; } } }"),
            new("b.gf", "namespace B { public class C { public int value; public C() { value = 22; } } }"),
            new("main.gf", "int main() { A::C a = new A::C(); B::C b = new B::C(); return a.value + b.value; }")]).ExitCode);

    [Fact]
    public void NamespacedGenericFunctionsKeepIdentityAndLexicalContext()
        => Assert.Equal(42, CompilerTestHelper.Run([
            new("a.gf", "namespace A { int helper() => 20; T value<T>(T x) { helper(); return x; } }"),
            new("b.gf", "namespace B { int helper() => 22; T value<T>(T x) { helper(); return x; } }"),
            new("main.gf", "int main() => A::value<int>(20) + B::value<int>(22);")]).ExitCode);

    [Fact]
    public void NamespacedGenericTypesAndPrivateHelpersWork()
        => Assert.Equal(42, CompilerTestHelper.Run([
            new("box.gf", "namespace A { int helper() => 42; class Box<T> { public T value; public int answer() => helper(); } }"),
            new("main.gf", "using A; int main() { Box<int> b = new Box<int>(); return b.answer(); }")]).ExitCode);

    [Fact]
    public void TypesReturnedAcrossFilesRetainDefiningNamespace()
        => Assert.Equal(42, CompilerTestHelper.Run([
            new("library.gf", "namespace A { class C { public int value; public C() { value = 42; } } C make() => new C(); }"),
            new("main.gf", "int main() { A::C c = A::make(); return c.value; }")]).ExitCode);

    [Fact]
    public void AmbiguousTypeImportsAreDiagnosed()
        => Assert.Contains("ambiguous", Assert.Throws<TypeCheckException>(() => Compiler.Check([
            new("a.gf", "namespace A { class C {} }"), new("b.gf", "namespace B { class C {} }"),
            new("main.gf", "using A; using B; int main() { C c = new C(); return 0; }")])).Message);

    [Fact]
    public void ImportedAliasKeepsItsDefiningScope()
        => Assert.Equal(42, CompilerTestHelper.Run([
            new("lib.gf", "namespace A { class C { public int answer() => 42; } alias View = C; }"),
            new("main.gf", "using A; int main() { View v = new View(); return v.answer(); }")]).ExitCode);

    [Fact]
    public void CrossFileInheritanceAndInterfaceDispatch()
        => Assert.Equal(42, CompilerTestHelper.Run([
            new("base.gf", "namespace A { interface I { int answer(); } class Base { public virtual int answer() => 1; } }"),
            new("derived.gf", "using A; namespace B { class Derived : Base, I { public override int answer() => 42; } }"),
            new("main.gf", "int main() { B::Derived d = new B::Derived(); A::Base* b = &d; return b.answer(); }")]).ExitCode);

    [Fact]
    public void UnimportedTypesAreRejected()
        => Assert.Throws<TypeCheckException>(() => Compiler.Check([
            new("lib.gf", "namespace A { class C {} }"),
            new("main.gf", "int main() { C c; return 0; }")]));

    [Fact]
    public void InvalidBaseReportsTheDeclaringFile()
    {
        var bag = new DiagnosticBag();
        Assert.Throws<TypeCheckException>(() => Compiler.Check([
            new("main.gf", "int main() => 0;"), new("type.gf", "class C : Missing {}")], bag));
        Assert.Equal("type.gf", bag.Items.First(d => d.Severity == DiagnosticSeverity.Error).FilePath);
    }

    [Fact]
    public void ConstantsCanBeDeclaredInLaterFiles()
        => Assert.Equal(42, CompilerTestHelper.Run([
            new("a.gf", "int main() => Answer;"), new("z.gf", "const int Answer = 42;")]).ExitCode);

    [Fact]
    public void EnumIdentitiesAndImportsStaySeparate()
        => Assert.Equal(42, CompilerTestHelper.Run([
            new("a.gf", "namespace A { enum E { Value = 20 } }"),
            new("b.gf", "namespace B { enum E { Value = 22 } }"),
            new("main.gf", "using A; int main() => (int)E::Value + (int)B::E::Value;")]).ExitCode);

    [Fact]
    public void NestedReturnTypesBindBeforeCallsFromOtherFiles()
        => Assert.Equal(42, CompilerTestHelper.Run([
            new("lib.gf", "namespace Lib { class Outer { public class Inner { public int value; public Inner() { value = 42; } } public Inner make() => new Inner(); } }"),
            new("main.gf", "int main() { Lib::Outer o = new Lib::Outer(); Lib::Outer::Inner i = o.make(); return i.value; }")]).ExitCode);

    [Fact]
    public void GenericFunctionsDoNotInheritTheCallersClassScope()
        => Assert.Equal(42, CompilerTestHelper.Run([
            new("lib.gf", "namespace Lib { int helper() => 42; T value<T>(T x) => (T)helper(); }"),
            new("main.gf", "class Caller { public int helper() => 7; public int call() => Lib::value<int>(0); } int main() { Caller c = new Caller(); return c.call(); }")]).ExitCode);
}
