using System.Numerics;
using System.Text;

namespace gflat.Tests;

public sealed class StandardMemoryAlignmentTests
{
    private const string Allocator="""
        using std; using std::Text;extern void*? malloc(nuint count);extern void free(void* storage);int attempts=0;bool deny=false;
        namespace Memory{
            public replace void*? Allocate(nuint count){attempts++;if(deny)return null;return malloc(count);}
            public replace void Free(void* storage)nothrow{free(storage);}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ValidIntegerOffsetsMatchWideArithmeticOracleWithoutAllocation(int optimization)
    {
        int bits=IntPtr.Size*8;BigInteger maximum=(BigInteger.One<<bits)-1;
        var body=new StringBuilder();int[] shifts=[0,1,2,3,7,15,bits-2,bits-1];
        foreach(int shift in shifts.Distinct()){
            BigInteger alignment=BigInteger.One<<shift;
            BigInteger[] values=[0,1,alignment-1,alignment,alignment+1,maximum-alignment,maximum-1,maximum];
            foreach(var value in values.Where(value=>value>=0&&value<=maximum).Distinct()){
                BigInteger down=value/alignment*alignment,up=(value+alignment-1)/alignment*alignment;
                body.Append($"{{nuint value=(nuint){value}ul;nuint alignment=(nuint){alignment}ul;if(Memory::AlignDown(value,alignment)!=(nuint){down}ul||Memory::IsAligned(value,alignment)!={Bool(value%alignment==0)})return 1;");
                if(up<=maximum)body.Append($"if(Memory::AlignUp(value,alignment)!=(nuint){up}ul)return 2;");
                body.Append('}');
            }
        }
        Run(Allocator+$"int main(){{if((nuint)sizeof(nuint)!=(nuint){IntPtr.Size})return 3;int before=attempts;deny=true;"+body+"deny=false;if(attempts!=before)return 4;return 0;}",optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InvalidAlignmentAndUpwardOverflowPreserveTheAssignmentTarget(int optimization)
    {
        int bits=IntPtr.Size*8;BigInteger maximum=(BigInteger.One<<bits)-1;
        var body=new StringBuilder();
        foreach(BigInteger invalid in new BigInteger[]{0,3,maximum}){
            body.Append($$$"""
                {nuint alignment=(nuint){{{invalid}}}ul;int before=attempts;deny=true;if(Memory::IsAligned(0,alignment)||Memory::IsAligned(17,alignment))return 1;deny=false;if(attempts!=before)return 2;
                 for(int mode=0;mode<2;mode++){nuint result=123;bool caught=false;
                    try{if(mode==0)result=Memory::AlignDown(17,alignment);else result=Memory::AlignUp(17,alignment);}
                    catch(ref Exception error){if(!CString::Equals(error.GetMessage(),"Alignment must be a positive power of two"))return 3;caught=true;}
                    if(!caught||result!=123)return 4;}}
                """);
        }
        foreach(int shift in new[]{1,2,7,bits-1}){
            BigInteger alignment=BigInteger.One<<shift;
            BigInteger[] values=[maximum,maximum-1,maximum-alignment+2];
            foreach(BigInteger value in values.Distinct()){
                BigInteger up=(value+alignment-1)/alignment*alignment;if(up<=maximum)continue;
                body.Append($$$"""
                    {nuint value=(nuint){{{value}}}ul;nuint alignment=(nuint){{{alignment}}}ul;nuint result=123;bool caught=false;
                     try{result=Memory::AlignUp(value,alignment);}catch(ref Exception error){if(!CString::Equals(error.GetMessage(),"Alignment overflow"))return 5;caught=true;}
                     if(!caught||result!=123||Memory::IsAligned(value,alignment))return 6;}
                    """);
            }
        }
        Run(Allocator+"int main(){"+body+"return 0;}",optimization);
    }

    private static string Bool(bool value)=>value?"true":"false";
    private static void Run(string program,int optimization){var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories).Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("memory-alignment.gf",program));var result=CompilerTestHelper.Run(sources,optimization);Assert.True(result.ExitCode==0,$"Memory alignment exit {result.ExitCode}: {result.StandardError}");}
}
