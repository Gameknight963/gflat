using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace gflat.Tests;

public sealed class StandardExtraLongHexFormattingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task AllBitsAndCapacityFailuresMatchIndependentBigIntegerOracle(int optimization)
    {
        BigInteger mask=(BigInteger.One<<128)-1;
        List<BigInteger> values=[0,1,15,16,(BigInteger.One<<127)-1,BigInteger.One<<127,mask];
        for(int shift=0;shift<128;shift+=7)values.Add(BigInteger.One<<shift);
        var random=new Random(17821);
        for(int i=0;i<32;i++){byte[] bytes=new byte[16];random.NextBytes(bytes);values.Add(new BigInteger(bytes,isUnsigned:true));}
        var oracle=new StringBuilder();
        int caseCount=0,oracleBytes=0;
        foreach(BigInteger bits in values)
        {
            foreach(int width in new[]{0,32,40})foreach(bool uppercase in new[]{true,false})
            {
                string expected=bits.ToString(uppercase?"X":"x").TrimStart('0');
                if(expected.Length==0)expected="0";
                expected=expected.PadLeft(width,'0');
                oracle.Append(expected).Append("\\0");caseCount++;oracleBytes+=expected.Length+1;
            }
        }
        string high=string.Join(",",values.Select(bits=>((ulong)(bits>>64)).ToString(System.Globalization.CultureInfo.InvariantCulture)+"ul"));
        string low=string.Join(",",values.Select(bits=>((ulong)(bits&ulong.MaxValue)).ToString(System.Globalization.CultureInfo.InvariantCulture)+"ul"));
        // One runtime loop executes the same oracle rows and every capacity;
        // repeating the whole body per row makes LLVM compile hundreds of copies.
        string code=$$"""
            int main(){
                ulong[{{values.Count}}] high=[{{high}}];ulong[{{values.Count}}] low=[{{low}}];
                int[3] widths=[0,32,40];readonly(char)* oracle="{{oracle}}";
                char[43] output;nuint written=0;nuint cursor=0;int cases=0;
                for(nuint index=0;index<{{values.Count}};index++){
                    extralong value=((extralong)high[index]<<64)|(extralong)low[index];
                    for(nuint width=0;width<3;width++)for(int letterCase=0;letterCase<2;letterCase++){
                        readonly(char)* expected=oracle+cursor;nuint length=0;
                        while(expected[length]!='\0')length++;
                        for(nuint capacity=0;capacity<=length+1;capacity++){
                            for(nuint i=0;i<43;i++)output[i]='Z';written=99;
                            bool success=std::IntegerFormatting::TryFormatHex(value,&output[1],capacity,ref written,(nuint)widths[width],letterCase==0);
                            if(success!=(capacity>=length))return 1;
                            if(!success){if(written!=0)return 2;for(nuint i=0;i<43;i++)if(output[i]!='Z')return 3;}
                            else{if(written!=length||output[0]!='Z')return 4;
                                for(nuint i=0;i<written;i++)if(output[i+1]!=expected[i])return 5;
                                for(nuint i=written+1;i<43;i++)if(output[i]!='Z')return 6;}
                        }
                        cursor+=length+1;cases++;
                    }
                }
                if(cases!={{caseCount}}||cursor!={{oracleBytes}})return 9;
                if(std::IntegerFormatting::TryFormatHex((extralong)0,(char*)1,0,ref written)||written!=0)return 7;
                if(std::IntegerFormatting::TryFormatHex((extralong)-1,output,43,ref written,(nuint)0-(nuint)1)||written!=0)return 8;
                return 0;
            }
            """;
        var options=new CompilationOptions{NoStdLib=true,NoBuiltins=true,InternalizeFunctions=true};
        string ir=Compiler.Emit(Sources(code),options:options);
        Assert.DoesNotMatch(@"\b(?:sdiv|udiv|srem|urem)\s+i128\b",ir);
        Assert.DoesNotContain("@__gflat_eh_",ir);
        Match method=Regex.Match(ir,@"(?ms)^define [^\r\n]*@[^\r\n]*TryFormatHex[^\r\n]*i128[^\r\n]*\{\r?\n(?<body>.*?)^\}");
        Assert.True(method.Success);Assert.Contains("nounwind",method.Value);Assert.Contains("\"no-builtins\"",method.Value);
        // Execute the same selective, no-builtins IR that was checked above.
        string directory=Path.Combine(Path.GetTempPath(),"gflat-extra-long-hex-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string input=Path.Combine(directory,"main.ll"),output=Path.Combine(directory,"main.exe");
            File.WriteAllText(input,ir);
            var compiled=NativeToolchain.Compile(input,output,optimizationLevel:optimization);
            Assert.True(compiled.ExitCode==0,compiled.StandardError);
            var result=NativeToolchain.Run(output,[]);
            Assert.True(result.ExitCode==0,$"ExtraLong hex exit {result.ExitCode}: {result.StandardError}");
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory);}
    }

    private static IEnumerable<SourceFile> Sources(string code)
        => new[]{new SourceFile("IntegerFormatting.gf",File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core/IntegerFormatting.gf"))),new SourceFile("main.gf",code)};
}
