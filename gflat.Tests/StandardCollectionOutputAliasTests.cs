namespace gflat.Tests;

public sealed class StandardCollectionOutputAliasTests
{
    private const string Prelude="""
        using std; using std::Memory; using std::Queries; using std::Collections;
        int made=0;int dead=0;int calls=0;
        struct Item {
            public int Id;
            public Item(int id){Id=id;made++;}
            ~Item(){dead++;}
        }
        int Compare(immutable(Item)* left,immutable(Item)* right){calls++;return left.Id-right.Id;}
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void StackRejectsTopAndOtherLiveOutputsBeforeMovingOrDestroyingOwners(int optimization)
        => Run(Prelude+"""
            int main(){
                for(int slotIndex=0;slotIndex<4;slotIndex++){
                    Stack<Item> items=new();for(int i=0;i<4;i++)items.Push(new Item(i));
                    var iterator=items.GetEnumerator();for(int i=0;i<=slotIndex;i++)iterator.MoveNext();
                    int destroyed=dead;bool caught=false;
                    try{items.TryPop(ref iterator.Current);}catch{caught=true;}
                    // Red repro: removal already consumed a live owner and ran
                    // its destructor even though output is that same slot.
                    if(!caught && dead==destroyed+1 && items.Count==3)return 71;
                    if(!caught || dead!=destroyed || items.Count!=4 || made-dead!=4)return 1;
                    var check=items.GetEnumerator();for(int i=3;i>=0;i--){if(!check.MoveNext() || check.Current.Id!=i)return 2;}
                    caught=false;try{items.TryPop(ref items.Top);}catch{caught=true;}
                    if(!caught || dead!=destroyed || items.Count!=4)return 3;
                    Item output=new Item(99);if(!items.TryPop(ref output) || output.Id!=3 || dead!=destroyed+1)return 4;
                    for(int i=2;i>=0;i--){Item value=items.Pop();if(value.Id!=i)return 5;}
                    if(items.TryPop(ref output) || output.Id!=3)return 6;
                }
                return made-dead;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void WrappedQueueRejectsFrontAndOtherLiveOutputsBeforeChangingRingState(int optimization)
        => Run(Prelude+"""
            int main(){
                for(int slotIndex=0;slotIndex<4;slotIndex++){
                    Queue<Item> items=new();items.Reserve(4);for(int i=0;i<4;i++)items.Enqueue(new Item(i));
                    {Item first=items.Dequeue();Item second=items.Dequeue();}
                    items.Enqueue(new Item(4));items.Enqueue(new Item(5));
                    var iterator=items.GetEnumerator();for(int i=0;i<=slotIndex;i++)iterator.MoveNext();
                    int destroyed=dead;bool caught=false;
                    try{items.TryDequeue(ref iterator.Current);}catch{caught=true;}
                    if(!caught || dead!=destroyed || items.Count!=4 || made-dead!=4)return 1;
                    var check=items.GetEnumerator();for(int i=2;i<6;i++){if(!check.MoveNext() || check.Current.Id!=i)return 2;}
                    caught=false;try{items.TryDequeue(ref items.Front);}catch{caught=true;}
                    if(!caught || dead!=destroyed || items.Count!=4)return 3;
                    Item output=new Item(99);if(!items.TryDequeue(ref output) || output.Id!=2 || dead!=destroyed+1)return 4;
                    for(int i=3;i<6;i++){Item value=items.Dequeue();if(value.Id!=i)return 5;}
                    if(items.TryDequeue(ref output) || output.Id!=2)return 6;
                }
                return made-dead;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PriorityQueueRejectsCastAwayReadonlyElementOutputsBeforeCallingComparator(int optimization)
        => Run(Prelude+"""
            int main(){
                for(int slotIndex=0;slotIndex<4;slotIndex++){
                    PriorityQueue<Item> items=new(Compare);for(int i=0;i<4;i++)items.Enqueue(new Item(i));
                    // Explicit immutable casts remain available in the language.
                    Item* slot=(Item*)&items.Top+slotIndex;int destroyed=dead;calls=0;bool caught=false;
                    try{items.TryDequeue(ref *slot);}catch{caught=true;}
                    if(!caught || calls!=0 || dead!=destroyed || items.Count!=4 || made-dead!=4)return 1;
                    Item output=new Item(99);if(!items.TryDequeue(ref output) || output.Id!=0 || dead!=destroyed+1)return 2;
                    for(int i=1;i<4;i++){Item value=items.Dequeue();if(value.Id!=i)return 3;}
                    if(items.TryDequeue(ref output) || output.Id!=0)return 4;
                }
                return made-dead;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ZeroSizedOwnersStillRejectAliasedOutputsAndAcceptExternalStorage(int optimization)
        => Run("""
            using std; using std::Memory; using std::Queries; using std::Collections;
            int made=0;int dead=0;
            struct Empty{public Empty(){made++;}~Empty(){dead++;}}
            int Compare(immutable(Empty)* left,immutable(Empty)* right)=>0;
            int main(){
                if(sizeof(Empty)!=0)return 1;
                {Stack<Empty> items=new();items.Push(new Empty());Empty output=new();
                    bool caught=false;try{items.TryPop(ref items.Top);}catch{caught=true;}
                    if(!caught || items.Count!=1 || dead!=0)return 2;
                    if(!items.TryPop(ref output) || items.TryPop(ref output))return 3;
                }
                if(made!=dead)return 4;
                {Queue<Empty> items=new();items.Enqueue(new Empty());Empty output=new();int before=dead;
                    bool caught=false;try{items.TryDequeue(ref items.Front);}catch{caught=true;}
                    if(!caught || items.Count!=1 || dead!=before)return 5;
                    if(!items.TryDequeue(ref output) || items.TryDequeue(ref output))return 6;
                }
                if(made!=dead)return 7;
                {PriorityQueue<Empty> items=new(Compare);items.Enqueue(new Empty());Empty output=new();int before=dead;
                    Empty* slot=(Empty*)&items.Top;bool caught=false;try{items.TryDequeue(ref *slot);}catch{caught=true;}
                    if(!caught || items.Count!=1 || dead!=before)return 8;
                    if(!items.TryDequeue(ref output) || items.TryDequeue(ref output))return 9;
                }
                return made-dead;
            }
            """,optimization);

    private static void Run(string program,int optimization)
    {
        string[] names=["Collections/IEnumerable.gf","Queries/Enumerable.gf","Memory/Span.gf","Collections/Vector.gf","Collections/Stack.gf","Collections/Queue.gf","Collections/PriorityQueue.gf"];
        var sources=names.Select(name=>new SourceFile(name,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core",name))))
            .Append(new SourceFile("output-alias.gf",program));
        string directory=Path.Combine(Path.GetTempPath(),"gflat_output_alias_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try{
            string ir=Path.Combine(directory,"alias.ll"),executable=Path.Combine(directory,"alias.exe");File.WriteAllText(ir,Compiler.Emit(sources,options:new(){InternalizeFunctions=true}));
            var compilation=NativeToolchain.Compile(ir,executable,optimizationLevel:optimization);Assert.True(compilation.ExitCode==0,compilation.StandardError);
            var result=NativeToolchain.Run(executable,[]);Assert.True(result.ExitCode==0,$"Collection output alias exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }finally{Directory.Delete(directory,true);}
    }
}

