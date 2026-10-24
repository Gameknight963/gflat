using System.Text;
using gflat.CompileExceptions;

namespace gflat.Tests;

// Declaration initializers, not general
// array common-type inference, overload guessing or numeric promotion.
public sealed class ArrayDeclarationLiteralContextTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DeclaredElementTargetsAcceptLiteralAndTypedValuePermutationsAtTheirActualWidths(int optimization)
    {
        var cases=new (string Type,string Low,string Value,string High)[]{
            ("nuint","0","17","42"),("nint","-1","-17","37"),("byte","0","200","255"),("ushort","0","60000","65535")};
        int[][] permutations=[[0,1,2],[0,2,1],[1,0,2],[1,2,0],[2,0,1],[2,1,0]];
        var code=new StringBuilder("int main(){if(sizeof(byte)!=1||sizeof(ushort)!=2||sizeof(nuint)!=sizeof(void*)||sizeof(nint)!=sizeof(void*))return 1;\n");
        foreach(var item in cases){
            string[] values=[item.Low,"typed",item.High];string[] expected=[item.Low,item.Value,item.High];
            foreach(int[] order in permutations){
                code.AppendLine($"{{{item.Type} typed={item.Value};{item.Type}[3] values=["+string.Join(',',order.Select(index=>values[index]))+"];");
                code.AppendLine($"if(sizeof(values)!=3*sizeof({item.Type}))return 2;");
                for(int i=0;i<3;i++)code.AppendLine($"if(values[{i}]!={expected[order[i]]})return 3;");
                code.AppendLine($"if(typed!={item.Value})return 4;}}");
            }
        }
        code.AppendLine("return 0;}");
        Run(code.ToString(),optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AliasesOuterInferredBoundsAndNestedFixedDimensionsKeepOrdinaryElementAndShapeRules(int optimization)
        =>Run("""
            alias Count=nuint;alias Signed=nint;alias Octet=byte;alias Half=ushort;alias Row=ushort[3];
            Count Unsigned()=>17;Signed Negative()=>-17;Octet Narrow()=>200;Half Wide()=>60000;
            int Increment(int value)=>value+1;
            int calls=0;byte Next(){calls++;return (byte)calls;}
            int main(){
                Count typed=17;Count[] counts=[0,Unsigned(),typed+1];Signed[] signedValues=[-1,Negative(),0];
                Octet[] bytes=[0,Narrow(),255];Half[] halves=[0,65535,Wide()];
                if(sizeof(counts)!=3*sizeof(nuint)||counts[0]!=0||counts[1]!=17||counts[2]!=18)return 1;
                if(sizeof(signedValues)!=3*sizeof(nint)||signedValues[0]!=-1||signedValues[1]!=-17||signedValues[2]!=0)return 2;
                if(sizeof(bytes)!=3||bytes[0]!=0||bytes[1]!=200||bytes[2]!=255||sizeof(halves)!=6||halves[0]!=0||halves[1]!=65535||halves[2]!=60000)return 3;
                Octet[3] row=[0,Narrow(),255];Octet[3][2] nested=[row,[1,2,Narrow()]];
                if(sizeof(nested)!=6||nested[0][0]!=0||nested[0][1]!=200||nested[0][2]!=255||nested[1][0]!=1||nested[1][1]!=2||nested[1][2]!=200)return 4;
                Row[] inferred=[[0,1,Wide()],[Wide(),65535,2]];
                if(sizeof(inferred)!=12||inferred[0][0]!=0||inferred[0][2]!=60000||inferred[1][0]!=60000||inferred[1][1]!=65535||inferred[1][2]!=2)return 5;
                Count[2][] nativeRows=[[0,Unsigned()],[Unsigned(),1]];
                if(sizeof(nativeRows)!=4*sizeof(nuint)||nativeRows[0][1]!=17||nativeRows[1][0]!=17||nativeRows[1][1]!=1)return 6;
                const byte[3] constant=[0,1,255];if(constant[0]!=0||constant[1]!=1||constant[2]!=255)return 7;
                long[2] ordinaryWide=[1,2];if(ordinaryWide[0]!=1l||ordinaryWide[1]!=2l||sizeof(ordinaryWide)!=2*sizeof(long))return 8;
                nuint high=(nuint)1<<((int)sizeof(nuint)*8-2);nuint maximum=(nuint)0-(nuint)1;nuint[] nativeWide=[0,high,maximum];
                nint highSigned=(nint)high;nint[] signedWide=[0,highSigned,-17];
                if(nativeWide[1]!=high||nativeWide[2]!=maximum||signedWide[1]!=highSigned||signedWide[2]!=-17)return 9;
                byte[] ordered=[0,Next(),Next(),3];if(calls!=2||ordered[0]!=0||ordered[1]!=1||ordered[2]!=2||ordered[3]!=3)return 10;
                int first=42;int second=7;readonly(int)*[2] readers=[&first,&second];
                if(*readers[0]!=42||*readers[1]!=7)return 11;readers[0]=&second;if(*readers[0]!=7||first!=42||second!=7)return 12;
                short negative=-2;long[3] signedWidening=[0,negative,1];byte positive=255;long[3] unsignedWidening=[0,positive,1];
                if(signedWidening[1]!=-2l||unsignedWidening[1]!=255l||sizeof(signedWidening)!=3*sizeof(long)||sizeof(unsignedWidening)!=3*sizeof(long))return 13;
                int(int)*[2] callbacks=[Increment,&Increment];if(callbacks[0](41)!=42||callbacks[1](5)!=6)return 14;
                return 0;
            }
            """,optimization);

    public static IEnumerable<object[]> InvalidContexts()
    {
        foreach(string body in new[]{
            "byte value=256;","byte[1] values=[256];","byte[1] values=[-1];",
            "ushort value=65536;","ushort[1] values=[65536];",
            "nuint value=-1;","nuint[1] values=[-1];",
            "nint value=18446744073709551615ul;","nint[1] values=[18446744073709551615ul];",
            "byte[3] values=[1,2];","nuint[2][2] values=[[0,1]];","ushort[2][2] values=[[1],[2,3]];",
            "byte[2] row=[1,2];byte[3][2] values=[row,[3,4,5]];","byte[] values=[];",
            "int typed=1;nuint[2] values=[0,typed];",
            "int[2] source=[1,2];nuint[2] destination=source;",
            "int value=1;readonly(int)* pointer=&value;int*[2] values=[pointer,&value];",
            "int value=1;readonly(int)*[1] values=[&value];*values[0]=2;",
            "nuint typed=1;var values=[0,typed];","nuint typed=1;var values=[typed,0];",
            "byte typed=1;var values=[typed,2];","var values=[1,2l];"
        })yield return ["int main(){"+body+"return 0;}"];
        yield return ["int Pick(byte[2] values)=>1;int Pick(ushort[2] values)=>2;int main()=>Pick([1,2]);"];
    }

    [Theory]
    [MemberData(nameof(InvalidContexts))]
    public void DeclaredTargetsDoNotRelaxScalarRangesFixedShapesPointerCapabilitiesOrUntargetedInference(string source)
        =>Assert.Throws<TypeCheckException>(()=>Compiler.Check(source));

    private static void Run(string source,int optimization)
    {
        var result=CompilerTestHelper.Run(source,optimization);
        Assert.True(result.ExitCode==0,$"Array declaration literal context exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
