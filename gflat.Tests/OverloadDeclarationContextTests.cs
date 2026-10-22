using gflat.diagnostics;

namespace gflat.Tests;

public sealed class OverloadDeclarationContextTests
{
    [Theory]
    [InlineData("class", false, false)]
    [InlineData("class", true, false)]
    [InlineData("class", false, true)]
    [InlineData("struct", false, false)]
    [InlineData("struct", true, false)]
    [InlineData("struct", false, true)]
    public void GenericInterfaceOverloadsUseDeclarationAliases(string kind, bool reverse, bool localShadow)
    {
        const string integer = "public int Read(Word value)=>23;";
        const string wide = "public int Read(long value)=>24;";
        string source = $$"""
            namespace Contract { interface I { int Read(int value); } }
            namespace Library {
                alias Word=int;
                {{kind}} Box<T>:Contract::I { {{(reverse ? wide + integer : integer + wide)}} }
            }
            {{(localShadow ? "" : "alias Word=long;")}}
            int main() {
                {{(localShadow ? "alias Word=long;" : "")}}
                Library::Box<int> item=new Library::Box<int>();
                Word callerValue=1L;
                if (callerValue != 1L) return 3;
                Contract::I* view=&item;
                if (view.Read(0) != 23) return 1;
                if (item.Read(0L) != 24) return 2;
                return 0;
            }
            """;
        AssertValid(source);
        Assert.Equal(0, CompilerTestHelper.Run(source).ExitCode);
    }

    [Theory]
    [InlineData("class", false)]
    [InlineData("class", true)]
    [InlineData("struct", false)]
    [InlineData("struct", true)]
    public void ConstructorsUseConcreteOwnerDeclarationContext(string kind, bool reverse)
    {
        const string integer = "public Box(Word value){ result=23; }";
        const string wide = "public Box(long value){ result=24; }";
        string source = $$"""
            namespace Library {
                alias Word=int;
                {{kind}} Box<T> { public int result; {{(reverse ? wide + integer : integer + wide)}} }
            }
            alias Word=long;
            int main() {
                Library::Box<int> a=new Library::Box<int>(0);
                Library::Box<int> b=new Library::Box<int>(0L);
                if (a.result != 23) return 1;
                if (b.result != 24) return 2;
                return 0;
            }
            """;
        AssertValid(source);
        Assert.Equal(0, CompilerTestHelper.Run(source).ExitCode);
    }

    [Fact]
    public void GenericSubstitutionsRemainDistinctFromNamespaceAliases()
    {
        const string source = """
            namespace Library {
                alias Word=int;
                class Box<T> { public int Read(Word value)=>23; public int Read(T value)=>24; }
            }
            alias Word=long;
            int main() {
                Library::Box<long> item=new Library::Box<long>();
                return item.Read(0)+item.Read(0L);
            }
            """;
        AssertValid(source);
        Assert.Equal(47, CompilerTestHelper.Run(source).ExitCode);
    }

    [Fact]
    public void NestedConcreteOwnerTypesAreVisibleWhilePreparingOverloads()
    {
        const string source = """
            namespace Library {
                class Owner {
                    public struct Word {}
                    public class Box<T> {
                        public int Read(Word value)=>23;
                        public int Read(long value)=>24;
                    }
                }
            }
            int main() {
                alias Word=long;
                Library::Owner::Box<int> item=new Library::Owner::Box<int>();
                return item.Read(new Library::Owner::Word())+item.Read(0L);
            }
            """;
        AssertValid(source);
        Assert.Equal(47, CompilerTestHelper.Run(source).ExitCode);
    }

    [Fact]
    public void GenericStaticMethodFunctionPointersUseConsistentOverloadSymbols()
    {
        const string source = """
            namespace Library {
                alias Word=int;
                class Box<T> { public static int Read(int value)=>23; public static int Read(long value)=>24; }
            }
            alias Word=long;
            int main() {
                int(int)* narrow=&Library::Box<int>::Read;
                int(long)* wide=&Library::Box<int>::Read;
                return narrow(0)+wide(0L);
            }
            """;
        AssertValid(source);
        Assert.Equal(47, CompilerTestHelper.Run(source).ExitCode);
    }

    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    public void ConstructorSignatureAliasesMayRegisterAnotherSpecialization(string kind)
    {
        string source = $$"""
            namespace Library {
                struct Cell<T> { public T value; }
                alias Payload=Cell<int>;
                {{kind}} Box<T> {
                    public int result;
                    public Box(Payload value){result=23;}
                    public Box(long value){result=24;}
                }
            }
            int main() {
                Library::Box<int> item=new Library::Box<int>(new Library::Cell<int>());
                return item.result;
            }
            """;
        AssertValid(source);
        Assert.Equal(23, CompilerTestHelper.Run(source).ExitCode);
    }

    [Theory]
    [InlineData("class", false)]
    [InlineData("class", true)]
    [InlineData("struct", false)]
    [InlineData("struct", true)]
    public void AliasesCannotHideReadonlyOnlyMethodOverloads(string kind, bool shadow)
    {
        string source = $$"""
            namespace Library {
                alias Ptr=int*;
                {{kind}} Box<T> { public int Read(Ptr value)=>23; public int Read(readonly(int)* value)=>24; }
            }
            {{(shadow ? "alias Ptr=long*;" : "")}}
            int main() { Library::Box<int> item=new Library::Box<int>(); return 0; }
            """;
        var errors = Analyze(source).Where(d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains(errors, d => d.Message == "Overloads of 'Read' cannot differ only by readonly, immutable, return type, or declaration modifiers");
    }

    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    public void AliasesCannotHideReadonlyOnlyConstructorOverloads(string kind)
    {
        string source = $$"""
            namespace Library {
                alias Ptr=int*;
                {{kind}} Box<T> { public Box(Ptr value){} public Box(readonly(int)* value){} }
            }
            alias Ptr=long*;
            int main() { int value=0; Library::Box<int> item=new Library::Box<int>(&value); return 0; }
            """;
        var errors = Analyze(source).Where(d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains(errors, d => d.Message == "Constructors cannot differ only by readonly, immutable, or parameter modifiers");
    }

    private static IReadOnlyList<Diagnostic> Analyze(string source) =>
        Compiler.Analyze([new SourceFile("overload-context.gf", source)], new() { NoStdLib = true });

    private static void AssertValid(string source) =>
        Assert.DoesNotContain(Analyze(source), d => d.Severity == DiagnosticSeverity.Error);
}
