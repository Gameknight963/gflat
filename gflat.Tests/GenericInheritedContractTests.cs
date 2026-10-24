namespace gflat.Tests;

public sealed class GenericInheritedContractTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 2)]
    public void InheritedFieldsAndResultsComposeRenamedArgumentsAcrossTwoBaseEdges(bool concreteFirst, int optimization)
    {
        string direct = "if(F(owner.Value)+F(owner.Get())!=4)return 1;";
        string forwarded = "if(Forward<int>(&owner)!=2)return 2;";
        Run($$"""
            int F<T>(T value)=>1;int F(int value)=>2;
            class Base<T>{public T Value;public T Get()=>Value;}
            class Middle<U>:Base<U>{}
            class Derived<A,B>:Middle<B>{}
            int Forward<P>(Derived<byte,P>* owner)=>F(owner.Value)+F(owner.Get());
            int main(){Derived<byte,int> owner=new();owner.Value=0;
                {{(concreteFirst ? direct + forwarded : forwarded + direct)}}
                if(Forward<int>(&owner)!=2||F(owner.Value)+F(owner.Get())!=4)return 3;return 0;}
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ConcreteAliasedBaseDoesNotAcquireUnrelatedDerivedParameterView(int optimization)
        => Run("""
            namespace Library{
                public class Base<T>{public T Value;public T Get()=>Value;}
                alias Parent=Base<int>;
                public class Derived<U>:Parent{}
            }
            int F<T>(T value)=>1;int F(int value)=>2;
            int Forward<P>(Library::Derived<P>* owner)=>F(owner.Value)+F(owner.Get());
            int main(){alias Base=byte;Library::Derived<int> owner=new();owner.Value=0;
                if(Forward<int>(&owner)!=4||F(owner.Value)+F(owner.Get())!=4)return 1;return 0;}
            """, optimization);

    private static void Run(string source, int optimization)
    {
        var result = CompilerTestHelper.Run(source, optimization);
        Assert.True(result.ExitCode == 0, $"Inherited contract exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
