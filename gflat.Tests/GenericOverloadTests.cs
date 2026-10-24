using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class GenericOverloadTests
{
    [Theory]
    [InlineData(false,0)]
    [InlineData(false,2)]
    [InlineData(true,0)]
    [InlineData(true,2)]
    public void MixedOrdinaryAndGenericFamiliesUseTheSelectedDeclarationAcrossCallContexts(bool reverse,int optimization)
    {
        string pick=Order(reverse,"int Pick(int value)=>11;","int Pick<T>(T value)=>22;");
        string cache=Order(reverse,"int Cache<T>(T value)=>20;","int Cache<T>(T* value)=>22;");
        string statics=Order(reverse,"public static int Read(int value)=>11;","public static int Read<T>(T value)=>22;");
        string members=Order(reverse,"public int Read(int value)=>11;","public int Read<T>(T value)=>22;");
        string owner=Order(reverse,"public int Read(T value)=>11;","public int Read<U>(U value)=>22;");
        Run($$"""
            {{pick}}
            {{cache}}
            namespace Library{ {{pick}} }
            namespace First{int Identity<T>(T value)=>31;}
            namespace Second{int Identity<T>(T value)=>37;}
            struct StaticMethods{ {{statics}} }
            struct Methods{ {{members}} public int Inside()=>Read(1)+Read<long>(1l); }
            struct Box<T>{ {{owner}} }
            int main(){
                if(Pick(1)!=11||Pick(1l)!=22||Pick<int>(1)!=22)return 1;
                if(Library::Pick(1)!=11||Library::Pick<long>(1l)!=22)return 2;
                if(StaticMethods::Read(1)!=11||StaticMethods::Read<long>(1l)!=22)return 3;
                Methods methods=new();if(methods.Read(1)!=11||methods.Read<long>(1l)!=22||methods.Inside()!=33)return 4;
                Box<int> box=new();if(box.Read(1)!=11||box.Read(1l)!=22)return 5;
                int value=1;
                if(Cache<int>(value)!=20||Cache<int>(&value)!=22)return 6;
                if(Cache<int>(&value)!=22||Cache<int>(value)!=20)return 7;
                if(First::Identity<int>(value)!=31||Second::Identity<int>(value)!=37)return 8;
                if(Second::Identity<int>(value)!=37||First::Identity<int>(value)!=31)return 9;
                return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(false,0)]
    [InlineData(false,2)]
    [InlineData(true,0)]
    [InlineData(true,2)]
    public void GenericInstanceOverloadsMutateTheOriginalStructAndGenericClassReceivers(bool reverse,int optimization)
    {
        string counterMethods=Order(reverse,
            "public int Update(int witness,int delta){this.total+=delta*10;return Read();}",
            "public int Update<U>(U witness,int delta){total+=delta;return this.Read();}");
        string boxMethods=Order(reverse,
            "public int Update(int witness,int delta){this.total+=delta*10;return Read();}",
            "public int Update<U>(U witness,int delta){total+=delta;item=Echo(this.item);return this.Read();}");
        Run($$"""
            struct Counter{
                public int total;
                public Counter(int initial){total=initial;}
                private int Read()=>this.total;
                {{counterMethods}}
            }
            class Box<T>{
                public int total;
                public T item;
                public Box(T value,int initial){item=value;total=initial;}
                private int Read()=>this.total;
                private T Echo(T value)=>value;
                {{boxMethods}}
            }
            int main(){
                Counter first=new(10);Counter second=new(100);
                if(first.Update(0,2)!=30||first.total!=30||second.total!=100)return 1;
                if(first.Update(0l,3)!=33||first.total!=33)return 2;
                if(second.Update<int>(0,4)!=104||second.total!=104||first.total!=33)return 3;
                Counter* counter=&first;
                if(counter.Update<long>(0l,5)!=38||first.total!=38||second.total!=104)return 4;
                Box<int> box=new(42,20);Box<int> other=new(73,200);
                if(box.Update(0,2)!=40||box.total!=40||other.total!=200)return 5;
                if(box.Update(0l,3)!=43||box.total!=43||box.item!=42)return 6;
                if(other.Update<int>(0,4)!=204||other.total!=204||box.total!=43||other.item!=73)return 7;
                Box<int>* pointer=&box;
                if(pointer.Update<long>(0l,5)!=48||box.total!=48||other.total!=204||box.item!=42)return 8;
                return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(false,0)]
    [InlineData(false,2)]
    [InlineData(true,0)]
    [InlineData(true,2)]
    public void CandidateInferenceConstraintsAndSpecificityDoNotInstantiateLosingBodies(bool reverse,int optimization)
    {
        string conversion=Order(reverse,"int Convert(long value)=>1;","int Convert<T>(T value)=>42;");
        string pointer=Order(reverse,"int Pointer<T>(T value)=>1;","int Pointer<T>(T* value)=>42;");
        string structured=Order(reverse,"int Structured<T>(T value)=>1;","int Structured<T>(Box<T> value)=>42;");
        string arity=Order(reverse,"int Arity<T>(T value)=>11;","int Arity<T,U>(T value)=>22;");
        string winner=Order(reverse,"int Winner(int value)=>42;","int Winner<T>(T value)=>MissingLosingBody();");
        string constraints=Order(reverse,"int Constrained(long value)=>42;","int Constrained<T:I>(T value)=>MissingConstraintLoser();");
        string consistent=Order(reverse,"int Consistent<T>(T first,T second)=>42;","int Consistent(long first,long second)=>11;");
        Run($$"""
            interface I{int Read();}
            struct Good:I{public int Read()=>42;}
            struct Box<T>{public T Value;}
            {{conversion}}
            {{pointer}}
            {{structured}}
            {{arity}}
            {{winner}}
            {{constraints}}
            {{consistent}}
            int Applicable(long value)=>1;
            int Applicable<T:I>(T value)=>value.Read();
            int main(){
                int value=1;Box<int> box=new();
                if(Convert(value)!=42||Convert<long>(value)!=42)return 1;
                if(Pointer(&value)!=42||Structured(box)!=42)return 2;
                if(Arity(value)!=11||Arity<int>(value)!=11||Arity<int,byte>(value)!=22)return 3;
                if(Winner(value)!=42||Constrained(value)!=42)return 4;
                Good good=new();if(Applicable(good)!=42)return 6;
                // Inference requires consistent T; explicit T permits ordinary conversions.
                if(Consistent(1,1l)!=11||Consistent<long>(1,1l)!=42)return 5;
                return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(false,0)]
    [InlineData(false,2)]
    [InlineData(true,0)]
    [InlineData(true,2)]
    public void DefaultsAndRefArgumentsRetainExistingRankingAndBorrowingRules(bool reverse,int optimization)
    {
        string defaults=Order(reverse,"int Defaults<T>(T value)=>11;","int Defaults<T>(T value,int tag=0)=>22;");
        string mixed=Order(reverse,"int Mixed(int value,int tag=0)=>11;","int Mixed<T>(T value)=>22;");
        string refs=Order(reverse,"int Ref<T>(T value)=>11;","int Ref<T>(ref T value)=>22;");
        string borrowing=Order(reverse,"int Borrow<T>(T value)=>MissingCopyLoser();","int Borrow<T>(ref immutable(T) value)=>42;");
        Run($$"""
            {{defaults}}
            {{mixed}}
            {{refs}}
            {{borrowing}}
            struct Owner{public int Value;public Owner(int value){Value=value;}~Owner(){} }
            int main(){
                if(Defaults(1)!=11||Defaults(1,0)!=22)return 1;
                if(Mixed(1)!=22)return 2;
                int value=0;
                if(Ref(value)!=11||Ref(ref value)!=22||Ref<int>(ref value)!=22)return 3;
                Owner owner=new(73);if(Borrow(ref owner)!=42||owner.Value!=73)return 4;
                readonly(int) protectedValue=41;
                if(Borrow(ref protectedValue)!=42||protectedValue!=41)return 5;
                return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(false,0)]
    [InlineData(false,2)]
    [InlineData(true,0)]
    [InlineData(true,2)]
    public void ContextualCreationLambdasAndKnownMethodGroupTargetsBindOnlyTheWinner(bool reverse,int optimization)
    {
        string create=Order(reverse,"int Create<T>(T witness,T value,int tag)=>42;","int Create<T>(T witness,T* value,bool tag)=>MissingCreationLoser();");
        string apply=Order(reverse,"int Apply(int(int)* callback)=>callback(40);","T Apply<T>(T(T)* callback)=>callback(default(T));");
        string callback=Order(reverse,"T WithCallback<T>(T witness,T(T)* callback)=>callback(witness);","int WithCallback<T>(T witness,int tag)=>1;");
        string echo=Order(reverse,"int Echo(int value)=>value+1;","T Echo<T>(T value)=>value;");
        Run($$"""
            int constructed=0;int allocations=0;
            namespace Memory{public replace void*? Allocate(nuint count){allocations++;return null;}public replace void Free(void* value)nothrow{} }
            struct Item{public int Value;public Item(int value){Value=value;constructed++;} }
            {{create}}
            {{apply}}
            {{callback}}
            {{echo}}
            int Project(int value)=>value+1;
            long Project(long value)=>value+2l;
            struct StaticOwner{public static int Echo(int value)=>value+1;public static T Echo<T>(T value)=>value;}
            int main(){
                Item item=new(0);if(Create(item,new(7),0)!=42||constructed!=2)return 1;
                if(Apply((int value)=>value+2)!=42||Apply((long value)=>value+42l)!=42l)return 2;
                if(WithCallback(40l,&Project)!=42l||WithCallback<int>(41,&Project)!=42)return 3;
                int(int)* narrow=Echo;long(long)* wide=&Echo;
                int(int)* member=StaticOwner::Echo;long(long)* genericMember=&StaticOwner::Echo;
                if(narrow(41)!=42||wide(4294967297l)!=4294967297l)return 4;
                if(member(41)!=42||genericMember(4294967297l)!=4294967297l)return 5;
                if(allocations!=0)return 6;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SpecializationTypeKeysRetainArrayExtentsAndRawPointerNullability(int optimization)
        => Run("""
            int Width<T>(T value)=>sizeof(T);
            int Nullable<T>(T value){if(typeof(T).IsNullable)return 20;return 22;}
            int main(){
                int[2] first=[1,2];int[3] second=[1,2,3];
                if(Width(first)!=8||Width(second)!=12||Width(first)!=8)return 1;
                int value=0;int* pointer=&value;int*? maybe=null;
                if(Nullable<int*>(pointer)!=22||Nullable<int*?>(maybe)!=20)return 2;
                if(Nullable<int*?>(maybe)!=20||Nullable<int*>(pointer)!=22)return 3;
                return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(false,0)]
    [InlineData(false,2)]
    [InlineData(true,0)]
    [InlineData(true,2)]
    public void NamespaceAliasesReplacementSelectionAndLocalHidingKeepTheirExistingMeaning(bool reverse,int optimization)
    {
        string declarations=Order(reverse,"int Read<T>(Box<T> value,Word tag)=>42;","int Read<T>(T value,long tag)=>1;");
        SourceFile[] sources=
        [
            new("types.gf","namespace Models{struct Box<T>{public T Value;}}"),
            new("library.gf",$$"""
                using Models;
                namespace Library{alias Word=int; {{declarations}} }
                """),
            new("main.gf","""
                using Library;alias Word=long;
                weak T Identity<T>(T value)=>MissingDiscardedWeakBody();
                replace U Identity<U>(U value)=>value;
                int Identity(int value)=>value+1;
                int Hidden(int value)=>1;int Hidden<T>(T value)=>2;
                class Base{public int Pick<T>(T value)=>42;}
                class Derived:Base{public int Pick(long value)=>11;}
                int main(){
                    Models::Box<byte> box=new();if(Read(box,1)!=42)return 1;
                    if(Identity(41)!=42||Identity<long>(42l)!=42l)return 2;
                    int(int)* Hidden=(int value)=>value+41;
                    if(Hidden(1)!=42)return 3;
                    Derived owner=new();if(owner.Pick(1)!=11)return 4;
                    return 0;
                }
                """)
        ];
        AssertSuccess(CompilerTestHelper.Run(sources,optimization));
    }

    public static IEnumerable<object[]> InvalidFamilies()
    {
        yield return ["int F<T>(T value)=>1;int F<U>(U value)=>2;int main()=>0;","cannot differ"];
        yield return ["int F<U>(U value)=>2;int F<T>(T value)=>1;int main()=>0;","cannot differ"];
        yield return ["int F<T>(T* value)=>1;int F<U>(U* value)=>2;int main()=>0;","cannot differ"];
        yield return ["interface A{int AValue();}interface B{int BValue();}int F<T:A>(T value)=>1;int F<U:B>(U value)=>2;int main()=>0;","cannot differ"];
        yield return ["int F<T>(T value)=>1;long F<U>(U value)=>2l;int main()=>0;","cannot differ"];
        yield return ["int F<T>(T* value)=>1;int F<U>(readonly(U)* value)=>2;int main()=>0;","cannot differ"];
        yield return ["int F<T>(ref T value)=>1;int F<U>(ref immutable(U) value)=>2;int main()=>0;","cannot differ"];
        yield return ["int F<T>(T value,int tag=0)=>1;int F<U>(U value,int tag=1)=>2;int main()=>0;","cannot differ"];
        yield return ["int F<T,U>(T* a,U b)=>MissingFirst();int F<T,U>(T a,U* b)=>MissingSecond();int main(){int a=0;int b=0;return F(&a,&b);}","ambiguous"];
        yield return ["int F<T,U>(T a,U* b)=>MissingSecond();int F<T,U>(T* a,U b)=>MissingFirst();int main(){int a=0;int b=0;return F(&a,&b);}","ambiguous"];
        yield return ["int P(int value)=>value;long P(long value)=>value;int Use<T>(T witness,int(int)* callback)=>1;int Use<T>(T witness,long(long)* callback)=>2;int main()=>Use(0,&P);","ambiguous"];
        yield return ["struct S{public static int F(long value)=>1;private static int F<T>(T value)=>42;}int main()=>S::F(1);","private"];
        yield return ["int F(long value)=>1;int F<T>(T value)=>MissingSelectedBody();int main()=>F(1);","MissingSelectedBody"];
    }

    [Theory]
    [MemberData(nameof(InvalidFamilies))]
    public void DuplicateAmbiguousAndInaccessibleCandidatesRetainSourceDiagnostics(string source,string fragment)
    {
        var error=Assert.Throws<TypeCheckException>(()=>Compiler.Check(source));
        Assert.Contains(fragment,error.Message,StringComparison.OrdinalIgnoreCase);
        if(fragment=="ambiguous")
        {
            Assert.DoesNotContain("MissingFirst",error.Message);Assert.DoesNotContain("MissingSecond",error.Message);
        }
    }

    public static IEnumerable<object[]> InapplicableCalls()
    {
        yield return ["int F<T>(T a,T b)=>42;int main()=>F(1,1l);"];
        yield return ["int F<T>(T value)=>1;int F<T,U>(T a,U b)=>2;int main()=>F<int,int,int>(1);"];
        yield return ["int F<T>(T value)=>1;int F<T>(T* value)=>2;int main()=>F(new());"];
        yield return ["interface I{int Read();}int F<T:I>(T value)=>MissingConstraintBody();int main()=>F(1);"];
        yield return ["int Identity(int value)=>value;T Identity<T>(T value)=>value;T Use<T>(T(T)* callback)=>callback(default(T));int main()=>Use(Identity);"];
        yield return ["int F<T>(T value)=>42;int main(){int(int)* F=(int value)=>value;return F<int>(1);}"];
        yield return ["int F<T>(ref T value)=>42;int main(){readonly(int) value=1;return F<int>(ref value);}"];
    }

    [Theory]
    [MemberData(nameof(InapplicableCalls))]
    public void FailedInferenceDoesNotBindInconsistentTypesOrEscapeLexicalHiding(string source)
    {
        var error=Assert.Throws<TypeCheckException>(()=>Compiler.Check(source));
        Assert.DoesNotContain("MissingConstraintBody",error.Message);
    }

    private static string Order(bool reverse,params string[] declarations)
        => string.Join("\n",reverse?declarations.Reverse():declarations);
    private static void Run(string source,int optimization)=>AssertSuccess(CompilerTestHelper.Run(source,optimization));
    private static void AssertSuccess(ExecutionResult result)
        => Assert.True(result.ExitCode==0,$"Generic overload exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
}
