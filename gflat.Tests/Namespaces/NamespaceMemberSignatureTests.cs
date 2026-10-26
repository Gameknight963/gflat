using gflat.CompileExceptions;

namespace gflat.Tests.Namespaces;

public sealed class NamespaceMemberSignatureTests
{
    [Theory]
    [InlineData(false,false,0)]
    [InlineData(false,false,2)]
    [InlineData(false,true,0)]
    [InlineData(false,true,2)]
    [InlineData(true,false,0)]
    [InlineData(true,false,2)]
    [InlineData(true,true,0)]
    [InlineData(true,true,2)]
    public void EarlyMemberSignaturesMatchNormalDeclarationBinding(bool early,bool reverse,int optimization)
    {
        const string library="""
            namespace Library {
                alias Word=int;
                Word FreeRead(Word* value)=>*value;
                struct SBox {
                    public Word Value;
                    public Word Read(Word* value)=>*value;
                    public Word Property {get=>Value;set{Value=value;}}
                }
                class CBox {
                    public Word Value;
                    public Word Read(Word* value)=>*value;
                    public Word Property {get=>Value;set{Value=value;}}
                }
                class Base {
                    public Word Value;
                    public Word Read(Word* value)=>*value;
                }
                class Derived:Base {}
                interface IBox {Word Read(Word* value);Word Property {get;}}
                class Implementation:IBox {
                    public Word Read(Word* value)=>*value;
                    public Word Property {get=>42;}
                }
                class Static {public static Word Read(Word* value)=>*value;}
                struct GenericBox<T> {public T Value;public T Read(T* value)=>*value;}
            }
            """;
        const string helper="""
            namespace Helpers {
                alias Word=long;
                struct Probe<T> {
                    public static int Free(int* value)=>Library::FreeRead(value);
                    public static int StructMethod(Library::SBox* owner,int* value)=>owner.Read(value);
                    public static int ClassMethod(Library::CBox* owner,int* value)=>owner.Read(value);
                    public static int InterfaceMethod(Library::IBox* owner,int* value)=>owner.Read(value);
                    public static int StructField(Library::SBox* owner)=>owner.Value;
                    public static int ClassField(Library::CBox* owner)=>owner.Value;
                    public static int StructProperty(Library::SBox* owner)=>owner.Property;
                    public static int ClassProperty(Library::CBox* owner)=>owner.Property;
                    public static int InterfaceProperty(Library::IBox* owner)=>owner.Property;
                    public static int InheritedField(Library::Derived* owner)=>owner.Value;
                    public static int InheritedMethod(Library::Derived* owner,int* value)=>owner.Read(value);
                    public static int StaticControl(int* value)=>Library::Static::Read(value);
                    public static U GenericMethod<U>(Library::GenericBox<U>* owner,U* value)=>owner.Read(value);
                    public static U GenericField<U>(Library::GenericBox<U>* owner)=>owner.Value;
                    public static void SetStruct(Library::SBox* owner){owner.Property=42;}
                    public static void SetClass(Library::CBox* owner){owner.Property=42;}
                }
            }
            """;
        string client=(early?"int Trigger(Helpers::Probe<int>* value)=>0;\n":"")+"""
            int main(){
                int value=42;
                Library::SBox structure=new();
                Library::CBox object=new();
                Library::Derived inherited=new();inherited.Value=42;
                Library::Implementation implementation=new();
                Library::IBox* contract=&implementation;
                Helpers::Probe<int>::SetStruct(&structure);
                Helpers::Probe<int>::SetClass(&object);
                if(Helpers::Probe<int>::Free(&value)!=42)return 1;
                if(Helpers::Probe<int>::StructMethod(&structure,&value)!=42)return 2;
                if(Helpers::Probe<int>::ClassMethod(&object,&value)!=42)return 3;
                if(Helpers::Probe<int>::InterfaceMethod(contract,&value)!=42)return 4;
                if(Helpers::Probe<int>::StructField(&structure)!=42)return 5;
                if(Helpers::Probe<int>::ClassField(&object)!=42)return 6;
                if(Helpers::Probe<int>::StructProperty(&structure)!=42)return 7;
                if(Helpers::Probe<int>::ClassProperty(&object)!=42)return 8;
                if(Helpers::Probe<int>::InterfaceProperty(contract)!=42)return 9;
                if(Helpers::Probe<int>::InheritedField(&inherited)!=42)return 10;
                if(Helpers::Probe<int>::InheritedMethod(&inherited,&value)!=42)return 11;
                if(Helpers::Probe<int>::StaticControl(&value)!=42)return 12;
                Library::GenericBox<int> generic=new();generic.Value=42;
                if(Helpers::Probe<int>::GenericMethod<int>(&generic,&value)!=42)return 13;
                if(Helpers::Probe<int>::GenericField<int>(&generic)!=42)return 14;
                return 0;
            }
            """;
        var result=CompilerTestHelper.Run(Ordered(library,helper,client,reverse),optimization);
        Assert.True(result.ExitCode==0,$"Exit {result.ExitCode}: {result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CallbackFieldAliasesBindTheirSignatureChildrenBeforeTheCallerUsesThem(int optimization)
    {
        const string library="""
            namespace Library {
                alias Word=int;
                alias Callback=Word(Word*);
                Word Read(Word* value)=>*value;
                struct Structure {public Callback*? Callback;public Word(Word*)*? Direct;}
                class Object {public Callback*? Callback;public Word(Word*)*? Direct;}
            }
            """;
        const string helper="""
            namespace Helpers {
                alias Word=long;
                struct Probe<T> {
                    public static int StructAlias(Library::Structure* owner,int* value)=>owner.Callback!(value);
                    public static int StructDirect(Library::Structure* owner,int* value)=>owner.Direct!(value);
                    public static int ClassAlias(Library::Object* owner,int* value)=>owner.Callback!(value);
                    public static int ClassDirect(Library::Object* owner,int* value)=>owner.Direct!(value);
                }
            }
            """;
        const string client="""
            int Trigger(Helpers::Probe<int>* owner)=>0;
            int main(){
                int value=42;
                Library::Structure structure=new();
                structure.Callback=Library::Read;structure.Direct=Library::Read;
                Library::Object object=new();
                object.Callback=Library::Read;object.Direct=Library::Read;
                if(Helpers::Probe<int>::StructAlias(&structure,&value)!=42)return 1;
                if(Helpers::Probe<int>::StructDirect(&structure,&value)!=42)return 2;
                if(Helpers::Probe<int>::ClassAlias(&object,&value)!=42)return 3;
                if(Helpers::Probe<int>::ClassDirect(&object,&value)!=42)return 4;
                return 0;
            }
            """;
        var result=CompilerTestHelper.Run(Ordered(library,helper,client,reverse:true),optimization);
        Assert.True(result.ExitCode==0,$"Exit {result.ExitCode}: {result.StandardError}");
    }

    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    public void EarlyFieldLookupDoesNotBypassPrivateAccess(string kind)
    {
        string library="namespace Library {alias Word=int; "+kind+" Box{private Word Secret;}}";
        const string helper="namespace Helpers {alias Word=long;struct Probe<T>{public static int Read(Library::Box* owner)=>owner.Secret;}}";
        var error=Assert.Throws<TypeCheckException>(()=>Compiler.Check(Ordered(library,helper,
            "int Trigger(Helpers::Probe<int>* value)=>0;int main()=>0;",reverse:true),options:new(){NoStdLib=true}));
        Assert.Contains("private",error.Message);
        Assert.Contains("Secret",error.Message);
        Assert.Equal("a-helper.gf",error.Span.Source?.Path);
    }

    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    public void EarlyFieldBindingKeepsImmutableAliasProtection(string kind)
    {
        string library="namespace Library {alias Text=immutable(char*); "+kind+" Box{public Text Data;}}";
        const string helper="namespace Helpers {alias Text=char*;struct Probe<T>{public static char* Read(Library::Box* owner)=>owner.Data;}}";
        var error=Assert.Throws<TypeCheckException>(()=>Compiler.Check(Ordered(library,helper,
            "int Trigger(Helpers::Probe<int>* value)=>0;int main()=>0;",reverse:false),options:new(){NoStdLib=true}));
        Assert.Contains("Cannot return",error.Message);
        Assert.Equal("b-helper.gf",error.Span.Source?.Path);
    }

    private static SourceFile[] Ordered(string library,string helper,string client,bool reverse)
        =>[new(reverse?"b-library.gf":"a-library.gf",library),new(reverse?"a-helper.gf":"b-helper.gf",helper),new("z-client.gf",client)];
}
