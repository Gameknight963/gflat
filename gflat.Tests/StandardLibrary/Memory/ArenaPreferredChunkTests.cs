namespace gflat.Tests.Std.Memory;

public sealed class ArenaPreferredChunkTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OversizedPreferredChunkFallsBackToBoundedRequestsAndRetainsReusablePadding(int optimization)
    {
        const string program = """
            using Standard; using Standard::Memory;
            extern void*? malloc(nuint bytes);extern void free(void* storage);
            int attempts=0;int allocated=0;int released=0;int oversized=0;
            nuint requested=0;
            namespace Memory{
                public replace void*? Allocate(nuint bytes){
                    attempts++;requested=bytes;
                    // A regression must fail promptly instead of asking malloc for
                    // an address-space-sized preferred chunk. Exception allocation
                    // remains available after that rejected request.
                    if(bytes>1024){oversized++;return null;}
                    void*? result=malloc(bytes);if(result!=null)allocated++;return result;
                }
                public replace void Free(void* storage)nothrow{released++;free(storage);}
            }
            int main(){
                {
                    Arena arena=new((nuint)0-(nuint)1);
                    if(attempts!=0)return 1;
                    byte* first=(byte*)arena.Allocate(7,256);
                    if(requested<7||requested>1024||attempts!=1||allocated!=1||oversized!=0)return 2;
                    if(first.reinterpret<nuint>()%256!=0)return 3;
                    for(nuint i=0;i<7;i++)first[i]=(byte)(i+41);
                    // malloc and the block bookkeeping preserve native alignment,
                    // leaving at least sizeof(nuint)-1 bytes of reusable slack.
                    byte* next=(byte*)arena.Allocate(1,1);*next=(byte)99;
                    if(next!=first+7||attempts!=1||allocated!=1)return 4;
                    byte* grown=(byte*)arena.Allocate(512,256);
                    if(requested<512||requested>1024||attempts!=2||allocated!=2||oversized!=0)return 5;
                    if(grown.reinterpret<nuint>()%256!=0||*next!=(byte)99)return 6;
                    for(nuint i=0;i<7;i++)if(first[i]!=(byte)(i+41))return 7;
                    grown[0]=(byte)17;grown[511]=(byte)18;
                    arena.Reset();arena.Reset();
                    if(released!=2||attempts!=2)return 8;
                    byte* fresh=(byte*)arena.AllocateZeroed(3,256);
                    if(requested<3||requested>1024||attempts!=3||allocated!=3||oversized!=0)return 9;
                    if(fresh.reinterpret<nuint>()%256!=0||fresh[0]!=(byte)0||fresh[1]!=(byte)0||fresh[2]!=(byte)0)return 10;
                }
                if(allocated!=3||released!=3||oversized!=0)return 11;
                return 0;
            }
            """;
        var sources = new[] { "Memory/Arena.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard/core", name))))
            .Append(new SourceFile("arena-preferred-chunk.gf", program));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Arena preferred chunk exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
