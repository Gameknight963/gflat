using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace gflat.Tests;

public sealed class StandardExtraLongHexFormattingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AllBitsAndCapacityFailuresMatchIndependentBigIntegerOracle(int optimization)
    {
        BigInteger mask=(BigInteger.One<<128)-1;
        List<BigInteger> values=[0,1,15,16,(BigInteger.One<<127)-1,BigInteger.One<<127,mask];
        for(int shift=0;shift<128;shift+=7)values.Add(BigInteger.One<<shift);
        var random=new Random(17821);
        for(int i=0;i<32;i++){byte[] bytes=new byte[16];random.NextBytes(bytes);values.Add(new BigInteger(bytes,isUnsigned:true));}
        var code=new StringBuilder("int main(){char[43] output;nuint written=0;\n");
        foreach(BigInteger bits in values)
        {
            ulong high=(ulong)(bits>>64),low=(ulong)(bits&ulong.MaxValue);
            foreach(int width in new[]{0,32,40})foreach(bool uppercase in new[]{true,false})
            {
                string expected=bits.ToString(uppercase?"X":"x").TrimStart('0');
                if(expected.Length==0)expected="0";
                expected=expected.PadLeft(width,'0');
                code.AppendLine($$"""
                    {extralong value=((extralong){{high}}ul<<64)|(extralong){{low}}ul;
                     readonly(char)* expected="{{expected}}";
                     for(nuint capacity=0;capacity<={{expected.Length+1}};capacity++){
                      for(nuint i=0;i<43;i++)output[i]='Z';written=99;
                      bool success=std::IntegerFormatting::TryFormatHex(value,&output[1],capacity,ref written,{{width}},{{(uppercase?"true":"false")}});
                      if(success!=(capacity>={{expected.Length}}))return 1;
                      if(!success){if(written!=0)return 2;for(nuint i=0;i<43;i++)if(output[i]!='Z')return 3;}
                      else{if(written!={{expected.Length}}||output[0]!='Z')return 4;
                       for(nuint i=0;i<written;i++)if(output[i+1]!=expected[i])return 5;
                       for(nuint i=written+1;i<43;i++)if(output[i]!='Z')return 6;}
                     } }
                    """);
            }
        }
        code.AppendLine("""
            if(std::IntegerFormatting::TryFormatHex((extralong)0,(char*)1,0,ref written)||written!=0)return 7;
            if(std::IntegerFormatting::TryFormatHex((extralong)-1,output,43,ref written,(nuint)0-(nuint)1)||written!=0)return 8;
            return 0;}
            """);
        var options=new CompilationOptions{NoStdLib=true,NoBuiltins=true,InternalizeFunctions=true};
        string ir=Compiler.Emit(Sources(code.ToString()),options:options);
        Assert.DoesNotMatch(@"\b(?:sdiv|udiv|srem|urem)\s+i128\b",ir);
        Assert.DoesNotContain("@__gflat_eh_",ir);
        Match method=Regex.Match(ir,@"(?ms)^define [^\r\n]*@[^\r\n]*TryFormatHex[^\r\n]*i128[^\r\n]*\{\r?\n(?<body>.*?)^\}");
        Assert.True(method.Success);Assert.Contains("nounwind",method.Value);Assert.Contains("\"no-builtins\"",method.Value);
        Assert.Equal(0,CompilerTestHelper.Run(Sources(code.ToString()),optimization).ExitCode);
    }

    private static IEnumerable<SourceFile> Sources(string code)
        => new[]{new SourceFile("IntegerFormatting.gf",File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core/IntegerFormatting.gf"))),new SourceFile("main.gf",code)};
}
