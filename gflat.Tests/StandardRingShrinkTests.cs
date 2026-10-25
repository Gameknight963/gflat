namespace gflat.Tests;

public sealed class StandardRingShrinkTests
{
    private static IEnumerable<SourceFile> Sources(string program, bool managed = false)
    {
        string[] names = ["core/Collections/IEnumerable.gf", "core/Collections/Enumerable.gf",
            "core/IStringConvertable.gf", "core/IHashable.gf", "core/IEquatable.gf", "core/IComparable.gf",
            "core/Hashing.gf", "core/String.gf", "core/ByteSearch.gf", "core/Collections/Vector.gf", "core/Span.gf",
            "core/Collections/Queue.gf", "core/Collections/Deque.gf"];
        return names.Concat(managed ? ["libc/GC.gf", "libc/TracingCollector.gf"] : Array.Empty<string>())
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", name))))
            .Append(new SourceFile("ring-shrink.gf", program));
    }

    public static TheoryData<string, int> Rings => new() { { "Queue", 0 }, { "Queue", 2 }, { "Deque", 0 }, { "Deque", 2 } };
    private static string Program(string text, string ring)
        => text.Replace("RING", ring).Replace("ADD", ring == "Queue" ? "Enqueue" : "AddLast")
            .Replace("REMOVE", ring == "Queue" ? "Dequeue" : "RemoveFirst");

    private const string Allocator = """
        using std; using std::Collections;
        extern void*? malloc(nuint bytes);extern void free(void* pointer);
        int allocations=0;int frees=0;bool deny=false;nuint lastSize=0;
        namespace Memory{
            replace void*? Allocate(nuint bytes){if(deny)return null;void*? result=malloc(bytes);if(result!=null){allocations++;lastSize=bytes;}return result;}
            replace void Free(void* pointer) nothrow{frees++;free(pointer);}
        }
        """;

    [Theory]
    [MemberData(nameof(Rings))]
    public void EverySmallWrappedHeadAndCountPreservesOrderAndTightOrEmptyCallsDoNotAllocate(string ring, int optimization)
    {
        var result = CompilerTestHelper.Run(Sources(Allocator + Program("""
            int main(){
                {RING<int> empty=new();empty.ShrinkToFit();if(allocations!=0 || frees!=0)return 1;}
                for(int rotation=0;rotation<8;rotation++)for(int count=0;count<=8;count++){
                    RING<int> values=new();values.Reserve(8);
                    for(int i=0;i<8;i++)values.ADD(i);
                    for(int i=0;i<rotation;i++)if(values.REMOVE()!=i)return 2;
                    for(int i=0;i<rotation;i++)values.ADD(8+i);
                    for(int i=0;i<8-count;i++)if(values.REMOVE()!=rotation+i)return 3;
                    values.ShrinkToFit();if(values.Count!=(nuint)count || values.Capacity!=(nuint)count)return 4;
                    var iterator=values.GetEnumerator();int expected=rotation+8-count;
                    while(iterator.MoveNext()){if(iterator.Current!=expected)return 5;expected++;}
                    if(expected!=rotation+8)return 6;
                    int beforeAlloc=allocations;int beforeFree=frees;
                    deny=true;values.ShrinkToFit();deny=false;
                    if(allocations!=beforeAlloc || frees!=beforeFree)return 7;
                    for(int i=0;i<count;i++)if(values.REMOVE()!=rotation+8-count+i)return 8;
                    deny=true;values.ShrinkToFit();deny=false;
                    if(values.Capacity!=0 || !values.IsEmpty)return 9;
                    values.ADD(99);if(values.REMOVE()!=99)return 10;
                }
                return allocations-frees;
            }
            """, ring)), optimization);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [MemberData(nameof(Rings))]
    public void WrappedNoncopyableOwnersAndBorrowedFrontSurviveFailedShrink(string ring, int optimization)
    {
        var result = CompilerTestHelper.Run(Sources(Allocator + Program("""
            int dead=0;
            struct Owner{public int* Data;public Owner(int id){Data=new* int(id);}~Owner(){dead++;delete Data;}}
            int main(){
                {
                    RING<Owner> values=new();values.Reserve(10);
                    for(int i=0;i<10;i++)values.ADD(new Owner(i));
                    for(int i=0;i<7;i++)values.REMOVE();
                    for(int i=10;i<14;i++)values.ADD(new Owner(i));
                    if(values.Count!=7 || dead!=7)return 1;
                    Owner* saved=&values.Front;var iterator=values.GetEnumerator();
                    int beforeAlloc=allocations;int beforeFree=frees;bool caught=false;
                    deny=true;try{values.ShrinkToFit();}catch(BadAllocException* error){caught=true;}deny=false;
                    if(!caught || &values.Front!=saved || values.Count!=7 || values.Capacity!=10 || dead!=7)return 2;
                    if(allocations!=beforeAlloc || frees!=beforeFree || !iterator.MoveNext() || *iterator.Current.Data!=7)return 3;
                    values.ShrinkToFit();
                    if(values.Capacity!=7 || dead!=7 || allocations!=beforeAlloc+1 || frees!=beforeFree+1)return 4;
                    for(int i=7;i<14;i++){Owner item=values.REMOVE();if(*item.Data!=i)return 5;}
                    values.ShrinkToFit();if(dead!=14 || values.Capacity!=0)return 6;
                    values.ADD(new Owner(14));
                }
                if(dead!=15 || allocations!=frees)return 7;return 0;
            }
            """, ring)), optimization);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [MemberData(nameof(Rings))]
    public void ZeroSizeElementsKeepOneBytePerSlotAllocationConvention(string ring, int optimization)
    {
        var result = CompilerTestHelper.Run(Sources(Allocator + Program("""
            struct Empty{}
            int main(){
                {RING<Empty> values=new();values.Reserve(10);
                 for(int i=0;i<10;i++)values.ADD(new Empty());
                 for(int i=0;i<7;i++)values.REMOVE();
                 for(int i=0;i<4;i++)values.ADD(new Empty());
                 values.ShrinkToFit();if(values.Count!=7 || values.Capacity!=7 || lastSize!=7)return 1;
                 values.Clear();values.ShrinkToFit();if(values.Capacity!=0)return 2;}
                return allocations-frees;
            }
            """, ring)), optimization);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [MemberData(nameof(Rings))]
    public void WrappedManagedReferencesRemainRootedAfterRelocationAndDropWithRemovedSlots(string ring, int optimization)
    {
        var result = CompilerTestHelper.Run(Sources(Program("""
            using std; using std::Collections;
            class Node{public int Id;public Node^? Next;public Node(int id){Id=id;}}
            void Add(RING<Node^>* values,int first,int last){for(int i=first;i<last;i++){Node^ node=new^ Node(i);node.Next=node;values.ADD(node);}}
            int main(){RING<Node^> values=new();values.Reserve(8);Add(&values,0,8);
                for(int i=0;i<5;i++)values.REMOVE();Add(&values,8,11);
                GC::Collect();if(TracingCollector::ManagedCount!=6)return 1;
                values.ShrinkToFit();GC::Collect();
                if(values.Capacity!=6 || values.Count!=6 || TracingCollector::ManagedCount!=6)return 2;
                {var iterator=values.GetEnumerator();int expected=5;
                 while(iterator.MoveNext()){if(iterator.Current.Id!=expected)return 3;expected++;}if(expected!=11)return 4;}
                values.REMOVE();values.REMOVE();values.ShrinkToFit();GC::Collect();
                if(values.Capacity!=4 || TracingCollector::ManagedCount!=4 || values.Front.Id!=7)return 5;
                values.Clear();values.ShrinkToFit();GC::Collect();
                if(values.Capacity!=0)return 6;return (int)TracingCollector::ManagedCount;}
            """, ring), managed: true), optimization);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DequeBothEndsRemainUsableAfterShrinkingWrappedStorage(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std; using std::Collections;
            int main(){Deque<int> values=new();values.Reserve(8);
                for(int i=1;i<=8;i++)values.AddLast(i);
                for(int i=1;i<=5;i++)if(values.RemoveFirst()!=i)return 1;
                values.AddFirst(5);values.AddLast(9);values.AddLast(10);
                values.ShrinkToFit();if(values.Capacity!=6 || values.Front!=5 || values.Back!=10)return 2;
                values.AddFirst(4);values.AddLast(11);
                if(values.RemoveLast()!=11 || values.RemoveFirst()!=4)return 3;
                values.ShrinkToFit();for(int i=10;i>=5;i--)if(values.RemoveLast()!=i)return 4;
                values.ShrinkToFit();values.AddFirst(42);
                if(values.Front!=42 || values.Back!=42 || values.RemoveLast()!=42)return 5;
                return 0;}
            """), optimization);
        Assert.Equal(0, result.ExitCode);
    }
}
