using gflat.CompileExceptions;

namespace gflat.Tests.Namespaces;

public sealed class DeclaredExtensionPrecedenceTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 2)]
    public void ConstrainedCallsIgnoreHiddenMembersAcrossWarmOrders(bool reverseWarmOrder, int optimization)
    {
        string direct = "if (hidden.Identify()!=7 || Duck(ref hidden)!=7) return 1;";
        string constrained = "if (Forward(ref hidden)!=42 || Forward(ref plain)!=43) return 2;";
        string source = """
            interface IValue { immutable int Read(); }
            struct Hidden:IValue {
                public immutable int Read()=>42;
                public immutable int Identify()=>7;
            }
            struct Plain:IValue { public immutable int Read()=>43; }
            weak alias Selected:IValue=Hidden;
            int Identify<T:IValue>(this ref immutable(T) value)=>value.Read();
            int Forward<T:IValue>(ref immutable(T) value)=>value.Identify();
            int Duck<T>(ref immutable(T) value)=>value.Identify();
            int main(){
                Hidden hidden=new(); Plain plain=new();
            """ + (reverseWarmOrder ? constrained + direct : direct + constrained) + """
                Selected selected=new();
                if(selected.Identify()!=42)return 3;
                if(hidden.Identify()!=7 || Forward(ref hidden)!=42)return 4;
                return 0;
            }
            """;
        var result=CompilerTestHelper.Run(source,optimization);
        Assert.True(result.ExitCode==0,$"Exit {result.ExitCode}: {result.StandardError}");
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 2)]
    public void HiddenInheritedContractCallableShapesDoNotSuppressExtensions(bool property, int optimization)
    {
        string member=property ? "public Callback Identify { immutable get=>Actual; }" :
            "public Callback Identify; public Derived(){Identify=Actual;}";
        string source="""
            alias Callback=int()*;
            int Actual()=>7;
            class Base { public immutable int Read()=>42; }
            class Derived:Base {
            """ + member + """
            }
            int Identify<T:Base>(this ref immutable(T) value)=>value.Read();
            int Forward<T:Base>(ref immutable(T) value)=>value.Identify();
            weak alias Selected:Base=Derived;
            int main(){
                Derived concrete=new();
                if(concrete.Identify()!=7)return 1;
                if(Forward(ref concrete)!=42)return 2;
                Selected selected=new();
                if(selected.Identify()!=42)return 3;
                return 0;
            }
            """;
        var result=CompilerTestHelper.Run(source,optimization);
        Assert.True(result.ExitCode==0,$"Exit {result.ExitCode}: {result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ExposedMemberNamesStillTakePrecedence(int optimization)
    {
        var result=CompilerTestHelper.Run("""
            interface IValue { immutable int Identify(); }
            struct Value:IValue { public immutable int Identify()=>7; }
            int Identify<T:IValue>(this ref immutable(T) value)=>42;
            int Forward<T:IValue>(ref immutable(T) value)=>value.Identify();
            int main(){Value value=new();if(Forward(ref value)!=7)return 1;return 0;}
            """,optimization);
        Assert.Equal(0,result.ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void GenericFormattableBorrowOfStringReturnsIndependentCountedOwner(int optimization)
    {
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path)))
            .Append(new SourceFile("generic-string-borrow.gf","""
                using std;
                using std::Text;
                String Borrow<T:IFormattable>(ref immutable(T) value)=>value.ToString();
                int main(){
                    String original=String::From("a\0b",3);
                    String result=Borrow(ref original);
                    if(result.Length!=3 || result.Data==original.Data)return 1;
                    if(result.Data[0]!='a' || result.Data[1]!='\0' || result.Data[2]!='b' || result.Data[3]!='\0')return 2;
                    original[0]='x';
                    if(result.Data[0]!='a' || original.Data[0]!='x')return 3;
                    return 0;
                }
                """));
        var result=CompilerTestHelper.Run(sources,optimization);
        Assert.True(result.ExitCode==0,$"Exit {result.ExitCode}: {result.StandardError}");
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VisibleStaticNameCannotBeRescuedByAnExtension(bool constrained)
    {
        string source="""
            interface IValue { static int Identify(); }
            struct Value:IValue { public static int Identify()=>7; }
            int Identify<T:IValue>(this ref immutable(T) value)=>42;
            int Forward<T:IValue>(ref immutable(T) value)=>value.Identify();
            int main(){Value value=new();return
            """ + " " + (constrained ? "Forward(ref value);}" : "value.Identify();}");
        var error=Assert.Throws<TypeCheckException>(()=>Compiler.Check(source,options:new(){NoStdLib=true}));
        Assert.Contains("Identify",error.Message);
        Assert.Contains(constrained ? "No matching declared member" : "Static method",error.Message);
    }
    [Fact]
    public void ExposedWrongArityMemberCannotFallBackToExtension()
    {
        var error=Assert.Throws<TypeCheckException>(()=>Compiler.Check("""
            interface IValue { immutable int Identify(int argument); }
            struct Value:IValue { public immutable int Identify(int argument)=>argument; }
            int Identify<T:IValue>(this ref immutable(T) value)=>42;
            int Forward<T:IValue>(ref immutable(T) value)=>value.Identify();
            int main(){Value value=new();return Forward(ref value);}
            """,options:new(){NoStdLib=true}));
        Assert.Contains("No matching overload",error.Message);
        Assert.Contains("Identify",error.Message);
    }
}
