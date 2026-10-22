namespace gflat.Tests;

public sealed class StandardVectorRangeGrowthTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SingletonRangesUseOrdinaryGrowthAndBulkRangesReserveOnce(int optimization)
        => Run("""
            int main(){
                int ordinary=0;
                {Vector<int> target=new();for(int i=0;i<1024;i++)target.Add(i);ordinary=allocations;}
                int before=allocations;
                {Vector<int> target=new();int[1] value=[7];Span<readonly(int)> source=new(value,1);
                 for(int i=0;i<1024;i++)target.AddRange(source);
                 if(target.Count!=1024||target.Capacity!=1024||allocations-before!=ordinary)return 1;
                 for(nuint i=0;i<target.Count;i++)if(target[i]!=7)return 2;
                 target.AddRange(target.AsReadOnly());
                 if(target.Count!=2048||target.Capacity!=2048)return 3;
                 for(nuint i=0;i<target.Count;i++)if(target[i]!=7)return 4;}
                {Vector<int> target=new();target.Add(1);int[20] values;
                 for(nuint i=0;i<20;i++)values[i]=(int)i;
                 Span<readonly(int)> source=new(values,20);before=allocations;target.AddRange(source);
                 if(allocations!=before+1||target.Capacity!=21||target.Count!=21)return 5;
                 for(nuint i=0;i<20;i++)if(target[i+1]!=(int)i)return 6;
                 Span<readonly(int)> empty=new();before=allocations;target.AddRange(empty);
                 if(allocations!=before||target.Count!=21)return 7;}
                if(allocations!=releases)return 8;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void GrowthFailureAndImpossibleRangePreserveTargetAndDoNotReadSource(int optimization)
        => Run("""
            int main(){
                {Vector<int> target=new();for(int i=0;i<4;i++)target.Add(i);
                 nuint capacity=target.Capacity;readonly(int)* address=target.AsReadOnly().Data!;
                 deny=true;
                 try{target.AddRange(target.AsReadOnly());return 1;}catch(ref BadAllocException error){}
                 deny=false;
                 if(target.Count!=4||target.Capacity!=capacity||target.AsReadOnly().Data!=address)return 2;
                 for(nuint i=0;i<4;i++)if(target[i]!=(int)i)return 3;
                 int before=allocations;
                 Span<readonly(int)> excessive=new((readonly(int)*)1,(nuint)0-(nuint)1);
                 try{target.AddRange(excessive);return 4;}catch(ref Exception error){}
                 if(largeAllocations!=0||target.Count!=4||target.Capacity!=capacity)return 5;
                 Span<readonly(int)> excessiveBytes=new((readonly(int)*)1,((nuint)0-(nuint)1)/(nuint)sizeof(int));
                 try{target.AddRange(excessiveBytes);return 6;}catch(ref Exception error){}
                 if(largeAllocations!=0||target.Count!=4||target.Capacity!=capacity)return 7;
                 target.AddRange(target.AsReadOnly());
                 if(target.Count!=8||target.Capacity!=8)return 8;
                 for(nuint i=0;i<8;i++)if(target[i]!=(int)(i%4))return 9;}
                if(allocations!=releases)return 10;return 0;
            }
            """,optimization);

    private static void Run(string code,int optimization)
    {
        const string counters="""
            using std;extern void*? malloc(nuint count);extern void free(void* storage);
            int allocations=0;int releases=0;int largeAllocations=0;bool deny=false;
            namespace Memory{
                public replace void*? Allocate(nuint count){if(count>8192){largeAllocations++;return null;}if(deny)return null;allocations++;return malloc(count);}
                public replace void Free(void* storage){releases++;free(storage);}
            }
            """;
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("main.gf",counters+code));
        Assert.Equal(0,CompilerTestHelper.Run(sources,optimization).ExitCode);
    }
}
