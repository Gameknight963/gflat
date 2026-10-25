using System.Numerics;
using System.Text.RegularExpressions;

namespace gflat.Tests;

public sealed class StandardCommonMultipleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void UnsignedWidthsMatchIndependentBigIntegerOracle(int optimization)
    {
        string directory=Path.Combine(Path.GetTempPath(),"gflat-gcd-lcm-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try{
            string path=Path.Combine(directory,"oracle.bin");
            ulong[] edges=[0,1,2,3,6,8,12,uint.MaxValue-1,uint.MaxValue,1ul<<32,1ul<<63,3ul<<62,ulong.MaxValue-1,ulong.MaxValue];
            var pairs=edges.SelectMany(a=>edges.Select(b=>(A:a,B:b))).ToList();
            var random=new Random(81073);byte[] bytes=new byte[16];
            for(int i=0;i<512;i++){
                random.NextBytes(bytes);pairs.Add((BitConverter.ToUInt64(bytes),BitConverter.ToUInt64(bytes,8)));
                ulong shared=(ulong)random.Next(1,65536);
                pairs.Add((shared*(ulong)random.Next(65536),shared*(ulong)random.Next(65536)));
            }
            ulong nativeMaximum=IntPtr.Size==8?ulong.MaxValue:uint.MaxValue;
            using(var output=new BinaryWriter(File.Create(path)))foreach(var pair in pairs){
                output.Write(pair.A);output.Write(pair.B);
                var wide=Expected(pair.A,pair.B,ulong.MaxValue);output.Write(wide.Gcd);output.Write(wide.Lcm);output.Write(wide.Fits?1:0);
                var narrow=Expected((uint)pair.A,(uint)pair.B,uint.MaxValue);output.Write((uint)narrow.Gcd);output.Write((uint)narrow.Lcm);output.Write(narrow.Fits?1:0);
                var native=Expected(pair.A&nativeMaximum,pair.B&nativeMaximum,nativeMaximum);output.Write(native.Gcd);output.Write(native.Lcm);output.Write(native.Fits?1:0);
            }
            string code=$$"""
                extern void*? fopen(readonly(char)* path,readonly(char)* mode)nothrow;
                extern nuint fread(void* output,nuint size,nuint count,void* input)nothrow;
                extern int fclose(void* input)nothrow;
                ulong Word(readonly(char)* bytes,nuint count)nothrow{
                    ulong value=0ul;for(nuint i=0;i<count;i++)value=value|((ulong)(byte)bytes[i]<<(int)(8*i));return value;
                }
                int main()nothrow{
                    void*? file=fopen({{Literal(path)}},"rb");if(file==null)return 1;defer{fclose(file!);}
                    char[68] row;
                    for(int i=0;i<{{pairs.Count}};i++){
                        if(fread((void*)&row[0],1,68,file!)!=68)return 2;
                        ulong a=Word(row,8);ulong b=Word(&row[8],8);ulong wide=7ul;
                        if(std::Numerics::Math::GreatestCommonDivisor(a,b)!=Word(&row[16],8))return 3;
                        bool fits=std::Numerics::Math::TryLeastCommonMultiple(a,b,ref wide);
                        if(wide!=Word(&row[24],8)||fits!=(Word(&row[32],4)!=0ul))return 4;
                        uint narrow=7u;
                        if(std::Numerics::Math::GreatestCommonDivisor((uint)a,(uint)b)!=(uint)Word(&row[36],4))return 5;
                        fits=std::Numerics::Math::TryLeastCommonMultiple((uint)a,(uint)b,ref narrow);
                        if(narrow!=(uint)Word(&row[40],4)||fits!=(Word(&row[44],4)!=0ul))return 6;
                        nuint native=(nuint)7;
                        if(std::Numerics::Math::GreatestCommonDivisor((nuint)a,(nuint)b)!=(nuint)Word(&row[48],8))return 7;
                        fits=std::Numerics::Math::TryLeastCommonMultiple((nuint)a,(nuint)b,ref native);
                        if(native!=(nuint)Word(&row[56],8)||fits!=(Word(&row[64],4)!=0ul))return 8;
                    }
                    if(fread((void*)&row[0],1,1,file!)!=0)return 9;return 0;
                }
                """;
            var result=CompilerTestHelper.Run(Sources(code),optimization);
            Assert.True(result.ExitCode==0,$"Exit {result.ExitCode}: {result.StandardError}");
        }finally{Directory.Delete(directory,true);}
    }

    private static (ulong Gcd,ulong Lcm,bool Fits) Expected(ulong a,ulong b,ulong maximum)
    {
        BigInteger gcd=BigInteger.GreatestCommonDivisor(a,b);
        BigInteger lcm=a==0||b==0?BigInteger.Zero:(BigInteger)a/gcd*b;
        return ((ulong)gcd,lcm<=maximum?(ulong)lcm:0,lcm<=maximum);
    }

    [Theory]
    [InlineData("uint",0)]
    [InlineData("ulong",0)]
    [InlineData("nuint",0)]
    [InlineData("uint",2)]
    [InlineData("ulong",2)]
    [InlineData("nuint",2)]
    public void RefResultMayAliasEitherInputOnSuccessZeroOrOverflow(string type,int optimization)
    {
        string code=$$"""
            using std; using std::Numerics;
            int main()nothrow{
                {{type}} a=({{type}})6;{{type}} b=({{type}})8;
                if(!Math::TryLeastCommonMultiple(a,b,ref a)||a!=({{type}})24||b!=({{type}})8)return 1;
                a=({{type}})6;if(!Math::TryLeastCommonMultiple(a,b,ref b)||b!=({{type}})24||a!=({{type}})6)return 2;
                {{type}} maximum=({{type}})0-({{type}})1;
                if(Math::TryLeastCommonMultiple(maximum,({{type}})2,ref maximum)||maximum!=({{type}})0)return 3;
                a=({{type}})0;b=({{type}})9;if(!Math::TryLeastCommonMultiple(a,b,ref b)||b!=({{type}})0)return 4;
                maximum=({{type}})0-({{type}})1;
                if(!Math::TryLeastCommonMultiple(maximum,maximum,ref maximum)||maximum!=({{type}})0-({{type}})1)return 5;
                return 0;
            }
            """;
        var result=CompilerTestHelper.Run(Sources(code),optimization);Assert.Equal(0,result.ExitCode);
    }

    [Fact]
    public void NewMethodBodiesAreNothrowAndHaveNoAllocationOrRuntimeCalls()
    {
        // Math's existing checked methods still contain exception support; inspect
        // only these six unsigned arithmetic bodies rather than the whole module.
        string ir=Compiler.Emit(Sources("int main()nothrow{return 0;}"));
        var bodies=Regex.Matches(ir,@"(?ms)^define [^\n]*\$Math\$(?:GreatestCommonDivisor|TryLeastCommonMultiple)[^\n]*\{.*?^\}")
            .Select(match=>match.Value).ToArray();
        Assert.Equal(6,bodies.Length);
        Assert.All(bodies,body=>{
            Assert.Contains("nounwind",body);
            foreach(string forbidden in new[]{"invoke ","@malloc","@free","gflat$Memory$","__gflat_eh"})Assert.DoesNotContain(forbidden,body);
            Assert.All(body.Split('\n').Where(line=>line.Contains("call ")),line=>Assert.Contains("$GreatestCommonDivisor",line));
        });
    }
    private static SourceFile[] Sources(string source)=>[
        new("Math.gf",File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core/Numerics/Math.gf"))),new("common-multiples.gf",source)];
    private static string Literal(string value)=>"\""+value.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";
}
