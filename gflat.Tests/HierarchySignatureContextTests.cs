using gflat.ast;
using gflat.diagnostics;
using gflat.symbols;

namespace gflat.Tests;

public sealed class HierarchySignatureContextTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("alias Word=long;", "")]
    [InlineData("", "alias Word=long;")]
    public void GenericOverridesUseTheirDeclarationAliases(string globalAlias, string localAlias)
    {
        string source = $$"""
            namespace Library {
                alias Word=int;
                class Base { public virtual Word Read(Word value)=>value; }
                class Box<T>:Base { public override Word Read(Word value)=>value; }
            }
            {{globalAlias}}
            int main() {
                {{localAlias}}
                Library::Box<int> item=new Library::Box<int>();
                Library::Base* view=&item;
                return view.Read(23);
            }
            """;
        AssertValid(source);
        Assert.Equal(23, CompilerTestHelper.Run(source).ExitCode);
    }

    [Fact]
    public void OverridesAndBaseMethodsUseIndependentNamespaces()
    {
        const string source = """
            namespace First {
                alias Word=int;
                class Base { public virtual Word Read(Word value)=>value; }
            }
            namespace Second {
                alias Word=int;
                class Box<T>:First::Base { public override Word Read(Word value)=>value; }
            }
            namespace Caller {
                alias Word=long;
                int Run() {
                    Second::Box<int> item=new Second::Box<int>();
                    First::Base* view=&item;
                    return view.Read(23);
                }
            }
            int main()=>Caller::Run();
            """;
        AssertValid(source);
        Assert.Equal(23, CompilerTestHelper.Run(source).ExitCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IncompatibleNamespaceAliasesCannotBeHiddenByCallerAliases(bool parameter)
    {
        string baseMethod = parameter ? "public virtual int Read(Word value)=>0;" : "public virtual Word Read()=>0;";
        string childMethod = parameter ? "public override int Read(Word value)=>0;" : "public override Word Read()=>0;";
        string source = $$"""
            namespace First { alias Word=int; class Base { {{baseMethod}} } }
            namespace Second { alias Word=long; class Box<T>:First::Base { {{childMethod}} } }
            alias Word=int;
            int main() { Second::Box<int> item=new Second::Box<int>(); return 0; }
            """;
        var errors = Analyze(source).Where(d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains(errors, d => d.Message == (parameter
            ? "Parameter 'value' of overriding method 'Read' has type 'long' which does not match base parameter type 'int'"
            : "Overriding method 'Read' in class 'Second::Box<int>' has return type 'long' which does not match base method return type 'int'"));
    }

    [Fact]
    public void ConcreteGenericSubstitutionAndNamespaceAliasBothSurvive()
    {
        const string source = """
            namespace Library {
                alias Word=int;
                class Base { public virtual int Read(Word value)=>value; }
                class Box<T>:Base { public override T Read(Word value)=>(T)value; }
            }
            alias Word=long;
            int main() {
                Library::Box<int> item=new Library::Box<int>();
                Library::Base* view=&item;
                return view.Read(23);
            }
            """;
        AssertValid(source);
        Assert.Equal(23, CompilerTestHelper.Run(source).ExitCode);
    }

    [Fact]
    public void PointerAliasKeepsItsDeclarationQualifiers()
    {
        const string source = """
            namespace Library {
                alias Text=readonly(char*);
                class Base { public virtual Text Read()=>"base"; }
                class Box<T>:Base { public override Text Read()=>"child"; }
            }
            alias Text=char*;
            int main() {
                Library::Box<int> item=new Library::Box<int>();
                Library::Base* view=&item;
                if (view.Read()[0] != 'c') return 1;
                return 0;
            }
            """;
        AssertValid(source);
        Assert.Equal(0, CompilerTestHelper.Run(source).ExitCode);
    }

    [Fact]
    public void NestedOwnerTypesResolveWithinConcreteDeclaringOwner()
    {
        const string source = """
            namespace Library {
                class Owner {
                    public struct Word { public int value; }
                    public class Base { public virtual Word Read()=>new Word(); }
                    public class Box<T>:Base { public override Word Read()=>new Word(); }
                }
            }
            int main() { Library::Owner::Box<int> item=new Library::Owner::Box<int>(); return item.Read().value; }
            """;
        AssertValid(source);
        Assert.Equal(0, CompilerTestHelper.Run(source).ExitCode);
    }

    [Fact]
    public void StandaloneHierarchyClientsRetainTheirAliasResolver()
    {
        var source = new NamedTypeExpression("Word", null, 0);
        var target = new NamedTypeExpression("int", null, 0);
        var method = new MethodDeclaration("Read", source, [], null, TokenKind.Public, false, false, false, false, 0);
        var symbols = new SymbolTable { AliasResolver = type => ReferenceEquals(type, source) ? target : type };
        Assert.Same(target, symbols.ResolveMethodType(method, source));
        symbols.MethodTypeResolver = (declaration, type) => {
            Assert.Same(method, declaration);
            Assert.Same(source, type);
            return source;
        };
        Assert.Same(source, symbols.ResolveMethodType(method, source));
    }

    [Fact]
    public void FailedOverrideResolutionRestoresCallerScopeForLaterCode()
    {
        const string source = """
            namespace Library {
                alias Word=long;
                class Base { public virtual int Read()=>0; }
                class Box<T>:Base { public override Word Read()=>0; }
            }
            namespace Caller {
                alias Word=int;
                void Broken() { alias Word=bool; Library::Box<int> item=new Library::Box<int>(); }
                Word Healthy(Word value)=>value;
            }
            int main()=>Caller::Healthy(23);
            """;
        var error = Assert.Single(Analyze(source), d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal("Overriding method 'Read' in class 'Library::Box<int>' has return type 'long' which does not match base method return type 'int'", error.Message);
    }

    private static IReadOnlyList<Diagnostic> Analyze(string source) =>
        Compiler.Analyze([new SourceFile("signature-context.gf", source)], new() { NoStdLib = true });

    private static void AssertValid(string source) =>
        Assert.DoesNotContain(Analyze(source), d => d.Severity == DiagnosticSeverity.Error);
}
