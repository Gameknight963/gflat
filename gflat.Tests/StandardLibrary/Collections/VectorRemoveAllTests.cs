namespace gflat.Tests.Std.Collections;

public sealed class VectorRemoveAllTests
{
    private static IEnumerable<SourceFile> Sources(string program, bool managed = false)
    {
        string[] names = ["core/Collections/IEnumerable.gf", "core/Queries/Enumerable.gf",
            "core/IStringConvertable.gf", "core/IHashable.gf", "core/IEquatable.gf", "core/IComparable.gf",
            "core/Hashing/primitive_hashing.gf", "core/Text/String.gf", "core/Text/ByteSearch.gf", "core/Collections/Vector.gf", "core/Memory/Span.gf"];
        return names.Concat(managed ? ["libc/GC.gf", "libc/TracingCollector.gf"] : Array.Empty<string>())
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", name))))
            .Append(new SourceFile("vector-remove-all.gf", program));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void StableRemovalMatchesListRemoveAllWithoutAllocating(int optimization)
    {
        var random = new Random(38593);
        var oracle = new List<int>();
        for (int count = 0; count <= 35; count++)
        for (int divisor = 1; divisor <= 6; divisor++)
        {
            var original = Enumerable.Range(0, count).Select(_ => random.Next(-40, 41)).ToList();
            var expected = original.ToList();
            int removed = expected.RemoveAll(value => value % divisor == 0);
            oracle.AddRange([count, divisor, removed, expected.Count]);
            oracle.AddRange(original);
            oracle.AddRange(expected);
        }
        string program = $$"""
            using std; using std::Memory; using std::Queries; using std::Collections;
            extern void*? malloc(nuint bytes);extern void free(void* pointer);
            bool deny=false;int attempts=0;int divisor=1;int calls=0;
            namespace Memory{
                replace void*? Allocate(nuint bytes){attempts++;if(deny)return null;return malloc(bytes);}
                replace void Free(void* pointer) nothrow{free(pointer);}
            }
            bool Matches(readonly(int)* value){calls++;return *value%divisor==0;}
            int main(){int[{{oracle.Count}}] oracle=[{{string.Join(',', oracle)}}];nuint cursor=0;
                while(cursor<{{oracle.Count}}){
                    int count=oracle[cursor++];divisor=oracle[cursor++];nuint removed=(nuint)oracle[cursor++];nuint expected=(nuint)oracle[cursor++];
                    Vector<int> values=new();values.Reserve((nuint)count+3);
                    for(int i=0;i<count;i++)values.Add(oracle[cursor++]);
                    nuint capacity=values.Capacity;int before=attempts;calls=0;deny=true;
                    nuint actual=values.RemoveAll(Matches);deny=false;
                    if(actual!=removed || values.Count!=expected || values.Capacity!=capacity || attempts!=before || calls!=count)return 1;
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
    public void NoncopyableOwnersStayInitializedDuringPredicatesAndAreDestroyedExactlyOnce(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std; using std::Memory; using std::Queries; using std::Collections;
            int dead=0;int calls=0;int next=0;bool bad=false;Vector<Owner>*? watched=null;
            struct Owner{public int* Data;public Owner(int id){Data=new* int(id);}~Owner(){dead++;delete Data;}}
            bool Match(immutable(Owner)* value){
                if(*value.Data!=next || dead!=0)bad=true;next++;calls++;
                // Readonly aliases observe initialized owners throughout partitioning.
                Span<immutable(Owner)> live=watched!.AsImmutable();
                int sum=0;for(nuint i=0;i<live.Length;i++)sum+=*live[i].Data;
                if(sum!=45)bad=true;
                return *value.Data%3==1;
            }
            bool NeverCalled(immutable(Owner)* value){throw new Exception("empty predicate called");}
            int main(){
                {
                    Vector<Owner> values=new();values.Reserve(16);
                    for(int i=0;i<10;i++)values.Add(new Owner(i));
                    watched=&values;
                    if(values.RemoveAll(Match)!=3 || bad || calls!=10 || dead!=3 || values.Count!=7)return 1;
                    int expected=0;for(nuint i=0;i<values.Count;i++){while(expected%3==1)expected++;if(*values[i].Data!=expected)return 2;expected++;}
                    values.Clear();if(dead!=10)return 3;
                    if(values.RemoveAll(NeverCalled)!=0 || values.Capacity!=16)return 4;
                }
                return dead-10;
            }
            """), optimization);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EachThrowPositionRemovesOnlyPriorMatchesAndPreservesFailingAndUnvisitedOwners(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std; using std::Memory; using std::Queries; using std::Collections;
            class PredicateFailure:Exception{public int Index;public PredicateFailure(int index):base("predicate failed",77){Index=index;}}
            int failAt=0;int calls=0;int dead=0;
            struct Owner{public int* Data;public Owner(int id){Data=new* int(id);}~Owner(){dead++;delete Data;}}
            bool Match(immutable(Owner)* value){int index=calls++;if(index==failAt)throw new PredicateFailure(index);return *value.Data%3==1;}
            int main(){for(int failure=0;failure<10;failure++){
                    dead=0;calls=0;failAt=failure;
                    {
                        Vector<Owner> values=new();values.Reserve(20);for(int i=0;i<10;i++)values.Add(new Owner(i));
                        Owner* first=&values[0];int caught=0;
                        try{values.RemoveAll(Match);}catch(PredicateFailure* error){if(error.Index!=failure || error.GetCode()!=77)return 1;caught++;}
                        if(caught!=1 || calls!=failure+1 || values.Capacity!=20 || &values[0]!=first)return 2;
                        nuint kept=0;int removed=0;
                        for(int i=0;i<10;i++){
                            if(i<failure && i%3==1)removed++;
                            else{if(*values[kept].Data!=i)return 3;kept++;}
                        }
                        if(values.Count!=kept || dead!=removed)return 4;
                        values.Add(new Owner(10));if(*values.Pop().Data!=10)return 5;
                    }
                    if(dead!=11)return 6;
                }
                return 0;
            }
            """), optimization);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PredicateAndDestructorCollectionsKeepEveryLiveManagedOwnerRooted(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std; using std::Memory; using std::Queries; using std::Collections;
            class Node{public int Id;public Node^? Next;public Node(int id){Id=id;}}
            class PredicateFailure:Exception{public PredicateFailure():base("predicate failed",77){}}
            int dead=0;int calls=0;int failAt=-1;int expectedLive=12;bool bad=false;
            struct Owner{
                public Node^ Root;
                public Owner(int id){Root=new^ Node(id);Root.Next=Root;}
                ~Owner(){GC::Collect();if(TracingCollector::ManagedCount!=(nuint)expectedLive)bad=true;expectedLive--;dead++;}
            }
            bool Match(immutable(Owner)* owner){
                GC::Collect();if(TracingCollector::ManagedCount!=12 || dead!=0 || owner.Root.Id!=calls)bad=true;
                int index=calls++;if(index==failAt)throw new PredicateFailure();return owner.Root.Id%2==1;
            }
            bool Scenario(int failure){
                dead=0;calls=0;failAt=failure;expectedLive=12;
                {
                    Vector<Owner> values=new();for(int i=0;i<12;i++)values.Add(new Owner(i));
                    if(failure<0){if(values.RemoveAll(Match)!=6)return false;}
                    else{bool caught=false;try{values.RemoveAll(Match);}catch(PredicateFailure* error){caught=true;}if(!caught)return false;}
                    GC::Collect();if(TracingCollector::ManagedCount!=(nuint)expectedLive || bad)return false;
                    nuint kept=0;for(int i=0;i<12;i++)if(i%2==0 || failure>=0 && i>=failure){if(values[kept].Root.Id!=i)return false;kept++;}
                    if(values.Count!=kept)return false;
                }
                GC::Collect();return dead==12 && expectedLive==0 && TracingCollector::ManagedCount==0 && !bad;
            }
            int main(){if(!Scenario(-1))return 1;if(!Scenario(7))return 2;return 0;}
            """, managed: true), optimization);
        Assert.Equal(0, result.ExitCode);
    }
}
