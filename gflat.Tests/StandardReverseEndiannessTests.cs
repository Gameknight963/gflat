using System.Buffers.Binary;

namespace gflat.Tests;

public class StandardReverseEndiannessTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EverySixteenBitPatternMatchesTheBytePermutationAndIsAnInvolution(int optimization)
    {
        // Validate the arithmetic oracle against .NET without embedding 65536
        // constants in the gflat program. Its native loop uses that same oracle.
        for (uint raw=0;raw<=65535;raw++)
            Assert.Equal(BinaryPrimitives.ReverseEndianness((ushort)raw),(ushort)(256*(raw%256)+raw/256));
        Run("""
            int main() {
                for(uint raw=0u;raw<=65535u;raw++) {
                    ushort value=(ushort)raw;
                    ushort expected=(ushort)(256u*(raw%256u)+raw/256u);
                    ushort result=std::BitOperations::ReverseEndianness(value);
                    if(result!=expected||std::BitOperations::ReverseEndianness(result)!=value)return 1;
                    short signed=(short)value;
                    short changed=std::BitOperations::ReverseEndianness(signed);
                    if(changed!=(short)expected||std::BitOperations::ReverseEndianness(changed)!=signed)return 2;
                }
                return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SignedAndUnsignedWidePatternsMatchDotNetAndRoundTripTheirBits(int optimization)
    {
        ulong[] edges=[0ul,1ul,255ul,256ul,65535ul,65536ul,0x0123456789abcdeful,0x8000000000000000ul,ulong.MaxValue,
            0x80000000ul,0x7ffffffful,0x7ffffffffffffffful,0xaaaaaaaaaaaaaaaaul,0x5555555555555555ul];
        var random=new Random(0x58d391);
        byte[] randomBytes=new byte[192*8];random.NextBytes(randomBytes);
        ulong[] values=edges.Concat(Enumerable.Range(0,192).Select(i=>BinaryPrimitives.ReadUInt64LittleEndian(randomBytes.AsSpan(i*8,8)))).ToArray();
        string code=$$"""
            int main() {
                ulong[{{values.Length}}] values=[{{string.Join(",",values.Select(value=>value+"ul"))}}];
                uint[{{values.Length}}] reversed32=[{{string.Join(",",values.Select(value=>BinaryPrimitives.ReverseEndianness((uint)value)+"u"))}}];
                ulong[{{values.Length}}] reversed64=[{{string.Join(",",values.Select(value=>BinaryPrimitives.ReverseEndianness(value)+"ul"))}}];
                for(int i=0;i<{{values.Length}};i++) {
                    uint small=(uint)values[i];ulong large=values[i];
                    uint changed32=std::BitOperations::ReverseEndianness(small);
                    ulong changed64=std::BitOperations::ReverseEndianness(large);
                    if(changed32!=reversed32[i]||changed64!=reversed64[i])return 1;
                    if(std::BitOperations::ReverseEndianness(changed32)!=small||std::BitOperations::ReverseEndianness(changed64)!=large)return 2;
                    int signed32=(int)small;long signed64=(long)large;
                    int result32=std::BitOperations::ReverseEndianness(signed32);
                    long result64=std::BitOperations::ReverseEndianness(signed64);
                    if((uint)result32!=reversed32[i]||(ulong)result64!=reversed64[i])return 3;
                    if(std::BitOperations::ReverseEndianness(result32)!=signed32||std::BitOperations::ReverseEndianness(result64)!=signed64)return 4;
                }
                return 0;
            }
            """;
        Run(code,optimization);
    }

    private static void Run(string code,int optimization)
    {
        SourceFile[] sources=[new("BitOperations.gf",File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core/BitOperations.gf"))),new("main.gf",code)];
        string ir=Compiler.Emit(sources,options:new(){NoStdLib=true});
        foreach(string forbidden in new[]{"@__gflat_eh_","personality","gflat$Memory$","@malloc","fptoui","uitofp"})Assert.DoesNotContain(forbidden,ir);
        Assert.Equal(0,CompilerTestHelper.Run(sources,optimization).ExitCode);
    }
}
