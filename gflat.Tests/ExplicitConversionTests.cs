using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ExplicitConversionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void StructAndClassConversionsAreExplicitAndEvaluateOperandsOnce(int optimization)
        => Run("""
            int calls=0;
            struct Meter {
                public int Value;public Meter(int value)nothrow{Value=value;}
                public static explicit operator int(Meter value)nothrow=>value.Value;
                public static explicit operator Meter(int value)nothrow=>new Meter(value);
            }
            class Box {
                public int Value;public Box(int value)nothrow{Value=value;}
                public static explicit operator int(Box value)nothrow=>value.Value;
                public static explicit operator Box(int value)nothrow=>new Box(value);
            }
            Meter Make()nothrow{calls++;return new Meter(42);}
            int main()nothrow{
                if((int)Make()!=42||calls!=1)return 1;
                Meter meter=(Meter)17;if(meter.Value!=17||(int)meter!=17)return 2;
                Box box=(Box)29;if(box.Value!=29||(int)box!=29)return 3;
                return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MoveOnlyConversionArgumentsAndFreshOwningResultsUseNormalMethodCleanup(int optimization)
        => Run("""
            int destroyed=0;int calls=0;
            struct Resource {
                public int Value;public Resource(int value)nothrow{Value=value;}
                ~Resource(){destroyed++;}
                public static explicit operator int(Resource value)nothrow{calls++;return value.Value;}
                public static explicit operator Resource(int value)nothrow=>new Resource(value);
            }
            int main()nothrow{
                {Resource source=new(7);if((int)move source!=7||destroyed!=1||calls!=1)return 1;}
                if(destroyed!=1)return 2;
                {Resource result=(Resource)9;if(result.Value!=9||destroyed!=1)return 3;}
                if(destroyed!=2)return 4;
                return 0;
            }
            """,optimization);

    [Theory]
    [InlineData("struct Box{public static explicit operator long(int value)=>value;}","containing")]
    [InlineData("struct Box{public static explicit operator Box(Box value)=>value;}","built-in")]
    [InlineData("alias Word=int;struct Box{public int Value;public static explicit operator int(Box value)=>value.Value;public static explicit operator Word(Box value)=>value.Value;}","Duplicate")]
    [InlineData("struct A{public static explicit operator B(A value)=>new B();}struct B{public static explicit operator B(A value)=>new B();}void Probe(){A value=new();B result=(B)value;}","ambiguous")]
    [InlineData("struct Box{public static explicit operator int(Box value)=>0;}void Probe(){Box value=new();int number=value;}","cannot")]
    public void InvalidOrImplicitUsesRejectWithSourceFacingDiagnostics(string source,string fragment)
    {
        var error=Assert.Throws<TypeCheckException>(()=>Compiler.Check(source,options:new(){NoStdLib=true,NoBuiltins=true}));
        Assert.Contains(fragment,error.Message,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("$conversion$",error.Message,StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void GenericAndConstantConversionsUseTheContainingSpecialization(int optimization)
        => Run("""
            struct Box<T> {
                public T Value;
                public const Box(T value) {Value=value;}
                public static const explicit operator T(Box<T> value)=>value.Value;
                public static const explicit operator Box<T>(T value)=>new Box<T>(value);
            }
            const int Answer=(int)(Box<int>)42;
            int main(){Box<long> value=(Box<long>)17l;if((long)value!=17l)return 1;return Answer-42;}
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ThrowingConversionsPropagateAndPreserveOrdinaryCleanup(int optimization)
        => Run("""
            int destroyed=0;
            struct Resource {
                public Resource(){}
                ~Resource(){destroyed++;}
                public static explicit operator int(Resource value){throw new Exception("conversion failed");}
            }
            int main(){
                Resource value=new();
                try {int unused=(int)move value;return 1;}
                catch(Exception* error){if(destroyed!=1)return 2;return 0;}
                return 3;
            }
            """,optimization);

    private static void Run(string source,int optimization)
    {
        string[] names=["core/Exception.gf","libc/Memory/allocation.gf","libc/Exceptions/unhandled.gf"];
        var sources=names.Select(name=>new SourceFile(name,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Standard",name))))
            .Append(new SourceFile("explicit-conversion-tests.gf",source));
        string ir=Compiler.Emit(sources,options:new(){NoStdLib=true,NoBuiltins=true,InternalizeFunctions=true});
        _=CompilerTestHelper.RunIr(ir,optimization,expectedExitCode:0);
    }
}

