namespace gflat.Tests.Namespaces;

public sealed class NamespaceAliasTargetContextTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach(string kind in new[]{"class","struct"})
        foreach(bool early in new[]{false,true})
        foreach(int optimization in new[]{0,2})
            yield return [kind,early,optimization];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void NamespaceAliasesResolveWithoutCallerOwnersAndRestoreTheCaller(string kind,bool early,int optimization)
    {
        const string definitions="""
            namespace Definitions;
            public struct Entry {
                public long Value;
                public Entry(long value){Value=value;}
            }
            public alias ValueAlias=Entry;
            public alias Storage=Entry*;
            """;
        string helper=$$"""
            using Definitions;
            namespace Callers;
            {{kind}} Probe<T> {
                public struct Entry {public byte Wrong;}
                public static int Check(Storage data) {
                    if(data.Value!=42l)return 1;
                    if(sizeof(ValueAlias)!=8)return 2;
                    if(sizeof(ValueAlias)+sizeof(Entry)!=9)return 3;
                    {alias Local=Entry;if(sizeof(Local)!=1)return 4;}
                    return 42;
                }
            }
            """;
        string client=(early?"int Trigger(Callers::Probe<int>* value)=>0;\n":"")+"""
            int main(){
                Definitions::Entry value=new(42l);
                return Callers::Probe<int>::Check(&value);
            }
            """;
        SourceFile[] sources=[
            new(early?"a-client.gf":"z-client.gf",client),
            new("m-definitions.gf",definitions),new("n-caller.gf",helper)
        ];
        var result=CompilerTestHelper.Run(sources,optimization);
        Assert.True(result.ExitCode==42,$"Exit {result.ExitCode}: {result.StandardError}");
    }
}
