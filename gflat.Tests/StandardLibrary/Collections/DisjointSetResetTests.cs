using gflat.CompileExceptions;

namespace gflat.Tests.Std.Collections;

public sealed class DisjointSetResetTests
{
    private const string Counters = """
        using std::Collections;
        extern void*? malloc(nuint count);extern void free(void* storage);
        int attempts=0;int allocated=0;int released=0;bool deny=false;
        namespace Memory{
            public replace void*? Allocate(nuint count){attempts++;if(deny)return null;void*? result=malloc(count);if(result!=null)allocated++;return result;}
            public replace void Free(void* storage) nothrow{released++;free(storage);}
        }
        void ResetPartition(DisjointSet* sets) nothrow{sets.Reset();}
        bool Singleton(immutable(DisjointSet)* sets){
            if(sets.SetCount!=sets.ElementCount)return false;
            for(nuint i=0;i<sets.ElementCount;i++){
                if(sets.Find(i)!=i||sets.SizeOf(i)!=1)return false;
                for(nuint j=0;j<sets.ElementCount;j++)if(sets.Connected(i,j)!=(i==j))return false;
            }
            return true;
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ReusedPartitionsMatchIndependentReachabilityForUnrelatedGraphBatches(int optimization)
    {
        const int count = 17, batches = 4, edgesPerBatch = 36;
        var random = new Random(857201);
        var bytes = new List<byte>();
        for (int batch = 0; batch < batches; batch++)
        {
            var graph = Enumerable.Range(0, count).Select(_ => new HashSet<int>()).ToArray();
            int[] Labels()
            {
                int[] labels = Enumerable.Repeat(-1, count).ToArray();
                for (int start = 0; start < count; start++)
                {
                    if (labels[start] >= 0) continue;
                    var queue = new Queue<int>(); queue.Enqueue(start); labels[start] = start;
                    while (queue.TryDequeue(out int current))
                        foreach (int next in graph[current])
                            if (labels[next] < 0) { labels[next] = start; queue.Enqueue(next); }
                }
                return labels;
            }
            void Snapshot()
            {
                int[] labels = Labels();
                bytes.Add((byte)labels.Distinct().Count());
                bytes.AddRange(labels.Select(value => (byte)value));
            }
            Snapshot();
            for (int edge = 0; edge < edgesPerBatch; edge++)
            {
                int first, second;
                if (batch == 0) { first = random.Next(9); second = random.Next(9); }
                else if (batch == 1) { first = random.Next(9) * 2; second = random.Next(9) * 2; }
                else if (batch == 2)
                {
                    int group = random.Next(3);
                    int[] vertices = Enumerable.Range(0, count).Where(value => value % 3 == group).ToArray();
                    first = vertices[random.Next(vertices.Length)]; second = vertices[random.Next(vertices.Length)];
                }
                else { first = random.Next(count); second = random.Next(count); }
                int[] before = Labels();
                bytes.Add((byte)first); bytes.Add((byte)second); bytes.Add((byte)(before[first] != before[second] ? 1 : 0));
                graph[first].Add(second); graph[second].Add(first); Snapshot();
            }
        }
        string hex = Convert.ToHexString(bytes.ToArray());
        Run(Counters + $$"""
            int Digit(char value)nothrow{if(value<='9')return value-'0';return value-'A'+10;}
            nuint Next(readonly(char)* text,ref nuint at)nothrow{nuint value=(nuint)((Digit(text[at])<<4)|Digit(text[at+1]));at+=2;return value;}
            bool Verify(immutable(DisjointSet)* sets,readonly(char)* text,ref nuint at){
                nuint expectedCount=Next(text,ref at);nuint[17] labels;
                for(nuint i=0;i<17;i++)labels[i]=Next(text,ref at);
                if(sets.ElementCount!=17||sets.SetCount!=expectedCount)return false;
                for(nuint i=0;i<17;i++){
                    nuint root=sets.Find(i);if(root>=17||sets.Find(root)!=root)return false;
                    nuint size=0;
                    for(nuint j=0;j<17;j++){
                        bool connected=labels[i]==labels[j];if(connected)size++;
                        if(sets.Connected(i,j)!=connected)return false;
                    }
                    if(sets.SizeOf(i)!=size)return false;
                }
                return true;
            }
            int main(){
                {DisjointSet sets=new(17);immutable(DisjointSet)* view=&sets;
                 readonly(char)* data="{{hex}}";nuint at=0;
                 int beforeAttempts=attempts;int beforeFrees=released;deny=true;
                 for(int batch=0;batch<{{batches}};batch++){
                    ResetPartition(&sets);
                    if(!Singleton(view)||!Verify(view,data,ref at))return 1;
                    for(int edge=0;edge<{{edgesPerBatch}};edge++){
                        nuint first=Next(data,ref at);nuint second=Next(data,ref at);bool expected=Next(data,ref at)!=0;
                        if(sets.Union(first,second)!=expected||!Verify(view,data,ref at))return 2;
                    }
                 }
                 if(at!={{hex.Length}}||attempts!=beforeAttempts||released!=beforeFrees)return 3;
                 deny=false;}
                if(allocated!=released)return 4;return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EmptySingletonAndMovedOwnersResetIdempotentlyWithoutAllocatingOrFreeing(int optimization)
        => Run(Counters + """
            int main(){
                {DisjointSet empty=new();DisjointSet countedEmpty=new(0);DisjointSet single=new(1);
                 DisjointSet original=new(33);original.Union(0,32);original.Union(1,2);
                 DisjointSet moved=move original;
                 DisjointSet many=new(257);for(nuint i=1;i<257;i++)many.Union(0,i);
                 int beforeAttempts=attempts;int beforeFrees=released;deny=true;
                 for(int cycle=0;cycle<3;cycle++){
                    ResetPartition(&empty);ResetPartition(&countedEmpty);ResetPartition(&single);
                    ResetPartition(&moved);ResetPartition(&many);
                    if(!Singleton(&empty)||!Singleton(&countedEmpty)||!Singleton(&single)||!Singleton(&moved)||!Singleton(&many))return 1;
                    if(empty.ElementCount!=0||countedEmpty.ElementCount!=0||single.ElementCount!=1||moved.ElementCount!=33||many.ElementCount!=257)return 2;
                    if(single.Union(0,0)||!moved.Union(1,32)||moved.Connected(0,32)||moved.SizeOf(1)!=2||moved.SetCount!=32)return 3;
                    if(!many.Union(0,256)||many.SizeOf(256)!=2||many.SetCount!=256)return 4;
                 }
                 if(attempts!=beforeAttempts||released!=beforeFrees)return 5;deny=false;}
                if(allocated!=released)return 6;return 0;
            }
            """, optimization);

    [Theory]
    [InlineData("immutable")]
    [InlineData("readonly")]
    public void ProtectedPartitionViewsCannotReset(string protection)
    {
        string program = "using std::Collections;void Probe(){DisjointSet sets=new(3);" +
            protection + "(DisjointSet)* view=&sets;view.Reset();}";
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Sources(program)));
        Assert.Contains($"Cannot call non-{protection}", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Reset", error.Message, StringComparison.Ordinal);
        Assert.Contains($"on {protection} instance", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<SourceFile> Sources(string program)
    {
        string[] files = ["Collections/IEnumerable.gf", "Queries/Enumerable.gf", "Memory/Span.gf",
            "Collections/Vector.gf", "Collections/DisjointSet.gf"];
        return files.Select(file => new SourceFile(file,
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", file))))
            .Append(new SourceFile("disjoint-set-reset.gf", program));
    }

    private static void Run(string program, int optimization)
    {
        var result = CompilerTestHelper.Run(Sources(program), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }
}
