using System.Numerics;
using System.Text;
using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class UnaryBitwiseNotTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void IntegralComplementsPreserveWidthAndAgreeWithConstantEvaluation(int optimization)
    {
        (string Name,int Bits,bool Signed)[] types=[("byte",8,false),("sbyte",8,true),("char",8,true),("short",16,true),("ushort",16,false),
            ("int",32,true),("uint",32,false),("long",64,true),("ulong",64,false),("nint",IntPtr.Size*8,true),("nuint",IntPtr.Size*8,false),("extralong",128,true)];
        var declarations=new StringBuilder();var checks=new StringBuilder();int index=0;
        foreach(var type in types){
            int tag=++index;declarations.Append($"int Pick({type.Name} value)=>{tag};");
            BigInteger modulus=BigInteger.One<<type.Bits,mask=modulus-1;
            BigInteger[] inputs=[0,1,mask,(BigInteger.One<<(type.Bits-1)),mask/3];
            foreach(var bits in inputs.Distinct()){
                BigInteger expected=mask^bits;if(type.Signed&&expected>=(modulus>>1))expected-=modulus;
                string input=$"({type.Name})({Wide(bits)})",answer=Wide(expected);
                declarations.Append($"const bool Fold{tag}_{bits}(){{{type.Name} value={input};return (extralong)~value=={answer} && (extralong)~~value==(extralong)value;}}const bool folded{tag}_{bits}=Fold{tag}_{bits}();");
                checks.Append($"{{immutable({type.Name}) value={input};if(!folded{tag}_{bits}||(extralong)~value!={answer}||Pick(~value)!={tag}||(extralong)~~value!=(extralong)value)return {tag};}}");
            }
        }
        var result=CompilerTestHelper.Run(declarations+"int main(){"+checks+"if(~0!=-1||~(nuint)0!=(nuint)0-(nuint)1)return 20;return 0;}",optimization);
        Assert.True(result.ExitCode==0,$"Unary integral complement exit {result.ExitCode}: {result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EnumAndAliasComplementsRetainTheirNominalTypeAndUnderlyingWidth(int optimization)
    {
        var result=CompilerTestHelper.Run("""
            enum Flags : byte { None=0, First=1, All=255 }
            enum Signed : char { Zero=0 }
            alias Alias=Flags;alias Native=nuint;
            const bool Fold(){Alias flags=Flags::First;Signed signed=(Signed)(char)233;return (int)~flags==254&&(int)~signed==22&&~(Native)0==(Native)0-(Native)1;}
            const bool folded=Fold();const Flags inverse=(Alias)~Flags::First;
            int Pick(Flags value)=>1;int Pick(byte value)=>2;
            int main(){Alias flags=Flags::First;Signed signed=(Signed)(char)233;Native zero=0;
                if(!folded||inverse!=(Flags)254||Pick(~flags)!=1||(int)~flags!=254||(int)~signed!=22||~zero!=(Native)0-(Native)1)return 1;return 0;}
            """,optimization);
        Assert.Equal(0,result.ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CustomOperatorAndBuiltinOperandsEvaluateOnceWithoutChangingDestructorSyntax(int optimization)
    {
        var result=CompilerTestHelper.Run("""
            int evaluations=0;int operators=0;int destroyed=0;
            struct Bits{
                public int Value;public Bits(int value){Value=value;}
                public static Bits operator ~(Bits value){operators++;return new Bits(~value.Value);}
            }
            struct Item{public int Value;public Item(int value){Value=value;}~Item(){destroyed+=Value;}}
            Bits Once(){evaluations++;return new Bits(20);}
            int Scalar(){evaluations++;return 41;}
            void Destroy<T>(T* value){value.~T();}
            int main(){Bits result=~Once();if(result.Value!=-21||evaluations!=1||operators!=1)return 1;
                if(~Scalar()!=-42||evaluations!=2)return 2;int number=7;int* pointer=&number;
                if(~*pointer++!=-8||pointer!=&number+1||number!=7)return 3;
                Item* item=new* Item(3);item.~Item();new* Item(5) at item;Destroy<Item>(item);Memory::Free((void*)item);
                if(destroyed!=8)return 4;return 0;}
            """,optimization);
        Assert.Equal(0,result.ExitCode);
    }

    [Theory]
    [InlineData("bool value=true;", "value")]
    [InlineData("float value=1.0f;", "value")]
    [InlineData("double value=1.0;", "value")]
    [InlineData("int value=1;int* pointer=&value;", "pointer")]
    [InlineData("int*? value=null;", "value")]
    [InlineData("int^? value=null;", "value")]
    [InlineData("struct Value{}", "new Value()")]
    public void NonintegralBuiltinOperandsAreRejected(string declaration,string operand)
    {
        string program=declaration.StartsWith("struct",StringComparison.Ordinal)?declaration+"void Probe(){~"+operand+";}":"void Probe(){"+declaration+"~"+operand+";}";
        Assert.Contains("integral",Assert.Throws<TypeCheckException>(()=>Compiler.Check(program)).Message,StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Bits left, Bits right")]
    [InlineData("")]
    public void ComplementOverloadsRequireOneOperand(string parameters)
        =>Assert.Contains("exactly 1",Assert.Throws<TypeCheckException>(()=>Compiler.Check("struct Bits{public static Bits operator ~("+parameters+")=>default;}")).Message);

    [Fact]
    public void PrivateComplementOverloadsCannotBeUsedOutsideTheirOwner()
        =>Assert.Contains("private",Assert.Throws<TypeCheckException>(()=>Compiler.Check("struct Bits{private static Bits operator ~(Bits value)=>value;}int main(){Bits value=default;~value;return 0;}")).Message,StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void BuiltinComplementUsesOnlyExactWidthXorWithoutWideRuntimeHelpers()
    {
        string ir=Compiler.Emit("byte Small(byte value)=>~value;extralong Wide(extralong value)=>~value;nuint Native(nuint value)=>~value;int main()=>0;",options:new(){NoStdLib=true});
        Assert.Contains("xor i8",ir);Assert.Contains("xor i128",ir);Assert.Contains("xor i"+(IntPtr.Size*8),ir);
        Assert.False(NativeToolchain.RequiresWideIntegerRuntime(ir));Assert.DoesNotContain("__gflat_eh_",ir);
    }

    private static string Wide(BigInteger value)
    {
        BigInteger modulus=BigInteger.One<<128;value=((value%modulus)+modulus)%modulus;
        ulong low=(ulong)(value&ulong.MaxValue),high=(ulong)(value>>64);
        return $"(((extralong){high}ul<<64)|(extralong){low}ul)";
    }
}
