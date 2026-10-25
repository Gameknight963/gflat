namespace gflat.Tests;

public sealed class StandardQueryAggregateTransferTests
{
    private static IEnumerable<SourceFile> Sources(string program)
        => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("aggregate-transfer.gf", program));

    private static void Run(string program, int optimization)
    {
        var result = CompilerTestHelper.Run(Sources(program), optimization);
        Assert.True(result.ExitCode == 0,
            $"Aggregate transfer exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    private const string Counters = """
        using std; using std::Queries;
        extern void*? malloc(nuint bytes);extern void free(void* storage);
        int allocated=0;int released=0;
        namespace Memory {
            public replace void*? Allocate(nuint bytes){void*? p=malloc(bytes);if(p!=null)allocated++;return p;}
            public replace void Free(void* storage) nothrow{released++;free(storage);}
        }
        """;

    private const string UniqueProgram = """
        using std; using std::Queries;
        Unique<int> Add(Unique<int> accumulator,int value){accumulator.Value+=value;return move accumulator;}
        int main(){
            int[4] input=[1,2,3,4];Span<int> values=new(input,4);
            Unique<int> initial=new(new* int(10));int* address=&initial.Value;
            {
                Unique<int> result=values.AsEnumerable().Aggregate(move initial,Add);
                if(result.Value!=20||&result.Value!=address)return 1;
            }
            Unique<int> emptySeed=new(new* int(37));address=&emptySeed.Value;
            Unique<int> empty=values.AsEnumerable().Take(0).Aggregate(move emptySeed,Add);
            if(empty.Value!=37||&empty.Value!=address)return 2;
            return 0;
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NoncopyableUniqueAccumulatorKeepsIdentityAndEmptySeed(int optimization)
        => Run(UniqueProgram,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PreallocatedStringAccumulatorNeedsNoPerElementDeepCopy(int optimization)
        => Run(Counters+"""
            String Append(String seed,int value){seed.Append((char)('a'+value));return move seed;}
            int main(){
                {
                    int[5] input=[0,1,2,3,4];Span<int> values=new(input,5);
                    String seed=s"start:";seed.Reserve(128);
                    readonly(char)* address=seed.Data;int before=allocated;
                    String result=values.AsEnumerable().Aggregate(move seed,Append);
                    if(result.Data!=address||allocated!=before||result.Length!=11)return 1;
                    if(result[6]!='a'||result[10]!='e')return 2;
                }
                if(allocated!=released)return 3;
                {
                    int[1] input=[0];Span<int> values=new(input,1);
                    String seed=s"caller";readonly(char)* address=seed.Data;
                    {
                        String empty=values.AsEnumerable().Take(0).Aggregate(seed,Append);
                        if(empty.Data==address||empty.Length!=6||seed.Data!=address||seed.Length!=6)return 4;
                        empty[0]='X';if(seed[0]!='c')return 5;
                    }
                    String folded=values.AsEnumerable().Aggregate(seed,Append);
                    if(folded.Data==address||folded.Length!=7||folded[6]!='a')return 6;
                    if(seed.Data!=address||seed.Length!=6||seed[0]!='c')return 7;
                }
                if(allocated!=released)return 8;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FailureBeforeAndAfterCallbackEntryCleansAccumulatorAndIteratorExactlyOnce(int optimization)
        => Run(Counters+"""
            int mode=0;int owners=0;int ownerDestroyed=0;int iteratorCreated=0;int iteratorDestroyed=0;int calls=0;
            struct Owner {
                private String token;public int Sum;
                public Owner(){token=s"owner";Sum=0;owners++;}
                ~Owner(){owners--;ownerDestroyed++;}
            }
            struct Source {
                public struct Iterator {
                    private String token;private int position;
                    public Iterator(){token=s"cursor";position=-1;iteratorCreated++;}
                    public bool MoveNext(){position++;if(mode==1&&position==1)throw new Exception("MoveNext");return position<3;}
                    public int Current {get {if(mode==2&&position==1)throw new Exception("Current");return position+1;}}
                    ~Iterator(){iteratorDestroyed++;}
                }
                public Iterator GetEnumerator(){if(mode==0)throw new Exception("GetEnumerator");return new Iterator();}
            }
            Owner Fold(Owner seed,int value){calls++;if(mode==3&&calls==2)throw new Exception("callback");seed.Sum+=value;return move seed;}
            int main(){
                for(mode=0;mode<4;mode++){
                    calls=0;int dead=ownerDestroyed;int created=iteratorCreated;int iterDead=iteratorDestroyed;
                    bool caught=false;
                    try{
                        var query=new Enumerable<int,Source,Source::Iterator>(new Source());
                        query.Aggregate(new Owner(),Fold);
                    }catch(Exception* error){caught=true;}
                    if(!caught||owners!=0||ownerDestroyed!=dead+1||allocated!=released)return 1+mode;
                    if(mode==0){if(calls!=0||iteratorCreated!=created||iteratorDestroyed!=iterDead)return 5;}
                    else{
                        if(iteratorCreated!=created+1||iteratorDestroyed!=iterDead+1)return 6;
                        if(mode==3){if(calls!=2)return 7;}else if(calls!=1)return 8;
                    }
                }
                mode=4;calls=0;
                {
                    var query=new Enumerable<int,Source,Source::Iterator>(new Source());
                    int dead=ownerDestroyed;
                    query.Aggregate(new Owner(),Fold);
                    if(owners!=0||ownerDestroyed!=dead+1||calls!=3||allocated!=released)return 9;
                }
                if(iteratorCreated!=iteratorDestroyed)return 10;return 0;
            }
            """,optimization);
}
