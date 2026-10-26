using gflat.CompileExceptions;

namespace gflat.Tests.Namespaces;

public sealed class NamespaceConstructorSignatureTests
{
    public static IEnumerable<object[]> CreationCases()
    {
        foreach (string kind in new[] { "class", "struct" })
        foreach (bool early in new[] { false, true })
        foreach (bool reverse in new[] { false, true })
        foreach (int optimization in new[] { 0, 2 })
            yield return [kind, early, reverse, optimization];
    }

    [Theory]
    [MemberData(nameof(CreationCases))]
    public void AllCreationFormsUseTheDeclaredPointerOverload(string kind, bool early, bool reverse, int optimization)
    {
        string library = "namespace Library { alias Word=int; " + kind + " " + """
             Box {
                public int Value;
                public Box(Word* value){Value=*value;}
                public Box(long* value){Value=7;}
            } }
            """;
        const string body = """
            int value=42; long other=9l;
            Library::Box first=new Library::Box(&value);
            Library::Box contextual=new(&value);
            Library::Box wide=new Library::Box(&other);
            if(first.Value!=42 || contextual.Value!=42 || wide.Value!=7)return 1;
            Library::Box* heap=new* Library::Box(&value);
            if(heap.Value!=42){delete heap;return 2;}
            delete heap;
            void*? memory=global::Memory::Allocate((nuint)sizeof(Library::Box));
            if(memory==null)return 3;
            Library::Box* placed=new* Library::Box(&value) at (Library::Box*)memory!;
            bool valid=placed.Value==42;
            delete placed;
            if(!valid)return 4;
            return 0;
            """;
        AssertRun(Sources(library, body, early, reverse), optimization);
    }

    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    public void BindingDoesNotMakeTheSelectedPrivateConstructorAccessible(string kind)
    {
        string library="namespace Library { alias Word=int; "+kind+" "+"""
             Box {
                private Box(Word* value){}
                public Box(long* value){}
            } }
            """;
        var error=Assert.Throws<TypeCheckException>(()=>Compiler.Check(Sources(library,
            "int value=42; Library::Box item=new Library::Box(&value);return 0;",early:true,reverse:true)));
        Assert.Contains("constructor",error.Message);
        Assert.Contains("inaccessible",error.Message);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 2)]
    public void ExplicitAndImplicitBaseSelectionUseTheBaseDeclaration(bool early,int optimization)
    {
        const string library="""
            namespace Library {
                alias Word=int;
                class Base {
                    public int Value;
                    public Base(Word* value){Value=*value;}
                    public Base(Word value=42){Value=value;}
                }
            }
            """;
        const string declarations="""
            class Derived<T>:Library::Base {
                public Derived(int* value):base(value){}
                public Derived(){}
            }
            """;
        const string body="""
            int value=42;
            Derived<int> explicitBase=new Derived<int>(&value);
            Derived<int> implicitBase=new Derived<int>();
            if(explicitBase.Value!=42 || implicitBase.Value!=42)return 1;
            return 0;
            """;
        AssertRun(Sources(library,body,early,reverse:true,declarations),optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NestedConstructorParametersKeepTheirEnclosingOwner(int optimization)
    {
        const string library="""
            namespace Library {
                class Outer {
                    public struct Token {public int Value;}
                    public class Box {
                        public int Value;
                        public Box(Token* value){Value=value.Value;}
                    }
                }
            }
            """;
        const string body="""
            Library::Outer::Token token=default;token.Value=42;
            Library::Outer::Box value=new Library::Outer::Box(&token);
            if(value.Value!=42)return 1;
            return 0;
            """;
        AssertRun(Sources(library,body,early:true,reverse:true,"alias Token=long;"),optimization);
    }

    private static SourceFile[] Sources(string library,string body,bool early,bool reverse,string extra="")
    {
        string helper="namespace Helpers { alias Word=long; "+extra+
            " struct Probe<T>{ public static int Check(){ "+body+" } } }";
        string client=(early?"int Trigger(Helpers::Probe<int>* value)=>0;\n":"")+
            "int main()=>Helpers::Probe<int>::Check();";
        return [new SourceFile(reverse?"b-library.gf":"a-library.gf",library),
            new SourceFile(reverse?"a-helper.gf":"b-helper.gf",helper),new SourceFile("z-client.gf",client)];
    }

    private static void AssertRun(IEnumerable<SourceFile> sources,int optimization)
    {
        var result=CompilerTestHelper.Run(sources,optimization);
        Assert.True(result.ExitCode==0,$"Exit {result.ExitCode}: {result.StandardError}");
    }
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void GeneratedDerivedConstructorUsesTheParentsNullableAliasDefault(int optimization)
    {
        var result=CompilerTestHelper.Run("""
            int Trigger(Helpers::Probe<int>* probe)=>0;
            namespace Library {
                alias Word=int*?;
                class Base {
                    public bool Empty;
                    public Base(Word value=null){Empty=value==null;}
                }
            }
            namespace Helpers {
                alias Word=long;
                class Automatic<T>:Library::Base {}
                struct Probe<T> {
                    public static int Check(){
                        Automatic<int> value=new();
                        if(!value.Empty)return 1;
                        return 42;
                    }
                }
            }
            int main()=>Helpers::Probe<int>::Check();
            """,optimization);
        Assert.Equal(42,result.ExitCode);
    }

}
