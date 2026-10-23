using System.Text;
using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class StandardSpanChunksTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EverySmallPartitionMatchesDotNetChunkIncludingPartialTail(int optimization)
    {
        var result=CompilerTestHelper.Run(Sources("""
            using std;extern int printf(readonly(char)* format,...);
            int main(){
                int[20] data;for(nuint i=0;i<20;i++)data[i]=(int)(3*i+2);
                for(nuint length=0;length<=20;length++)for(nuint size=1;size<=25;size++){
                    Span<int> source=new(data,length);
                    foreach(Span<int> chunk in source.Chunks(size)){
                        printf("[%d:",(int)chunk.Length);
                        for(nuint i=0;i<chunk.Length;i++)printf("%d,",chunk[i]);
                        printf("]");
                    }
                    printf("\n");
                }
                return 0;
            }
            """),optimization);
        Assert.Equal(0,result.ExitCode);var expected=new StringBuilder();
        for(int length=0;length<=20;length++)for(int size=1;size<=25;size++){
            foreach(int[] chunk in Enumerable.Range(0,length).Select(i=>3*i+2).Chunk(size)){
                expected.Append('[').Append(chunk.Length).Append(':');
                foreach(int value in chunk)expected.Append(value).Append(',');expected.Append(']');
            }
            expected.Append('\n');
        }
        Assert.Equal(expected.ToString(),result.StandardOutput.Replace("\r\n","\n"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void LazyRepeatedQueriesCaptureMetadataAndObserveWritesWithoutAllocation(int optimization)
        => Run("""
            using std;extern void*? malloc(nuint size);extern void free(void* data);
            int allocations=0;int selections=0;
            namespace Memory{public replace void*? Allocate(nuint size){allocations++;return malloc(size);}public replace void Free(void* data) nothrow{free(data);}}
            int SumChunk(Span<readonly(int)> chunk){selections++;int sum=0;for(nuint i=0;i<chunk.Length;i++)sum+=chunk[i];return sum;}
            int Add(int total,int value)=>total+value;
            SpanChunks<int> Capture(Span<int> source)=>source.Chunks(3);
            int main(){
                int[7] data=[1,2,3,4,5,6,7];Span<int> source=new(data,7);int before=allocations;
                var captured=Capture(source);var query=source.AsImmutable().Chunks(3).AsEnumerable().Select(SumChunk);
                if(selections!=0||query.Aggregate(0,Add)!=28||selections!=3)return 1;
                data[6]=70;source=new();
                if(query.Aggregate(0,Add)!=91||selections!=6)return 2;
                if(query.Take(2).Aggregate(0,Add)!=21||selections!=8)return 3;
                int count=0;foreach(Span<int> chunk in captured){chunk[0]*=10;count++;}
                if(count!=3||data[0]!=10||data[3]!=40||data[6]!=700)return 4;
                int total=0;foreach(Span<int> chunk in captured)for(nuint i=0;i<chunk.Length;i++)total+=chunk[i];
                if(total!=766||allocations!=before)return 5;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NoncopyableAndQualifiedElementsRetainOwnershipAndProtection(int optimization)
        =>Run("""
            using std;int dead=0;
            struct Owner{public int Id;public int* Pointer;public Owner(int id,int* pointer){Id=id;Pointer=pointer;}~Owner(){dead++;}}
            int main(){int pointed=0;
                {
                    Owner[3] data=[new Owner(1,&pointed),new Owner(2,&pointed),new Owner(3,&pointed)];Span<Owner> source=new(data,3);
                    foreach(Span<Owner> chunk in source.Chunks(2))for(nuint i=0;i<chunk.Length;i++)chunk[i].Id+=10;
                    foreach(Span<readonly(Owner)> chunk in source.AsReadOnly().Chunks(2))for(nuint i=0;i<chunk.Length;i++)*chunk[i].Pointer+=1;
                    int total=0;foreach(Span<immutable(Owner)> chunk in source.AsImmutable().Chunks(2))for(nuint i=0;i<chunk.Length;i++)total+=chunk[i].Id+*chunk[i].Pointer;
                    if(pointed!=3||total!=45||dead!=0)return 1;
                }
                if(dead!=3)return 2;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EmptyInvalidCurrentAndMaximumSizeBoundariesAreDefined(int optimization)
        =>Run("""
            using std;
            int main(){
                Span<int> empty=new();var absent=empty.Chunks(1).GetEnumerator();if(absent.MoveNext()||absent.MoveNext())return 1;int errors=0;
                try{absent.Current;}catch{errors++;}
                try{empty.Chunks(0);}catch{errors++;}
                int value=42;Span<int> one=new(&value,1);try{one.Chunks(0);}catch{errors++;}
                try{SpanChunks<int> invalid=new(null,1,1);}catch{errors++;}
                try{SpanChunks<int>::Enumerator invalid=new(null,1,1);}catch{errors++;}
                var cursor=one.Chunks(1).GetEnumerator();try{cursor.Current;}catch{errors++;}
                if(!cursor.MoveNext()||cursor.Current[0]!=42||cursor.MoveNext()||cursor.MoveNext())return 2;
                try{cursor.Current;}catch{errors++;}
                nuint maximum=(nuint)0-(nuint)1;
                cursor=one.Chunks(maximum).GetEnumerator();if(!cursor.MoveNext()||cursor.Current.Length!=1||cursor.Current[0]!=42||cursor.MoveNext())return 3;
                // Probe cursor metadata only; no fabricated extent is dereferenced.
                SpanChunks<int>::Enumerator huge=new((int*)1,maximum,maximum);
                if(!huge.MoveNext()||huge.MoveNext())return 4;
                SpanChunks<int>::Enumerator tail=new((int*)1,maximum,maximum-1);
                if(!tail.MoveNext()||!tail.MoveNext()||tail.MoveNext())return 5;
                if(errors!=7)return 6;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChunkElementQualifiersRejectForbiddenWrites(bool deep)
    {
        string setup="""
            using std;struct Item{public int Value;public int* Pointer;public Item(int* pointer){Value=0;Pointer=pointer;}}
            int main(){int pointed=0;Item item=new(&pointed);Span<Item> source=new(&item,1);
            """;
        string loop=deep?"foreach(Span<immutable(Item)> chunk in source.AsImmutable().Chunks(1)){BODY}":
            "foreach(Span<readonly(Item)> chunk in source.AsReadOnly().Chunks(1)){BODY}";
        Compiler.Check(Sources(setup+loop.Replace("BODY","int read=chunk[0].Value;")+"return 0;}"));
        var error=Assert.Throws<TypeCheckException>(()=>Compiler.Check(Sources(setup+loop.Replace("BODY",deep?"*chunk[0].Pointer=1;":"chunk[0].Value=1;")+"return 0;}")));
        Assert.Equal("span-chunks.gf",error.Span.Source?.Path);Assert.Contains("readonly",error.Message,StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<SourceFile> Sources(string source)
    {
        string[] names=["Span.gf","Collections/IEnumerable.gf","Collections/IEnumerator.gf","Collections/Enumerable.gf","Collections/SpanChunks.gf",
            "Collections/EnumerableExtensions.gf","Collections/WhereSource.gf","Collections/SelectSource.gf","Collections/SliceSource.gf","Collections/TakeWhileSource.gf","Collections/SkipWhileSource.gf"];
        return names.Select(name=>new SourceFile(name,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core",name))))
            .Append(new SourceFile("span-chunks.gf",source));
    }
    private static void Run(string source,int optimization)
    {
        var result=CompilerTestHelper.Run(Sources(source),optimization);Assert.True(result.ExitCode==0,$"Exit {result.ExitCode}: {result.StandardError}");
    }
}
