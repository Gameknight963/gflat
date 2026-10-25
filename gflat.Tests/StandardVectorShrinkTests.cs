namespace gflat.Tests;

public sealed class StandardVectorShrinkTests
{
    private static IEnumerable<SourceFile> Sources(string program, bool managed = false)
    {
        string[] names = ["core/Collections/IEnumerable.gf", "core/Queries/Enumerable.gf",
            "core/IStringConvertable.gf", "core/IHashable.gf", "core/IEquatable.gf", "core/IComparable.gf",
            "core/Hashing.gf", "core/String.gf", "core/ByteSearch.gf", "core/Collections/Vector.gf", "core/Span.gf"];
        return names.Concat(managed ? ["libc/GC.gf", "libc/TracingCollector.gf"] : Array.Empty<string>())
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", name))))
            .Append(new SourceFile("vector-shrink.gf", program));
    }

    private const string Allocator = """
        using std; using std::Collections;
        extern void*? malloc(nuint bytes);extern void free(void* pointer);
        int allocations=0;int frees=0;nuint lastSize=0;bool deny=false;
        namespace Memory{
            replace void*? Allocate(nuint bytes){
                if(deny)return null;
                void*? result=malloc(bytes);if(result!=null){allocations++;lastSize=bytes;}return result;
            }
            replace void Free(void* pointer) nothrow{frees++;free(pointer);}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ShrinksExactlyReleasesEmptyBufferAndTightCallsDoNothing(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources(Allocator + """
            int main(){
                {
                    Vector<int> values=new();values.ShrinkToFit();
                    if(allocations!=0 || frees!=0 || values.Capacity!=0)return 1;
                    values.Reserve(32);for(int i=0;i<5;i++)values.Add(i*7);
                    values.ShrinkToFit();
                    if(values.Capacity!=5 || values.Count!=5 || allocations!=2 || frees!=1 || lastSize!=5*(nuint)sizeof(int))return 2;
                    for(nuint i=0;i<5;i++)if(values[i]!=(int)i*7)return 3;
                    int* saved=&values[0];deny=true;values.ShrinkToFit();deny=false;
                    if(&values[0]!=saved || allocations!=2 || frees!=1)return 4;
                    values.Clear();if(values.Capacity!=5)return 5;
                    deny=true;values.ShrinkToFit();deny=false;
                    if(values.Count!=0 || values.Capacity!=0 || allocations!=2 || frees!=2)return 6;
                    values.ShrinkToFit();if(frees!=2)return 7;
                    values.Add(99);if(values[0]!=99 || values.Capacity==0)return 8;
                }
                if(allocations!=frees)return 9;return 0;
            }
            """), optimization);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FailedShrinkPreservesAllocationBorrowedViewsAndNoncopyableOwners(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources(Allocator + """
            int dead=0;
            struct Owner{public int* Data;public Owner(int id){Data=new* int(id);}~Owner(){dead++;delete Data;}}
            int main(){
                {
                    Vector<Owner> values=new();values.Reserve(20);
                    for(int i=0;i<8;i++)values.Add(new Owner(i));
                    values.RemoveRange(2,3);if(dead!=3)return 1;
                    Owner* saved=&values[0];Span<Owner> borrowed=values.AsSpan();nuint capacity=values.Capacity;
                    int oldAllocations=allocations;int oldFrees=frees;bool caught=false;
                    deny=true;try{values.ShrinkToFit();}catch(BadAllocException* error){caught=true;}deny=false;
                    if(!caught || values.Count!=5 || values.Capacity!=capacity || &values[0]!=saved || dead!=3)return 2;
                    if(allocations!=oldAllocations || frees!=oldFrees || *borrowed[2].Data!=5)return 3;
                    values.ShrinkToFit();
                    if(values.Count!=5 || values.Capacity!=5 || dead!=3 || allocations!=oldAllocations+1 || frees!=oldFrees+1)return 4;
                    if(*values[0].Data!=0 || *values[1].Data!=1 || *values[2].Data!=5 || *values[3].Data!=6 || *values[4].Data!=7)return 5;
                    values.Reserve(30);values.ShrinkToFit();if(values.Capacity!=5 || dead!=3)return 6;
                }
                if(dead!=8 || allocations!=frees)return 7;return 0;
            }
            """), optimization);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ZeroSizedElementsReserveOneBytePerLogicalSlotAndCanShrinkToZero(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources(Allocator + """
            struct Empty{}
            int main(){
                {Vector<Empty> values=new();values.Reserve(20);
                 for(int i=0;i<7;i++)values.Add(new Empty());
                 values.ShrinkToFit();if(values.Capacity!=7 || values.Count!=7 || lastSize!=7)return 1;
                 values.Truncate(1);values.ShrinkToFit();if(values.Capacity!=1 || lastSize!=1)return 2;
                 values.Clear();values.ShrinkToFit();if(values.Capacity!=0)return 3;}
                return allocations-frees;
            }
            """), optimization);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ShrinkingRelocatesManagedRootsAndEmptyReleaseDropsThem(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std; using std::Collections;
            class Node{public int Id;public Node^? Next;public Node(int id){Id=id;}}
            void Fill(Vector<Node^>* values){for(int i=0;i<12;i++){Node^ node=new^ Node(i);node.Next=node;values.Add(node);}}
            int main(){Vector<Node^> values=new();values.Reserve(100);Fill(&values);
                values.RemoveRange(2,7);GC::Collect();if(TracingCollector::ManagedCount!=5)return 1;
                values.ShrinkToFit();GC::Collect();
                if(values.Capacity!=5 || values.Count!=5 || TracingCollector::ManagedCount!=5)return 2;
                if(values[0].Id!=0 || values[1].Id!=1 || values[2].Id!=9 || values[3].Id!=10 || values[4].Id!=11)return 3;
                values.Truncate(1);values.ShrinkToFit();GC::Collect();
                if(values.Capacity!=1 || TracingCollector::ManagedCount!=1 || values[0].Id!=0)return 4;
                values.Clear();values.ShrinkToFit();GC::Collect();
                if(values.Capacity!=0)return 5;return (int)TracingCollector::ManagedCount;}
            """, managed: true), optimization);
        Assert.Equal(0, result.ExitCode);
    }
}
