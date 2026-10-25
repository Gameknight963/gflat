using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class GenericExtensionOverloadTests
{
    [Theory]
    [InlineData(false,0)]
    [InlineData(false,2)]
    [InlineData(true,0)]
    [InlineData(true,2)]
    public void ReceiverAndLaterArgumentsSelectMixedFamiliesWithoutDefinitionCacheCollisions(bool reverse,int optimization)
    {
        string kind=Order(reverse,"int Kind(this int value)=>11;","int Kind<T>(this T value)=>22;");
        string measure=Order(reverse,"int Measure<T>(this T value)=>20;","int Measure<T>(this Box<T> value)=>22;");
        string arity=Order(reverse,"int Read<T>(this Box<T> value)=>11;","int Read<T>(this Box<T> value,int tag)=>22;");
        string later=Order(reverse,"int Later<T,U>(this Box<T> value,U tag)=>22;","int Later<T>(this Box<T> value,bool tag)=>11;");
        Run($$"""
            struct Box<T>{public T Value;}
            {{kind}}
            {{measure}}
            {{arity}}
            {{later}}
            int main(){
                int value=1;long wide=2l;Box<int> box=new();
                if(value.Kind()!=11||wide.Kind()!=22||value.Kind<int>()!=22)return 1;
                if(value.Measure<int>()!=20||box.Measure<int>()!=22||box.Measure()!=22)return 2;
                if(box.Measure<int>()!=22||value.Measure<int>()!=20)return 3;
                if(box.Read()!=11||box.Read(1)!=22||box.Read()!=11)return 4;
                if(box.Later(1l)!=22||box.Later(true)!=11||box.Later<int,long>(1l)!=22)return 5;
                return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(false,0)]
    [InlineData(false,2)]
    [InlineData(true,0)]
    [InlineData(true,2)]
    public void ConstraintsExactReceiverTierAndEvaluationOrderKeepLosersDeferred(bool reverse,int optimization)
    {
        string constrained=Order(reverse,"int Check(this int value,long tag)=>42;","int Check<T:I>(this int value,T tag)=>MissingConstraintLoser();");
        string tier=Order(reverse,"int Tier<T>(this T* value,long tag)=>11;","int Tier(this ref int value,int tag)=>22;");
        string read=Order(reverse,"int Read<T>(this T* value,int tag)=>(int)*value;","int Read<T>(this ref int value,T tag)=>MissingAdaptedLoser();");
        Run($$"""
            interface I{int Read();}
            struct Good:I{public int Read()=>42;}
            {{constrained}}
            {{tier}}
            {{read}}
            int Valid(this long value)=>1;
            int Valid<T:I>(this T value)=>value.Read();
            int* Receiver(ref int steps){steps=steps*10+1;return &steps;}
            int Argument(ref int steps){steps=steps*10+2;return 0;}
            int main(){
                int value=1;if(value.Check(1)!=42)return 1;
                Good good=new();if(good.Valid()!=42)return 4;
                int* pointer=&value;if(pointer.Tier(0)!=11)return 2;
                int steps=0;
                if(Receiver(ref steps).Read(Argument(ref steps))!=12||steps!=12)return 3;
                return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(false,0)]
    [InlineData(false,2)]
    [InlineData(true,0)]
    [InlineData(true,2)]
    public void VisibleGenericFamiliesBindAliasesInTheirOwnDeclarationContexts(bool reverse,int optimization)
    {
        string declarations=Order(reverse,"int Read<T>(this Models::Box<T> value,Word tag)=>42;","int Read<T>(this T value,long tag)=>11;");
        SourceFile[] sources=
        [
            new("types.gf","namespace Models{struct Box<T>{public T Value;}}"),
            new("helpers.gf",$$"""
                namespace Helpers{alias Word=int; {{declarations}} }
                """),
            new("other.gf","namespace Other{int Read(this int value,bool tag)=>1;}"),
            new("main.gf","""
                using Helpers;using Other;alias Word=long;
                int main(){Models::Box<byte> box=new();if(box.Read(1)!=42)return 1;int value=1;if(value.Read(true)!=1)return 2;return 0;}
                """)
        ];
        AssertSuccess(CompilerTestHelper.Run(sources,optimization));
    }

    [Theory]
    [InlineData(false,0)]
    [InlineData(false,2)]
    [InlineData(true,0)]
    [InlineData(true,2)]
    public void ProductionStdAnyOverloadsRetainDifferentSpecializations(bool predicateFirst,int optimization)
    {
        string first=predicateFirst?"if(!source.AsEnumerable().Any(&IsTwo)||calls!=2)return 1;calls=0;":"if(!source.AsEnumerable().Any()||calls!=0)return 1;";
        Run($$"""
            using std; using std::Queries;
            int calls=0;
            bool IsTwo(int value){calls++;return value==2;}
            bool Missing(int value){calls++;return false;}
            int main(){
                int[3] values=[1,2,3];Span<int> source=new(values,3);
                {{first}}
                if(!source.AsEnumerable().Any()||calls!=0)return 2;
                if(!source.AsEnumerable().Any(&IsTwo)||calls!=2)return 3;
                calls=0;if(source.AsEnumerable().Any(&Missing)||calls!=3)return 4;
                if(!source.AsEnumerable().Any()||calls!=3)return 5;
                Span<int> empty=new();calls=0;
                if(empty.AsEnumerable().Any(&IsTwo)||empty.AsEnumerable().Any()||calls!=0)return 6;
                return 0;
            }
            """,optimization,standardLibrary:true);
    }

    public static IEnumerable<object[]> InvalidExtensionCalls()
    {
        yield return ["int Read<T>(this T value,int tag)=>11;int Read<T>(this ref T value,bool tag)=>22;int main(){int value=0;return value.Read(0);}","ambiguous"];
        yield return ["int Read<T>(this T* self,long tag,bool extra)=>11;int Read(this ref int self,int tag)=>22;int main(){int value=0;int* pointer=&value;return pointer.Read(0);}",""];
        yield return ["struct S{public int Read(int value)=>value;}int Read<T>(this ref T self)=>42;int main(){S value=new();return value.Read();}","expects"];
        yield return ["int Read(this ref int value)=>42;int Read<T>(this ref int value,T tag)=>1;int main(){int*? value=null;return value.Read();}",""];
        yield return ["int Read<T>(this ref T value)=>42;int Read<U>(this ref U value)=>1;int main()=>0;","cannot differ"];
        yield return ["interface I{int Read();}int Read<T:I>(this T value)=>MissingLoser();int main(){int value=1;return value.Read();}",""];
    }

    [Theory]
    [MemberData(nameof(InvalidExtensionCalls))]
    public void GenericExtensionsKeepReceiverShapeAndMemberPrecedenceDiagnostics(string source,string fragment)
    {
        var error=Assert.Throws<TypeCheckException>(()=>Compiler.Check(source));
        if(fragment.Length!=0)Assert.Contains(fragment,error.Message,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MissingLoser",error.Message);
    }

    private static string Order(bool reverse,params string[] declarations)
        => string.Join("\n",reverse?declarations.Reverse():declarations);
    private static void Run(string source,int optimization,bool standardLibrary=false)
    {
        if(!standardLibrary){AssertSuccess(CompilerTestHelper.Run(source,optimization));return;}
        var files=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("generic-extension.gf",source));
        AssertSuccess(CompilerTestHelper.Run(files,optimization));
    }
    private static void AssertSuccess(ExecutionResult result)
        => Assert.True(result.ExitCode==0,$"Generic extension overload exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
}
