using gflat.CompileExceptions;

namespace gflat.Tests.Std.Collections;

// Compare partitions with independent reachability and inject real constructor failures.
public sealed class DisjointSetTests
{
    private const string Counters="""
        using std; using std::Collections;extern void*? malloc(nuint count);extern void free(void* storage);
        int attempts=0;int allocated=0;int released=0;int denied=0;int failAfter=-1;bool deny=false;
        namespace Memory{
            public replace void*? Allocate(nuint count){
                attempts++;
                if(deny){denied++;return null;}
                if(failAfter==0){failAfter=-1;denied++;return null;}
                if(failAfter>0)failAfter--;
                void*? result=malloc(count);if(result!=null)allocated++;return result;
            }
            public replace void Free(void* storage)nothrow{released++;free(storage);}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BalancedAndRandomUnionsMatchIndependentGraphReachabilityWithoutAssumingRootIdentity(int optimization)
    {
        const int count=17;
        var edges=new List<(int First,int Second)>();
        for(int stride=1;stride<8;stride*=2)for(int first=0;first<8;first+=2*stride)edges.Add((first,first+stride));
        edges.Add((16,16));edges.Add((3,0));
        var random=new Random(762119);for(int i=0;i<64;i++)edges.Add((random.Next(count),random.Next(count)));
        var graph=Enumerable.Range(0,count).Select(_=>new HashSet<int>()).ToArray();
        int[] Labels(){
            int[] labels=Enumerable.Repeat(-1,count).ToArray();
            for(int start=0;start<count;start++)if(labels[start]<0){
                var queue=new Queue<int>();queue.Enqueue(start);labels[start]=start;
                while(queue.TryDequeue(out int current))foreach(int next in graph[current])if(labels[next]<0){labels[next]=start;queue.Enqueue(next);}
            }
            return labels;
        }
        var bytes=new List<byte>();
        void Snapshot(){int[] labels=Labels();bytes.Add((byte)labels.Distinct().Count());bytes.AddRange(labels.Select(value=>(byte)value));}
        Snapshot();
        foreach(var edge in edges){
            int[] before=Labels();bool merged=before[edge.First]!=before[edge.Second];
            bytes.Add((byte)edge.First);bytes.Add((byte)edge.Second);bytes.Add((byte)(merged?1:0));
            graph[edge.First].Add(edge.Second);graph[edge.Second].Add(edge.First);Snapshot();
        }
        string hex=Convert.ToHexString(bytes.ToArray());
        Run(Counters+$$"""
            int Digit(char value)nothrow{if(value<='9')return value-'0';return value-'A'+10;}
            nuint Next(readonly(char)* text,ref nuint at)nothrow{nuint result=(nuint)((Digit(text[at])<<4)|Digit(text[at+1]));at+=2;return result;}
            bool Verify(immutable(DisjointSet)* sets,readonly(char)* data,ref nuint at){
                nuint expectedCount=Next(data,ref at);nuint[17] labels;
                for(nuint i=0;i<17;i++)labels[i]=Next(data,ref at);
                if(sets.ElementCount!=17||sets.SetCount!=expectedCount)return false;
                for(nuint i=0;i<17;i++){
                    nuint root=sets.Find(i);if(root>=17||sets.Find(root)!=root||!sets.Connected(i,root))return false;
                    nuint size=0;for(nuint j=0;j<17;j++){
                        bool expected=labels[i]==labels[j];if(expected)size++;
                        if(sets.Connected(i,j)!=expected||((sets.Find(i)==sets.Find(j))!=expected))return false;
                    }
                    if(sets.SizeOf(i)!=size)return false;
                }
                return true;
            }
            int main(){
                {
                    DisjointSet sets=new(17);immutable(DisjointSet)* view=&sets;
                    readonly(char)* data="{{hex}}";nuint at=0;int before=attempts;deny=true;
                    if(!Verify(view,data,ref at))return 1;
                    for(int row=0;row<{{edges.Count}};row++){
                        nuint first=Next(data,ref at);nuint second=Next(data,ref at);bool expected=Next(data,ref at)!=0;
                        if(sets.Union(first,second)!=expected||!Verify(view,data,ref at))return 2;
                    }
                    if(at!={{hex.Length}}||attempts!=before)return 3;deny=false;
                }
                if(allocated!=released)return 4;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryInvalidPositionRejectsBeforeMutationAndEmptyConstructionAllocatesNothing(int optimization)
        =>Run(Counters+"""
            int main(){
                {
                    int before=attempts;deny=true;DisjointSet empty=new();DisjointSet countedEmpty=new(0);
                    if(empty.ElementCount!=0||empty.SetCount!=0||countedEmpty.ElementCount!=0||countedEmpty.SetCount!=0||attempts!=before)return 1;deny=false;
                    DisjointSet sets=new(7);sets.Union(0,1);sets.Union(2,3);sets.Union(3,4);
                    nuint[7] roots;nuint[7] sizes;for(nuint i=0;i<7;i++){roots[i]=sets.Find(i);sizes[i]=sets.SizeOf(i);}
                    nuint[2] invalid=[7,(nuint)0-1];
                    for(nuint bad=0;bad<2;bad++)for(int operation=0;operation<6;operation++){
                        bool caught=false;
                        try{
                            if(operation==0)sets.Find(invalid[bad]);
                            else if(operation==1)sets.SizeOf(invalid[bad]);
                            else if(operation==2)sets.Connected(invalid[bad],0);
                            else if(operation==3)sets.Connected(0,invalid[bad]);
                            else if(operation==4)sets.Union(invalid[bad],0);
                            else sets.Union(0,invalid[bad]);
                        }catch(ref Exception error){caught=true;}
                        if(!caught||sets.ElementCount!=7||sets.SetCount!=4)return 2;
                        before=attempts;deny=true;
                        for(nuint i=0;i<7;i++){
                            if(sets.Find(i)!=roots[i]||sets.SizeOf(i)!=sizes[i])return 3;
                            for(nuint j=0;j<7;j++)if(sets.Connected(i,j)!=(roots[i]==roots[j]))return 4;
                        }
                        deny=false;if(attempts!=before)return 5;
                    }
                    bool caught=false;try{empty.Find(0);}catch(ref Exception error){caught=true;}if(!caught||empty.SetCount!=0)return 6;
                }
                if(allocated!=released)return 7;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryMeasuredConstructorFailureCleansEarlierOwnersAndMoveReturnAndReplacementReleaseExactlyOnce(int optimization)
        =>Run(Counters+"""
            DisjointSet Make(){DisjointSet result=new(17);result.Union(0,16);return move result;}
            int main(){
                {
                    DisjointSet target=new(7);target.Union(0,6);nuint root=target.Find(0);
                    nuint[3] counts=[1,33,4096];
                    for(nuint row=0;row<3;row++){
                        int requests=attempts;int live=allocated-released;int points=0;
                        {DisjointSet probe=new(counts[row]);if(probe.ElementCount!=counts[row]||probe.SetCount!=counts[row])return 1;points=attempts-requests;}
                        if(points==0||allocated-released!=live)return 2;
                        for(int point=0;point<points;point++){
                            int deniedBefore=denied;live=allocated-released;failAfter=point;bool caught=false;
                            try{target=new DisjointSet(counts[row]);}catch(ref BadAllocException error){caught=true;}
                            failAfter=-1;
                            // A single denial is injected; exception payload allocation can
                            // succeed afterwards. Compare successful live allocations after catch.
                            if(!caught||denied!=deniedBefore+1||allocated-released!=live)return 3;
                            if(target.ElementCount!=7||target.SetCount!=6||target.Find(0)!=root||!target.Connected(0,6)||target.SizeOf(0)!=2)return 4;
                        }
                    }
                    DisjointSet returned=Make();DisjointSet moved=move returned;
                    if(moved.ElementCount!=17||moved.SetCount!=16||!moved.Connected(0,16)||moved.SizeOf(0)!=2)return 5;
                    target=move moved;
                    if(target.ElementCount!=17||target.SetCount!=16||!target.Connected(0,16))return 6;
                }
                if(allocated!=released)return 7;return 0;
            }
            """,optimization);

    [Fact]
    public void ImplicitCopyOfTheOwningPartitionIsRejected()
    {
        var error=Assert.Throws<TypeCheckException>(()=>Compiler.Check(Sources("using std; using std::Collections;void Probe(){DisjointSet original=new(3);DisjointSet copied=original;}")));
        Assert.Contains("copy",error.Message,StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<SourceFile> Sources(string program)
        =>Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("disjoint-set.gf",program));
    private static void Run(string program,int optimization)
    {
        var result=CompilerTestHelper.Run(Sources(program),optimization);Assert.True(result.ExitCode==0,$"DisjointSet exit {result.ExitCode}: {result.StandardError}");
    }
}
