using System.Numerics;

namespace gflat.Tests;

public sealed class StandardBitRoundingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FixedWidthsMatchDotNetForPowerNeighborsAndRandomInputs(int optimization)
    {
        string directory=Path.Combine(Path.GetTempPath(),"gflat-bit-rounding-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try{
            string path=Path.Combine(directory,"oracle.bin");
            var values=new List<ulong>{0,1,2,3,uint.MaxValue,ulong.MaxValue};
            for(int bit=0;bit<64;bit++){ulong power=1ul<<bit;values.Add(power-1);values.Add(power);values.Add(power+1);}
            var random=new Random(154871);byte[] bytes=new byte[8];
            for(int i=0;i<512;i++){random.NextBytes(bytes);values.Add(BitConverter.ToUInt64(bytes));}
            using(var output=new BinaryWriter(File.Create(path)))foreach(ulong value in values){
                output.Write(value);output.Write(BitOperations.RoundUpToPowerOf2((uint)value));output.Write(BitOperations.RoundUpToPowerOf2(value));
            }
            string code=$$"""
                extern void*? fopen(readonly(char)* path,readonly(char)* mode)nothrow;
                extern nuint fread(void* output,nuint size,nuint count,void* input)nothrow;
                extern int fclose(void* input)nothrow;
                ulong Word(readonly(char)* bytes,nuint count)nothrow{
                    ulong result=0ul;for(nuint i=0;i<count;i++)result=result|((ulong)(byte)bytes[i]<<(int)(i*8));return result;
                }
                int main()nothrow{
                    void*? file=fopen({{Literal(path)}},"rb");if(file==null)return 1;
                    defer{fclose(file!);}char[20] row;
                    for(int i=0;i<{{values.Count}};i++){
                        if(fread((void*)&row[0],1,20,file!)!=20)return 2;
                        ulong value=Word(row,8);
                        if(std::BitOperations::RoundUpToPowerOfTwo((uint)value)!=(uint)Word(&row[8],4))return 3;
                        if(std::BitOperations::RoundUpToPowerOfTwo(value)!=Word(&row[12],8))return 4;
                    }
                    if(fread((void*)&row[0],1,1,file!)!=0)return 5;return 0;
                }
                """;
            var result=CompilerTestHelper.Run(Sources(code),optimization);
            Assert.True(result.ExitCode==0,$"Exit {result.ExitCode}: {result.StandardError}");
        }finally{Directory.Delete(directory,true);}
    }

    [Fact]
    public void RoundingHasNoAllocationExceptionOrLibraryRuntimeDependency()
    {
        string ir=Compiler.Emit(Sources("""
            int main()nothrow{
                if(std::BitOperations::RoundUpToPowerOfTwo(9u)!=16u)return 1;
                if(std::BitOperations::RoundUpToPowerOfTwo(9223372036854775809ul)!=0ul)return 2;
                return 0;
            }
            """),options:new(){NoStdLib=true,NoBuiltins=true,InternalizeFunctions=true});
        string[] definitions=ir.Split('\n').Where(line=>line.StartsWith("define ")&&line.Contains("$RoundUpToPowerOfTwo")).ToArray();
        Assert.Equal(2,definitions.Length);
        Assert.All(definitions,line=>Assert.Contains("nounwind",line));
        foreach(string forbidden in new[]{"invoke ","__gflat_eh","@malloc","@free","gflat$Memory$","llvm.memcpy"})Assert.DoesNotContain(forbidden,ir);
    }
    private static SourceFile[] Sources(string source)=>[
        new("BitOperations.gf",File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core/Numerics/BitOperations.gf"))),
        new("bit-rounding.gf",source)];
    private static string Literal(string value)=>"\""+value.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";
}
