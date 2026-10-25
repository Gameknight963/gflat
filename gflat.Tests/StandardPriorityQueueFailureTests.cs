namespace gflat.Tests;

public sealed class StandardPriorityQueueFailureTests
{
    private const string Prelude = """
        using std; using std::Collections;
        int made=0;int dead=0;int allocations=0;int releases=0;
        int calls=0;int failAt=-1;bool failAllocation=false;
        private extern void*? malloc(nuint bytes);
        private extern void free(void* storage);
        namespace Memory {
            public replace void*? Allocate(nuint bytes) {
                if(failAllocation)return null;
                void*? result=malloc(bytes);if(result!=null)allocations++;return result;
            }
            public replace void Free(void* storage) nothrow{releases++;free(storage);}
        }
        struct Item {
            public int Priority;public String Text;
            public Item(int priority){Priority=priority;Text=s"owned";made++;}
            ~Item(){dead++;}
        }
        int Compare(immutable(Item)* left,immutable(Item)* right){
            int index=calls;calls++;if(index==failAt)throw new Exception("comparison");
            return left.Priority-right.Priority;
        }
        void Fill(PriorityQueue<Item>* items){for(int i=0;i<31;i++)items.Enqueue(new Item(i));}
        bool Drain(PriorityQueue<Item>* items){
            for(int i=0;i<31;i++){Item value=items.Dequeue();if(value.Priority!=i || value.Text.Length!=5)return false;}
            return items.IsEmpty;
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryInsertionComparisonFailurePreservesHeapAndDisposesIncomingOwner(int optimization)
        => Run(Prelude+"""
            int main(){
                int comparisons=0;
                {PriorityQueue<Item> probe=new(Compare);Fill(&probe);calls=0;probe.Enqueue(new Item(-1));comparisons=calls;}
                if(comparisons<4)return 1;
                for(int failure=0;failure<comparisons;failure++){
                    PriorityQueue<Item> items=new(Compare);Fill(&items);
                    immutable(Item)* saved=&items.Top;int memory=allocations-releases;int destroyed=dead;
                    calls=0;failAt=failure;bool caught=false;
                    try{items.Enqueue(new Item(-1));}catch{caught=true;}
                    failAt=-1;
                    if(!caught || calls!=failure+1 || items.Count!=31 || &items.Top!=saved || saved.Priority!=0)return 2;
                    if(dead!=destroyed+1 || made-dead!=31 || allocations-releases!=memory)return 3;
                    if(!Drain(&items))return 4;
                }
                if(made!=dead || allocations!=releases)return 5;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryRemovalComparisonFailurePreservesHeapAndTryOutputOwner(int optimization)
        => Run(Prelude+"""
            int main(){
                int comparisons=0;
                {PriorityQueue<Item> probe=new(Compare);Fill(&probe);calls=0;probe.Dequeue();comparisons=calls;}
                if(comparisons<6)return 1;
                for(int useTry=0;useTry<2;useTry++)
                for(int failure=0;failure<comparisons;failure++){
                    PriorityQueue<Item> items=new(Compare);Fill(&items);Item output=new Item(99);
                    readonly(char)*? outputData=output.Text.Data;
                    immutable(Item)* saved=&items.Top;int memory=allocations-releases;int destroyed=dead;
                    calls=0;failAt=failure;bool caught=false;
                    try{if(useTry==1)items.TryDequeue(ref output);else items.Dequeue();}catch{caught=true;}
                    failAt=-1;
                    if(!caught || calls!=failure+1 || items.Count!=31 || &items.Top!=saved || saved.Priority!=0)return 2;
                    if(dead!=destroyed || made-dead!=32 || allocations-releases!=memory)return 3;
                    if(output.Priority!=99 || output.Text.Data!=outputData || output.Text.Length!=5)return 4;
                    if(!Drain(&items))return 5;
                    if(items.TryDequeue(ref output) || output.Priority!=99 || output.Text.Data!=outputData)return 6;
                }
                if(made!=dead || allocations!=releases)return 7;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AllocationFailureAfterComparisonPlanningPreservesOldStorageAndOwners(int optimization)
        => Run(Prelude+"""
            int main(){ {
                PriorityQueue<Item> items=new(Compare);items.Reserve(31);Fill(&items);
                immutable(Item)* saved=&items.Top;int memory=allocations-releases;
                Item incoming=new Item(-1);int destroyed=dead;
                calls=0;failAllocation=true;bool caught=false;
                try{items.Enqueue(move incoming);}catch{caught=true;}
                failAllocation=false;
                if(!caught || calls<4 || items.Count!=31 || items.Capacity!=31 || &items.Top!=saved)return 1;
                if(dead!=destroyed+1 || made-dead!=31 || allocations-releases!=memory)return 2;
                if(!Drain(&items))return 3;
            }
            if(made!=dead || allocations!=releases)return 4;return 0;}
            """,optimization);

    private static void Run(string program,int optimization)
    {
        string[] names=["Collections/IEnumerable.gf","Collections/Enumerable.gf","IStringConvertable.gf","IHashable.gf","IEquatable.gf","IComparable.gf","Hashing.gf","String.gf", "ByteSearch.gf","Collections/Vector.gf","Span.gf","Collections/PriorityQueue.gf"];
        var sources=names.Select(name=>new SourceFile(name,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core",name))))
            .Append(new SourceFile("priority-failure.gf",program));
        string directory=Path.Combine(Path.GetTempPath(),"gflat_priority_failure_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try{
            string ir=Path.Combine(directory,"priority.ll"),executable=Path.Combine(directory,"priority.exe");
            File.WriteAllText(ir,Compiler.Emit(sources,options:new(){InternalizeFunctions=true}));
            var compilation=NativeToolchain.Compile(ir,executable,optimizationLevel:optimization);Assert.True(compilation.ExitCode==0,compilation.StandardError);
            var result=NativeToolchain.Run(executable,[]);Assert.True(result.ExitCode==0,$"Priority queue failure exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }finally{Directory.Delete(directory,true);}
    }
}
