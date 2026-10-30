using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class GenericOverloadContractTests
{
    private const string Providers="""
        interface IProvider{static int Read();}
        interface IExtra{static int Extra();}
        static class Answer:IProvider,IExtra{
            public static int Read()=>42;
            public static int Extra()=>7;
        }
        weak alias Selected:IProvider=Answer;
        struct Handler<P>{}
        int Pick<P:IProvider>(Handler<P> value,long tag)=>P::Read();
        int Pick<P:IExtra>(Handler<P> value,int tag)=>P::Extra();
        int PickExtension<P:IProvider>(Handler<P> value,long tag)=>P::Read();
        extension<P:IProvider> Handler<P> { public int PickExtension(long tag) =>P::Read(); }
        int PickExtension<P:IExtra>(Handler<P> value,int tag)=>P::Extra();
        extension<P:IExtra> Handler<P> { public int PickExtension(int tag) =>P::Extra(); }
        """;

    [Theory]
    [InlineData(false,0)]
    [InlineData(false,2)]
    [InlineData(true,0)]
    [InlineData(true,2)]
    public void ConstrainedAliasKeepsItsExposedViewWithoutChangingNominalIdentity(bool concreteFirst,int optimization)
    {
        const string concrete="""
            if(Pick<Answer>(concrete,1)!=7||Pick(concrete,1)!=7||concrete.PickExtension(1)!=7)return 1;
            """;
        const string alias="""
            if(Pick<Selected>(contract,1)!=42||Pick(contract,1)!=42||contract.PickExtension(1)!=42)return 2;
            """;
        Run(Providers+$$"""
            int main(){
                Handler<Answer> concrete=new();
                // The alias retains the provider's nominal type; its declared view
                // still controls which constraints overload resolution may use.
                Handler<Selected> contract=concrete;
                {{(concreteFirst?concrete+alias:alias+concrete)}}
                if(Pick<Selected>(contract,1)!=42||Pick<Answer>(concrete,1)!=7)return 3;
                return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(false,0)]
    [InlineData(false,2)]
    [InlineData(true,0)]
    [InlineData(true,2)]
    public void GenericForwardingUsesOnlyTheDeclaredInterfacesInBothCacheWarmOrders(bool concreteFirst,int optimization)
    {
        const string concrete="""
            if(Forward<Answer>(concrete)!=42||ForwardExtension<Answer>(concrete)!=42)return 1;
            """;
        const string alias="""
            if(Forward<Selected>(contract)!=42||ForwardExtension<Selected>(contract)!=42)return 2;
            """;
        Run(Providers+$$"""
            int Forward<T:IProvider>(Handler<T> value)=>Pick<T>(value,1);
            int ForwardExtension<T:IProvider>(Handler<T> value)=>value.PickExtension(1);
            int main(){
                Handler<Answer> concrete=new();Handler<Selected> contract=concrete;
                {{(concreteFirst?concrete+alias:alias+concrete)}}
                // A direct concrete call may use IExtra; the generic body may not.
                if(Pick<Answer>(concrete,1)!=7||Forward<Answer>(concrete)!=42)return 3;
                return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(false,0)]
    [InlineData(false,2)]
    [InlineData(true,0)]
    [InlineData(true,2)]
    public void GenericOwnerBodiesPreserveTheirDeclaredInterfaceView(bool concreteFirst,int optimization)
    {
        const string concrete="""
            if(Forwarder<Answer>::Read(concrete)!=42||Forwarder<Answer>::ReadExtension(concrete)!=42)return 1;
            """;
        const string alias="""
            if(Forwarder<Selected>::Read(contract)!=42||Forwarder<Selected>::ReadExtension(contract)!=42)return 2;
            """;
        Run(Providers+$$"""
            struct Forwarder<P:IProvider>{
                public static int Read(Handler<P> value)=>Pick<P>(value,1);
                public static int ReadExtension(Handler<P> value)=>value.PickExtension(1);
            }
            int main(){
                Handler<Answer> concrete=new();Handler<Selected> contract=concrete;
                {{(concreteFirst?concrete+alias:alias+concrete)}}
                if(Pick<Answer>(concrete,1)!=7||Forwarder<Answer>::Read(concrete)!=42)return 3;
                return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(false,0)]
    [InlineData(false,2)]
    [InlineData(true,0)]
    [InlineData(true,2)]
    public void UnconstrainedGenericOriginDoesNotBecomeAConcreteOverloadAfterSubstitution(bool concreteFirst,int optimization)
    {
        const string concrete="if(F(0)!=2)return 1;";
        const string generic="if(Forward<int>(0)!=1||Owner<int>::Read(0)!=1)return 2;";
        Run($$"""
            int F<T>(T value)=>1;
            int F(int value)=>2;
            int Forward<P>(P value)=>F(value);
            struct Owner<P>{public static int Read(P value)=>F(value);}
            int main(){
                {{(concreteFirst?concrete+generic:generic+concrete)}}
                if(F(0)!=2||Forward<int>(0)!=1||Owner<int>::Read(0)!=1)return 3;
                return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(false,0)]
    [InlineData(false,2)]
    [InlineData(true,0)]
    [InlineData(true,2)]
    public void GenericFunctionResultsUseTheCallersDeclaredTypeView(bool concreteFirst,int optimization)
    {
        const string concrete="if(F(Identity<int>(0))!=2)return 1;";
        const string generic="if(Forward<int>(0)!=1)return 2;";
        Run($$"""
            int F<T>(T value)=>1;
            int F(int value)=>2;
            T Identity<T>(T value)=>value;
            int Forward<P>(P value)=>F(Identity<P>(value));
            int main(){
                {{(concreteFirst?concrete+generic:generic+concrete)}}
                if(F(Identity<int>(0))!=2||Forward<int>(0)!=1)return 3;
                return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(false,0)]
    [InlineData(false,2)]
    [InlineData(true,0)]
    [InlineData(true,2)]
    public void GenericOwnerResultsUseTheCallersDeclaredTypeView(bool concreteFirst,int optimization)
    {
        const string concrete="if(F(concrete.Get())!=2)return 1;";
        const string generic="if(Forward<int>(0)!=1)return 2;";
        Run($$"""
            int F<T>(T value)=>1;
            int F(int value)=>2;
            struct Owner<T>{
                private T value;
                public Owner(T input){value=input;}
                public T Get()=>value;
            }
            int Forward<P>(P value){Owner<P> owner=new(value);return F(owner.Get());}
            int main(){
                Owner<int> concrete=new(0);
                {{(concreteFirst?concrete+generic:generic+concrete)}}
                if(F(concrete.Get())!=2||Forward<int>(0)!=1)return 3;
                return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(false,0)]
    [InlineData(false,2)]
    [InlineData(true,0)]
    [InlineData(true,2)]
    public void GenericStaticOwnerFunctionGroupResultsUseTheCallersDeclaredTypeView(bool concreteFirst,int optimization)
    {
        const string concrete="if(F(callback(0,0))!=2)return 1;";
        const string generic="if(Forward<int>(0)!=1)return 2;";
        Run($$"""
            int F<T>(T value)=>1;
            int F(int value)=>2;
            class Factory<T>{
                public static T Make(bool witness,T value)=>value;
                public static T Make<U>(U witness,T value)=>value;
            }
            int Forward<P>(P value){
                P(int,P)* callback=&Factory<P>::Make;
                return F(callback(0,value));
            }
            int main(){
                int(int,int)* callback=&Factory<int>::Make;
                {{(concreteFirst?concrete+generic:generic+concrete)}}
                if(F(callback(0,0))!=2||Forward<int>(0)!=1)return 3;
                return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(false,0)]
    [InlineData(false,2)]
    [InlineData(true,0)]
    [InlineData(true,2)]
    public void StaticProviderOverloadsAreRankedWithinTheExposedContract(bool concreteFirst,int optimization)
    {
        const string concrete="""
            if(Answer::Read(0)!=7||Forward<Answer>()!=42)return 1;
            """;
        const string alias="""
            if(Selected::Read(0)!=42||Forward<Selected>()!=42)return 2;
            """;
        Run($$"""
            interface IProvider{static int Read(long value);}
            static class Answer:IProvider{
                public static int Read(long value)=>42;
                public static int Read(int value)=>7;
            }
            weak alias Selected:IProvider=Answer;
            int Forward<P:IProvider>()=>P::Read(0);
            int main(){
                {{(concreteFirst?concrete+alias:alias+concrete)}}
                if(Answer::Read(0)!=7||Selected::Read(0)!=42)return 3;
                if(Forward<Answer>()!=42||Forward<Selected>()!=42)return 4;
                return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InferredGenericDependenciesRetainAliasRestrictionsAfterNominalCacheWarmup(bool warmConcrete)
    {
        string source=Providers+$$"""
            int Hidden<P>(Handler<P> value)=>P::Extra();
            int Forward<P>(Handler<P> value)=>Hidden(value);
            int main(){
                Handler<Answer> concrete=new();Handler<Selected> contract=concrete;
                {{(warmConcrete?"Forward<Answer>(concrete);":"")}}
                return Forward<Selected>(contract);
            }
            """;
        Assert.Throws<TypeCheckException>(()=>Compiler.Check(source));
    }

    [Theory]
    [InlineData(false,0)]
    [InlineData(false,2)]
    [InlineData(true,0)]
    [InlineData(true,2)]
    public void UnselectedOverloadBodiesDoNotAddAliasCapabilityRequirements(bool concreteFirst,int optimization)
    {
        const string concrete="if(Visible<Answer>(concrete,1)!=42)return 1;";
        const string alias="if(Visible<Selected>(contract,1)!=42)return 2;";
        Run(Providers+$$"""
            int Visible<P:IProvider>(Handler<P> value,int tag)=>P::Read();
            int Visible<P>(Handler<P> value,long tag)=>P::Extra();
            int main(){
                Handler<Answer> concrete=new();Handler<Selected> contract=concrete;
                {{(concreteFirst?concrete+alias:alias+concrete)}}
                if(Visible<Selected>(contract,1)!=42||Visible<Answer>(concrete,1)!=42)return 3;
                return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData("alias",false)]
    [InlineData("alias",true)]
    [InlineData("function",false)]
    [InlineData("function",true)]
    [InlineData("owner",false)]
    [InlineData("owner",true)]
    public void ConcreteProviderCapabilitiesDoNotAuthorizeUnexposedCalls(string context,bool warmConcrete)
    {
        // Single-target templates retain their existing specialization semantics.
        // The function/owner cases specifically test overload applicability.
        string extraDeclarations=context=="alias"
            ? "int OnlyExtra<P:IExtra>(Handler<P> value)=>P::Extra();"
            : "int OnlyExtra(bool tag)=>0;int OnlyExtra<P:IExtra>(int tag)=>P::Extra();";
        string declaration=context switch{
            "function"=>"int Forbidden<T:IProvider>(Handler<T> value)=>OnlyExtra<T>(0);",
            "owner"=>"struct Forbidden<P:IProvider>{public static int Read(Handler<P> value)=>OnlyExtra<P>(0);}",
            _=>""
        };
        string call=context switch{
            "function"=>"Forbidden<Answer>(concrete)",
            "owner"=>"Forbidden<Answer>::Read(concrete)",
            _=>"OnlyExtra<Selected>(contract)"
        };
        string warmCall=context=="alias"?"OnlyExtra<Answer>(concrete);":"OnlyExtra<Answer>(0);";
        string source=Providers+$$"""
            {{extraDeclarations}}
            {{declaration}}
            int main(){
                Handler<Answer> concrete=new();Handler<Selected> contract=concrete;
                {{(warmConcrete?warmCall:"")}}
                return {{call}};
            }
            """;
        var error=Assert.Throws<TypeCheckException>(()=>Compiler.Check(source));
        Assert.DoesNotContain("Overloaded generic function families are not yet supported",error.Message);
    }

    private static void Run(string source,int optimization)
    {
        var result=CompilerTestHelper.Run(source,optimization);
        Assert.True(result.ExitCode==0,$"Generic contract exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
