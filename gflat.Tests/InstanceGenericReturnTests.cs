using gflat.ast;
using gflat.CompileExceptions;
using gflat.diagnostics;

namespace gflat.Tests;

public sealed class InstanceGenericReturnTests
{
    private const string Box = "struct Box<T>{public T value;public Box(T item){value=item;}}";
    private static string Helpers(string kind) => Box + kind + " Helpers{" +
        "public Box<T> Make<T>(Box<T> value)=>value;public Box<T>* Pointer<T>(Box<T>* value)=>value;}";

    [Theory]
    [InlineData("class", false, 0)]
    [InlineData("class", false, 2)]
    [InlineData("class", true, 0)]
    [InlineData("class", true, 2)]
    [InlineData("struct", false, 0)]
    [InlineData("struct", false, 2)]
    [InlineData("struct", true, 0)]
    [InlineData("struct", true, 2)]
    public void ConstructedAndPointerReturnsBindBeforeChainedUse(string kind, bool explicitArguments, int optimization)
    {
        string arguments = explicitArguments ? "<int>" : "";
        Run(Helpers(kind) + $$"""
            int main(){
                Helpers helper=new();Box<int> input=new(42);
                var result=helper.Make{{arguments}}(input);
                if(result.value!=42||helper.Make{{arguments}}(input).value!=42)return 1;
                var pointer=helper.Pointer{{arguments}}(&input);
                return pointer.value-42;
            }
            """, optimization);
    }

    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    public void CheckedCallAndCalleeBothHaveTheRealClosedReturn(string kind)
    {
        string text=Helpers(kind)+"int main(){Helpers helper=new();Box<int> input=new(42);return helper.Make<int>(input).value;}";
        var source=new SourceFile("instance-return-types.gf",text);
        var snapshot=new AnalysisSnapshot([source],new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,d=>d.Severity==DiagnosticSeverity.Error);
        var call=Assert.Single(snapshot.Nodes.Select(entry=>entry.Node).OfType<CallExpression>(),
            call=>call.Callee is MemberAccessExpression{Member:"Make"});
        var result=Assert.IsType<NamedTypeExpression>(snapshot.Checker!.GetType(call));
        var callee=Assert.IsType<NamedTypeExpression>(snapshot.Checker.GetType(call.Callee));
        Assert.Equal(result.Name,callee.Name);
        Assert.Equal("Box<int>",snapshot.Checker.DisplayTypeName(result));
        Assert.NotNull(snapshot.Checker.GetTypeDeclaration(result.Name));
        Assert.NotNull(snapshot.Checker.GetResolvedCall(call));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void GenericOwnerShadowingInheritanceAndOverloadsRetainTheirBindings(int optimization)
        => Run(Box+"""
            class Helpers<T>{
                public Box<T> Keep(Box<T> value)=>value;
                public Box<T> Make<T>(Box<T> value)=>value;
                public int Make(int value)=>value;
            }
            class Base<A>{public Box<T> Make<T>(Box<T> value)=>value;}
            class Derived:Base<long>{}
            struct ReadOnlyHelper{public readonly Box<T> Read<T>(Box<T> value)=>value;}
            interface IFactory{Box<int> Make(Box<int> value);}
            struct Factory:IFactory{public Box<int> Make(Box<int> value)=>value;}
            int main(){
                Helpers<long> helper=new();Box<int> input=new(42);Box<long> wide=new(43l);
                if(helper.Make<int>(input).value!=42||helper.Make(input).value!=42||helper.Make(44)!=44)return 1;
                if(helper.Keep(wide).value!=43l)return 2;
                Derived inherited=new();if(inherited.Make(input).value!=42)return 3;
                Factory concrete=new();IFactory* factory=&concrete;if(factory.Make(input).value!=42)return 4;
                readonly(ReadOnlyHelper) protectedHelper=new();
                return protectedHelper.Read(input).value-42;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PointerFieldsPropertiesAndGenericReturnedPointersKeepExpectedTyping(int optimization)
        => Run(Box+"""
            int Increment(int value)=>value+1;
            T Identity<T>(T value)=>value;
            Box<T> Free<T>(Box<T> value)=>value;
            static class Functions{public static Box<T> Make<T>(Box<T> value)=>value;}
            struct Holder{
                public int(int)* target;
                public int(int)* Callback{get=>target;}
                public Holder(int(int)* callback){target=callback;}
                public T(T)* Wrap<T>(T(T)* callback)=>callback;
            }
            int main(){
                Holder holder=new(Increment);
                if(holder.target(41)!=42||holder.Callback(41)!=42)return 1;
                int(int)* produced=holder.Wrap<int>(Identity);
                if(produced(42)!=42||holder.Wrap<int>(Increment)(41)!=42)return 2;
                Box<int>(Box<int>)* free=Free;
                Box<int>(Box<int>)* selected=&Functions::Make;
                Box<int> input=new(42);
                if(free(input).value!=42||selected(input).value!=42)return 3;
                return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ReceiverIsEvaluatedOnceBeforeArguments(int optimization)
    {
        string text=Box+"""
            extern int putchar(int value);
            struct Receiver{public Box<T> Make<T>(Box<T> value){putchar(77);return value;}}
            Receiver receiver;
            Receiver* GetReceiver(){putchar(82);return &receiver;}
            Box<int> Input(){putchar(65);return new Box<int>(42);}
            int main(){var result=GetReceiver().Make<int>(Input());return result.value-42;}
            """;
        var result=CompilerTestHelper.Run(text,optimization);
        Assert.Equal(0,result.ExitCode);
        Assert.Equal("RAM",result.StandardOutput);
        Assert.Empty(result.StandardError);
    }

    [Theory]
    [InlineData("class Helpers{private Box<T> Make<T>(Box<T> value)=>value;public int Make(int value)=>value;}","Helpers helper=new();helper.Make<int>(input);")]
    [InlineData("class Helpers{public Box<T> Make<T>(Box<T> value)=>value;}","Helpers helper=new();helper.Make<long>(input);")]
    [InlineData("interface Mark{}class Helpers{public Box<T> Make<T:Mark>(Box<T> value)=>value;}","Helpers helper=new();helper.Make<int>(input);")]
    [InlineData("struct Helpers{public Box<T> Make<T>(Box<T> value)=>value;}","immutable(Helpers) helper=new();helper.Make<int>(input);")]
    [InlineData("struct Helpers{public Box<T> Make<T>(Box<T> value)=>value;}","Helpers*? helper=null;helper.Make<int>(input);")]
    [InlineData("struct Helpers{public Box<T> Make<T>(Box<T> value)=>value;}","Helpers helper=new();var unused=helper.Make;")]
    [InlineData("struct Helpers{public Box<T> Make<T>(Box<T> value)=>value;}","Helpers helper=new();var unused=&helper.Make;")]
    [InlineData("struct Helpers{public Box<T> Make<T>(Box<T> value)=>value;}","Helpers.Make<int>(input);")]
    public void InvalidCallsAndUnboundInstanceValuesStillReject(string declarations,string statement)
        => Assert.Throws<TypeCheckException>(()=>Compiler.Check(Box+declarations+
            "int main(){Box<int> input=new(42);"+statement+"return 0;}",options:new(){NoStdLib=true}));

    [Theory]
    [InlineData("class",false)]
    [InlineData("class",true)]
    [InlineData("struct",false)]
    [InlineData("struct",true)]
    public void UnrelatedGlobalTDoesNotMakeAnUnboundMethodAValue(string kind,bool address)
    {
        string expression=(address?"&":"")+"helper.Make";
        var error=Assert.Throws<TypeCheckException>(()=>Compiler.Check("class T{public int Bogus;}"+Helpers(kind)+
            "int main(){Helpers helper=new();var unused="+expression+";return 0;}",options:new(){NoStdLib=true}));
        Assert.Equal("Unspecialized generic member 'Make' cannot be used as a value",error.Description);
    }

    [Fact]
    public void NameofStillUsesItsSeparateSourceMethodBinding()
        => Compiler.Check("class T{}"+Helpers("struct")+
            "int main(){Helpers helper=new();readonly(char)* name=nameof(helper.Make);return 0;}",options:new(){NoStdLib=true});

    private static void Run(string text,int optimization)
    {
        var result=CompilerTestHelper.Run(text,optimization);
        Assert.True(result.ExitCode==0,$"Exit {result.ExitCode}: {result.StandardError}");
        Assert.Empty(result.StandardOutput);
    }
}
