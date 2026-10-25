namespace gflat.Tests;

public sealed class StandardLinkedListRemoveAllTests
{
    private const string Allocator="""
        using std; using std::Queries; using std::Collections;
        private extern void*? malloc(nuint size);
        private extern void free(void* storage);
        int attempts=0;int allocated=0;int freed=0;bool deny=false;
        namespace Memory {
            public replace void*? Allocate(nuint size){attempts++;if(deny)return null;void*? result=malloc(size);if(result!=null)allocated++;return result;}
            public replace void Free(void* storage) nothrow{freed++;free(storage);}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FixedAndSeededMasksPreserveOriginalOrderSurvivorAddressesAndBothLinks(int optimization)
    {
        var cases=new List<(int Length,uint Mask)>{(0,0),(1,0),(1,1),(8,0),(8,255),(8,129),(8,126),(8,85)};
        var random=new Random(73819);
        for(int i=0;i<32;i++){int length=random.Next(17);cases.Add((length,(uint)random.Next(1<<length)));}
        var survivors=cases.Select(value=>Enumerable.Range(0,value.Length).Where(index=>(value.Mask&(1u<<index))==0).ToArray()).ToArray();
        string lengths=string.Join(',',cases.Select(value=>value.Length));
        string masks=string.Join(',',cases.Select(value=>value.Mask+"u"));
        string counts=string.Join(',',survivors.Select(value=>value.Length));
        int[] oracle=survivors.SelectMany(value=>value).ToArray();
        Run(Allocator+$$"""

            uint mask=0u;int calls=0;bool ordered=true;
            bool Match(immutable(int)* value){if(*value!=calls)ordered=false;calls++;return (mask&(1u<<*value))!=0u;}
            int main(){
                int[{{cases.Count}}] lengths=[{{lengths}}];uint[{{cases.Count}}] masks=[{{masks}}];
                int[{{cases.Count}}] counts=[{{counts}}];int[{{oracle.Length}}] oracle=[{{string.Join(',',oracle)}}];nuint offset=0;
                for(nuint row=0;row<{{cases.Count}};row++){
                    LinkedList<int> list=new();LinkedList<int>::Node*?[16] saved;
                    for(int id=0;id<lengths[row];id++)saved[(nuint)id]=list.AddLast(id);
                    calls=0;ordered=true;mask=masks[row];int before=attempts;deny=true;
                    nuint removed=list.RemoveAll(&Match);deny=false;
                    if(attempts!=before||!ordered||calls!=lengths[row])return 1;
                    if(removed!=(nuint)(lengths[row]-counts[row])||list.Count!=(nuint)counts[row])return 2;
                    LinkedList<int>::Node*? node=list.First;LinkedList<int>::Node*? previous=null;
                    for(int i=0;i<counts[row];i++){
                        int id=oracle[offset+(nuint)i];
                        if(node==null||node!=saved[(nuint)id]||node!.Value!=id||node!.Previous!=previous)return 3;
                        previous=node;node=node!.Next;
                    }
                    if(node!=null||list.Last!=previous)return 4;
                    node=list.Last;LinkedList<int>::Node*? next=null;
                    for(int i=counts[row]-1;i>=0;i--){
                        if(node==null||node!.Value!=oracle[offset+(nuint)i]||node!.Next!=next)return 5;
                        next=node;node=node!.Previous;
                    }
                    if(node!=null||list.First!=next)return 6;
                    offset+=(nuint)counts[row];
                }
                if(offset!={{oracle.Length}}||allocated!=freed)return 7;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NoncopyableOwnersAreUnlinkedBeforeDestructionWithoutAllocationAndEmptyListsReuseState(int optimization)
        =>Run(Allocator+"""

            int made=0;int dead=0;int calls=0;bool observe=false;bool invalidLink=false;
            LinkedList<Owner>*? observed=null;
            struct Owner {
                public int Id;
                public Owner(int id){Id=id;made++;}
                ~Owner(){
                    dead++;
                    if(observe){
                        LinkedList<Owner>::Node*? node=observed!.First;
                        LinkedList<Owner>::Node*? previous=null;nuint seen=0;
                        while(node!=null){
                            if(node!.Value.Id==Id||node!.Previous!=previous)invalidLink=true;
                            previous=node;node=node!.Next;seen++;
                        }
                        if(previous!=observed!.Last||seen!=observed!.Count)invalidLink=true;
                    }
                }
            }
            bool Even(immutable(Owner)* value){calls++;return value.Id%2==0;}
            bool All(immutable(Owner)* value){calls++;return true;}
            int main(){
                {
                    LinkedList<Owner> list=new();observed=&list;observe=true;
                    deny=true;int before=attempts;
                    if(list.RemoveAll(&All)!=0||calls!=0||attempts!=before||!list.IsEmpty)return 1;
                    deny=false;for(int i=0;i<12;i++)list.AddLast(new Owner(i));
                    before=attempts;deny=true;
                    if(list.RemoveAll(&Even)!=6||calls!=12||dead!=6||list.Count!=6)return 2;
                    if(attempts!=before||invalidLink)return 3;
                    calls=0;if(list.RemoveAll(&All)!=6||calls!=6||dead!=12||!list.IsEmpty)return 4;
                    if(list.First!=null||list.Last!=null||invalidLink||attempts!=before)return 5;
                    calls=0;if(list.RemoveAll(&All)!=0||calls!=0||attempts!=before)return 6;
                    deny=false;before=attempts;list.AddFirst(new Owner(42));
                    if(attempts!=before+1||list.Count!=1||list.First!=list.Last)return 7;
                    list.Clear();observe=false;observed=null;
                }
                if(made!=13||dead!=13||invalidLink||allocated!=freed)return 8;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryPredicateFailureCommitsEarlierRemovalsAndRetainsCurrentSuffixAndOwnerBalance(int optimization)
    {
        int[][] survivors=Enumerable.Range(0,12).Select(failure=>Enumerable.Range(0,12)
            .Where(id=>!(id<failure&&id%3!=1)).ToArray()).ToArray();
        int[] oracle=survivors.SelectMany(value=>value).ToArray();
        Run(Allocator+$$"""

            int made=0;int dead=0;int calls=0;int failAt=-1;bool ordered=true;
            struct Owner {public int Id;public Owner(int id){Id=id;made++;}~Owner(){dead++;} }
            bool Match(immutable(Owner)* value){
                int index=calls;calls++;if(value.Id!=index)ordered=false;
                if(index==failAt)throw new Exception("predicate failure",73);
                return value.Id%3!=1;
            }
            int main(){
                int[12] counts=[{{string.Join(',',survivors.Select(value=>value.Length))}}];
                int[{{oracle.Length}}] oracle=[{{string.Join(',',oracle)}}];nuint offset=0;
                for(int failure=0;failure<12;failure++){
                    LinkedList<Owner> list=new();LinkedList<Owner>::Node*?[12] saved;
                    for(int id=0;id<12;id++)saved[(nuint)id]=list.AddLast(new Owner(id));
                    int destroyed=dead;calls=0;failAt=failure;ordered=true;bool caught=false;
                    try{list.RemoveAll(&Match);}catch(ref Exception error){caught=error.GetCode()==73;}
                    if(!caught||!ordered||calls!=failure+1||list.Count!=(nuint)counts[(nuint)failure])return 1;
                    if(dead-destroyed!=12-counts[(nuint)failure]||made-dead!=counts[(nuint)failure])return 2;
                    LinkedList<Owner>::Node*? node=list.First;LinkedList<Owner>::Node*? previous=null;
                    for(int i=0;i<counts[(nuint)failure];i++){
                        int id=oracle[offset+(nuint)i];
                        if(node==null||node!=saved[(nuint)id]||node!.Value.Id!=id||node!.Previous!=previous)return 3;
                        previous=node;node=node!.Next;
                    }
                    if(node!=null||list.Last!=previous)return 4;
                    node=list.Last;LinkedList<Owner>::Node*? next=null;
                    for(int i=counts[(nuint)failure]-1;i>=0;i--){
                        if(node==null||node!.Value.Id!=oracle[offset+(nuint)i]||node!.Next!=next)return 5;
                        next=node;node=node!.Previous;
                    }
                    if(node!=null||list.First!=next)return 6;
                    offset+=(nuint)counts[(nuint)failure];
                    // Retained nodes, including the throwing element, are still ordinary live nodes.
                    list.AddLast(new Owner(99));list.Clear();
                    if(!list.IsEmpty||made!=dead)return 7;
                }
                if(offset!={{oracle.Length}}||allocated!=freed)return 8;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RemovingManagedElementNodesRetiresRootsAndKeepsSurvivorAlive(int optimization)
    {
        const string program="""
            using std; using std::Queries; using std::Collections;
            public static replace nuint std::TracingCollector::CollectionThreshold()=>0;
            void Populate(ref LinkedList<int^> values){
                values.AddLast(new^ int(1));
                values.AddLast(new^ int(2));
                values.AddLast(new^ int(3));
            }
            bool Remove(immutable(int^)* value)=>**value!=2;
            int ReadSurvivor(LinkedList<int^>* values)=>*values.First!.Value;
            int main(){
                LinkedList<int^> values=new();
                Populate(ref values);
                if(TracingCollector::ManagedCount!=3)return 1;
                if(values.RemoveAll(&Remove)!=2||values.Count!=1)return 2;
                if(GC::Collect()!=2||TracingCollector::ManagedCount!=1)return 3;
                if(ReadSurvivor(&values)!=2)return 4;
                values.Clear();
                if(GC::Collect()!=1||TracingCollector::ManagedCount!=0)return 5;
                return 0;
            }
            """;
        // Match StandardLinkedListTests.Sources(gc:true), not an alternate collector.
        string[] names=["core/Collections/IEnumerable.gf","core/Queries/Enumerable.gf",
            "core/Collections/LinkedList.gf","libc/GC.gf","libc/TracingCollector.gf"];
        var sources=names.Select(name=>new SourceFile(name,File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory,"Std",name))))
            .Append(new SourceFile("linked-list-remove-all-managed.gf",program));
        var result=CompilerTestHelper.Run(sources,optimization);
        Assert.True(result.ExitCode==0,$"Managed LinkedList RemoveAll exit {result.ExitCode}: {result.StandardError}");
    }

    private static void Run(string program,int optimization)
    {
        string[] names=["Collections/IEnumerable.gf","Queries/Enumerable.gf","Collections/LinkedList.gf"];
        var sources=names.Select(name=>new SourceFile(name,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core",name))))
            .Append(new SourceFile("linked-list-remove-all.gf",program));
        var result=CompilerTestHelper.Run(sources,optimization);
        Assert.True(result.ExitCode==0,$"LinkedList RemoveAll exit {result.ExitCode}: {result.StandardError}");
    }
}
