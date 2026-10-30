using gflat.ast;
using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ExtensionAliasConstraintTests
{
    private const string Providers="""
        interface IProvider {static int Read();}
        interface IExtra {static int Extra();}
        static class Answer:IProvider,IExtra {
            public static int Read()=>42;
            public static int Extra()=>7;
        }
        weak alias Selected:IProvider=Answer;
        struct Handler<P>{}
        int Visible<P:IProvider>(Handler<P> value)=>P::Read();
        extension<P:IProvider> Handler<P> { public int Visible() =>P::Read(); }
        int Hidden<P:IExtra>(Handler<P> value)=>P::Extra();
        extension<P:IExtra> Handler<P> { public int Hidden() =>P::Extra(); }
        """;

    [Fact]
    public void PreviewUsesTheExposedAliasInterfacesWithoutChangingSemanticState()
    {
        var (ast,checker)=Compiler.Check(Providers+"int main(){Handler<Selected> value=new Handler<Selected>();return 0;}",options:new(){NoStdLib=true});
        var type=Receiver(checker);
        var origin=checker.GetGenericTypeOrigin(Assert.IsType<NamedTypeExpression>(type).Name);
        int members=ast.Members.Count;
        for(int i=0;i<5;i++) {
            Assert.Single(checker.GetVisibleExtensions("",[],type,true,false,"Visible"));
            Assert.Empty(checker.GetVisibleExtensions("",[],type,true,false,"Hidden"));
        }
        Assert.Equal(members,ast.Members.Count);
        Assert.Same(origin,checker.GetGenericTypeOrigin(Assert.IsType<NamedTypeExpression>(type).Name));
    }

    [Fact]
    public void AnInterfaceTypeIsNotItsOwnStaticImplementation()
    {
        string source=Providers+"int main(){Handler<IProvider> value=new Handler<IProvider>();return 0;}";
        var (_,checker)=Compiler.Check(source,options:new(){NoStdLib=true});
        var type=checker.ResolveAlias(new NamedTypeExpression("Handler",null,1,[new NamedTypeExpression("IProvider",null,1)]));
        Assert.Empty(checker.GetVisibleExtensions("",[],type,true,false,"Visible"));
        var error=Assert.Throws<TypeCheckException>(()=>Compiler.Emit(
            source.Replace("return 0;","return Visible<IProvider>(value);"),options:new(){NoStdLib=true}));
        Assert.Contains("does not satisfy",error.Message);
    }

    [Fact]
    public void ConstraintExposureDoesNotDependOnEditorSourceBindings()
    {
        var ast=Parser.Parse(Lexer.Tokenize(Providers+"int main(){Handler<Selected> value=new Handler<Selected>();return 0;}"));
        var alias=ast.Members.OfType<AliasDeclaration>().Single();
        var constraint=Assert.IsType<NamedTypeExpression>(Assert.Single(alias.Constraints));
        constraint.NameSpan=default;
        var checker=new TypeChecker();
        ast.Accept(checker);
        Assert.Null(checker.GetSourceTypeBinding(constraint));
        var type=Receiver(checker);
        Assert.Single(checker.GetVisibleExtensions("",[],type,true,false,"Visible"));
        Assert.Empty(checker.GetVisibleExtensions("",[],type,true,false,"Hidden"));
    }

    [Fact]
    public void AClassContractExposesItsBaseAndInterfacesButNotDerivedCapabilities()
    {
        const string source="""
            interface IProvider {static int Read();}
            interface IExtra {static int Extra();}
            class Base:IProvider {public static int Read()=>42;}
            class Derived:Base,IExtra {public static int Extra()=>7;}
            weak alias Selected:Base=Derived;
            struct Handler<P>{}
            int BaseMethod<P:Base>(Handler<P> value)=>0;
            extension<P:Base> Handler<P> { public int BaseMethod() =>0; }
            int ProviderMethod<P:IProvider>(Handler<P> value)=>0;
            extension<P:IProvider> Handler<P> { public int ProviderMethod() =>0; }
            int DerivedMethod<P:Derived>(Handler<P> value)=>0;
            extension<P:Derived> Handler<P> { public int DerivedMethod() =>0; }
            int ExtraMethod<P:IExtra>(Handler<P> value)=>0;
            extension<P:IExtra> Handler<P> { public int ExtraMethod() =>0; }
            int main(){Handler<Selected> value=new Handler<Selected>();return 0;}
            """;
        var (_,checker)=Compiler.Check(source,options:new(){NoStdLib=true});
        var type=Receiver(checker);
        Assert.Single(checker.GetVisibleExtensions("",[],type,true,false,"BaseMethod"));
        Assert.Single(checker.GetVisibleExtensions("",[],type,true,false,"ProviderMethod"));
        Assert.Empty(checker.GetVisibleExtensions("",[],type,true,false,"DerivedMethod"));
        Assert.Empty(checker.GetVisibleExtensions("",[],type,true,false,"ExtraMethod"));
    }

    [Theory]
    [InlineData(false,false)]
    [InlineData(false,true)]
    [InlineData(true,false)]
    [InlineData(true,true)]
    public void ActualCallsKeepAcceptingExposedConstraints(bool extension,bool cached)
    {
        string source=Providers+$$"""
            int main(){
                {{(cached ? "Handler<Answer> first=new Handler<Answer>();Hidden<Answer>(first);" : "")}}
                Handler<Selected> value=new Handler<Selected>();
                return {{(extension ? "value.Visible()" : "Visible<Selected>(value)")}};
            }
            """;
        Assert.Equal(42,CompilerTestHelper.Run(source).ExitCode);
    }

    [Theory]
    [InlineData(false,false)]
    [InlineData(false,true)]
    [InlineData(true,false)]
    [InlineData(true,true)]
    public void ActualCallsKeepRejectingUnexposedConstraints(bool extension,bool cached)
    {
        string source=Providers+$$"""
            int main(){
                {{(cached ? "Handler<Answer> first=new Handler<Answer>();Hidden<Answer>(first);" : "")}}
                Handler<Selected> value=new Handler<Selected>();
                return {{(extension ? "value.Hidden()" : "Hidden<Selected>(value)")}};
            }
            """;
        var error=Assert.Throws<TypeCheckException>(()=>Compiler.Emit(source,options:new(){NoStdLib=true}));
        Assert.Contains(extension ? "has no field or method 'Hidden'" : "does not satisfy constraint",error.Message);
    }

    private static TypeExpression Receiver(TypeChecker checker)=>checker.ResolveAlias(
        new NamedTypeExpression("Handler",null,1,[new NamedTypeExpression("Selected",null,1)]));

}
