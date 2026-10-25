namespace gflat.Tests;

public sealed class StandardVectorTryPopTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NoncopyableOwnersTransferWithoutAllocationAndEmptyPreservesOutput(int optimization)
        => Run("""
            using std; using std::Collections;
            extern void*? malloc(nuint size);extern void free(void* pointer);
            int attempts=0;int allocated=0;int freed=0;int made=0;int dead=0;bool deny=false;
            namespace Memory {
                public replace void*? Allocate(nuint size){attempts++;if(deny)return null;
                    void*? value=malloc(size);if(value!=null)allocated++;return value;}
                public replace void Free(void* pointer) nothrow{freed++;free(pointer);}
            }
            struct Item{public int Id;public Item(int id){Id=id;made++;}~Item(){dead++;}}
            int main(){
                {
                    Vector<Unique<Item>> values=new();Unique<Item> output=new(new* Item(99));
                    Item* original=&output.Value;
                    if(values.TryPop(ref output)||&output.Value!=original||dead!=0)return 1;
                    values.Reserve(16);
                    if(values.TryPop(ref output)||&output.Value!=original||dead!=0)return 2;
                    for(int i=0;i<9;i++)values.Add(new Unique<Item>(new* Item(i)));
                    nuint capacity=values.Capacity;int before=attempts;deny=true;
                    for(int i=8;i>=0;i--){
                        if(!values.TryPop(ref output)||output.Value.Id!=i)return 3;
                        if(values.Count!=(nuint)i||values.Capacity!=capacity||dead!=9-i)return 4;
                    }
                    Item* retained=&output.Value;
                    if(values.TryPop(ref output)||&output.Value!=retained||dead!=9)return 5;
                    if(attempts!=before)return 6;deny=false;
                }
                if(made!=10||dead!=made||allocated!=freed)return 7;
                return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void LiveAndConstructedSpareOutputAliasesRejectBeforeMutation(int optimization)
        => Run("""
            using std; using std::Collections;
            int made=0;int dead=0;
            struct Item{public int Id;public Item(int id){Id=id;made++;}~Item(){dead++;}}
            int main(){
                {
                    Vector<Item> values=new();values.Reserve(8);
                    for(int i=0;i<4;i++)values.Add(new Item(i));
                    for(nuint slot=0;slot<4;slot++){
                        int before=dead;bool caught=false;
                        try{values.TryPop(ref values[slot]);}catch{caught=true;}
                        if(!caught||values.Count!=4||values.Capacity!=8||dead!=before)return 1;
                        for(nuint i=0;i<4;i++)if(values[i].Id!=(int)i)return 2;
                    }
                    // Explicitly initialize backed spare slots; the vector does not own them.
                    for(nuint slot=4;slot<8;slot++){
                        Item* spare=&values[(nuint)0]+slot;
                        Item* placed=new* Item(66) at spare;
                        int before=dead;bool caught=false;
                        try{values.TryPop(ref *placed);}catch{caught=true;}
                        if(!caught||values.Count!=4||values.Capacity!=8||dead!=before||placed.Id!=66)return 3;
                        for(nuint i=0;i<4;i++)if(values[i].Id!=(int)i)return 4;
                        placed.~Item();
                    }
                    Item output=new(99);
                    if(!values.TryPop(ref output)||output.Id!=3||values.Count!=3)return 5;
                }
                return made-dead;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ZeroSizedOwnerUsesAllocationStrideAndRetainsOrdinaryCleanup(int optimization)
        => Run("""
            using std; using std::Collections;
            int made=0;int dead=0;
            struct Empty{public Empty(){made++;}~Empty(){dead++;}}
            int main(){
                if(sizeof(Empty)!=0)return 1;
                {
                    Vector<Empty> values=new();values.Reserve(4);values.Add(new Empty());
                    Empty output=new();bool caught=false;
                    try{values.TryPop(ref values[(nuint)0]);}catch{caught=true;}
                    if(!caught||values.Count!=1||values.Capacity!=4||dead!=0)return 2;
                    if(!values.TryPop(ref output)||values.Count!=0||values.Capacity!=4||dead!=1)return 3;
                    if(values.TryPop(ref output)||dead!=1)return 4;
                }
                return made-dead;
            }
            """,optimization);

    private static void Run(string program,int optimization)
    {
        string[] names=["Collections/IEnumerable.gf","Queries/Enumerable.gf","Span.gf","Collections/Vector.gf","Unique.gf"];
        var sources=names.Select(name=>new SourceFile(name,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core",name))))
            .Append(new SourceFile("vector-try-pop.gf",program));
        var result=CompilerTestHelper.Run(sources,optimization);
        Assert.True(result.ExitCode==0,$"Vector TryPop exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
