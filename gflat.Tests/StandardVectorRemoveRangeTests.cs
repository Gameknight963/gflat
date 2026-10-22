namespace gflat.Tests;

public sealed class StandardVectorRemoveRangeTests
{
    private static IEnumerable<SourceFile> Sources(string program, bool managed = false)
    {
        string[] names = ["core/Collections/IEnumerable.gf", "core/Collections/Enumerable.gf",
            "core/IStringConvertable.gf", "core/IHashable.gf", "core/IEquatable.gf", "core/IComparable.gf",
            "core/Hashing.gf", "core/String.gf", "core/ByteSearch.gf", "core/Vector.gf", "core/Span.gf"];
        return names.Concat(managed ? ["libc/GC.gf", "libc/TracingCollector.gf"] : Array.Empty<string>())
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", name))))
            .Append(new SourceFile("vector-remove-range.gf", program));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EverySmallWindowMatchesListOrderAndPreservesCapacity(int optimization)
    {
        // Independent List oracle includes empty, prefix, middle, suffix, and
        // whole-vector removals with both overlapping and disjoint relocations.
        var oracle = new List<int>();
        for (int count = 0; count <= 9; count++)
        for (int start = 0; start <= count; start++)
        for (int length = 0; length <= count - start; length++)
        {
            var expected = Enumerable.Range(0, count).ToList();
            expected.RemoveRange(start, length);
            oracle.AddRange([count, start, length, expected.Count]);
            oracle.AddRange(expected);
        }
        // One runtime loop exercises the table, keeping IR and optimizer work
        // bounded rather than unrolling hundreds of owning-vector lifetimes.
        string program = $$"""
            using std;
            int main(){int[{{oracle.Count}}] oracle=[{{string.Join(',', oracle)}}];nuint cursor=0;
                while(cursor<{{oracle.Count}}){
                    int count=oracle[cursor++];nuint start=(nuint)oracle[cursor++];
                    nuint length=(nuint)oracle[cursor++];nuint expected=(nuint)oracle[cursor++];
                    Vector<int> values=new();values.Reserve(12);
                    for(int i=0;i<count;i++)values.Add(i);
                    values.RemoveRange(start,length);
                    if(values.Count!=expected || values.Capacity!=12)return 1;
                    for(nuint i=0;i<expected;i++)if(values[i]!=oracle[cursor++])return 2;
                    values.Add(99);if(values.Pop()!=99)return 3;
                }
                return 0;
            }
            """;
        Assert.Equal(0, CompilerTestHelper.Run(Sources(program), optimization).ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NoncopyableOwnersAreDestroyedInReverseAndSurvivorsRelocateWithoutAllocation(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std;
            extern void* malloc(nuint size); extern void free(void* pointer);
            bool deny=false; int allocationAttempts=0;
            namespace Memory {
                replace void*? Allocate(nuint size) {allocationAttempts++;if(deny)return null;return malloc(size);}
                replace void Free(void* pointer){free(pointer);}
            }
            int[12] order; int dead=0;
            struct Owner {
                public int* Data;
                public Owner(int id){Data=new* int(id);}
                ~Owner(){order[dead]=*Data;dead++;delete Data;}
            }
            int main(){
                {
                    Vector<Owner> values=new();values.Reserve(12);
                    for(int i=0;i<8;i++)values.Add(new Owner(i));
                    int before=allocationAttempts;deny=true;
                    values.RemoveRange(2,3);
                    if(allocationAttempts!=before || values.Count!=5 || dead!=3)return 1;
                    if(order[0]!=4 || order[1]!=3 || order[2]!=2)return 2;
                    if(*values[0].Data!=0 || *values[1].Data!=1 || *values[2].Data!=5 || *values[3].Data!=6 || *values[4].Data!=7)return 3;
                    values.RemoveRange(0,0);values.RemoveRange(values.Count,0);
                    if(dead!=3 || allocationAttempts!=before)return 4;
                    values.RemoveRange(3,2);
                    if(order[3]!=7 || order[4]!=6)return 5;
                    values.RemoveRange(0,values.Count);
                    if(dead!=8 || order[5]!=5 || order[6]!=1 || order[7]!=0 || !values.IsEmpty)return 6;
                    deny=false;values.Add(new Owner(8));
                }
                if(dead!=9 || order[8]!=8)return 7;return 0;
            }
            """), optimization);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InvalidAndOverflowingRangesPreserveEveryOwner(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std;
            int dead=0;
            struct Owner{public int Id;public Owner(int id){Id=id;}~Owner(){dead++;}}
            int main(){
                {
                    Vector<Owner> values=new();for(int i=0;i<5;i++)values.Add(new Owner(i));
                    nuint maximum=(nuint)0-(nuint)1;int caught=0;
                    try{values.RemoveRange(6,0);}catch{caught++;}
                    try{values.RemoveRange(5,1);}catch{caught++;}
                    try{values.RemoveRange(2,4);}catch{caught++;}
                    try{values.RemoveRange(maximum,2);}catch{caught++;}
                    try{values.RemoveRange(1,maximum);}catch{caught++;}
                    if(caught!=5 || dead!=0 || values.Count!=5)return 1;
                    for(nuint i=0;i<values.Count;i++)if(values[i].Id!=(int)i)return 2;
                    values.RemoveRange(5,0);if(dead!=0)return 3;
                }
                return dead-5;
            }
            """), optimization);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ZeroSizedElementsCanRemoveWholeAndPartialRanges(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std;struct Empty{}
            int main(){Vector<Empty> values=new();values.RemoveRange(0,0);
                for(int i=0;i<10;i++)values.Add(new Empty());
                values.RemoveRange(2,5);if(values.Count!=5)return 1;
                values.RemoveRange(0,5);values.Add(new Empty());return (int)values.Count-1;}
            """), optimization);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ManagedSurvivorsStayLiveDuringRemovedOwnerDestructorsAndRelocation(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std;
            class Node{public int Id;public Node^? Next;public Node(int id){Id=id;}}
            int dead=0;bool bad=false;
            struct Owner{
                public Node^ Root;
                public Owner(int id){Root=new^ Node(id);Root.Next=Root;}
                ~Owner(){GC::Collect();if(TracingCollector::ManagedCount!=(nuint)(6-dead))bad=true;dead++;}
            }
            int main(){
                {
                    Vector<Owner> values=new();
                    for(int i=0;i<6;i++)values.Add(new Owner(i));
                    values.RemoveRange(1,3);GC::Collect();
                    if(bad || dead!=3 || TracingCollector::ManagedCount!=3 || values.Count!=3)return 1;
                    if(values[0].Root.Id!=0 || values[1].Root.Id!=4 || values[2].Root.Id!=5)return 2;
                    values.RemoveRange(3,0);if(dead!=3)return 3;
                }
                GC::Collect();if(bad || dead!=6)return 4;return (int)TracingCollector::ManagedCount;
            }
            """, managed: true), optimization);
        Assert.Equal(0, result.ExitCode);
    }
}
