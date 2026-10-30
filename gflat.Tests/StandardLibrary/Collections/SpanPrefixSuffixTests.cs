namespace gflat.Tests.Std.Collections;

public sealed class SpanPrefixSuffixTests
{
    private const string Allocator = """
        using Standard; using Standard::Text; using Standard::Memory; using Standard::Collections;
        extern void*? malloc(nuint bytes);extern void free(void* storage);
        int attempts=0;bool deny=false;
        namespace Memory{
            public replace void*? Allocate(nuint bytes){attempts++;if(deny)return null;return malloc(bytes);}
            public replace void Free(void* storage) nothrow{free(storage);}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BorrowedRegionsMatchHostPrefixSuffixOracleWithoutAllocation(int optimization)
    {
        List<(int[] Source,int[] Pattern)> cases=[([],[]),([],[1]),([1],[]),([1],[1]),([1],[2]),
            ([1,2,3],[1,2]),([1,2,3],[2,3]),([1,2],[1,2,3]),([0,0,0],[0,0]),
            ([int.MinValue,0,int.MaxValue],[int.MinValue])];
        var random=new Random(61117);
        for(int row=0;row<8;row++){
            int[] source=Enumerable.Range(0,random.Next(0,13)).Select(_=>random.Next(50)).ToArray();
            int[] pattern=(row%3) switch{
                0=>source.Take(source.Length/2).ToArray(),
                1=>source.Skip(source.Length/2).ToArray(),
                _=>Enumerable.Range(0,random.Next(0,17)).Select(_=>random.Next(50)).ToArray()
            };
            cases.Add((source,pattern));
        }
        var packet=new List<int>();
        foreach(var (source,pattern) in cases){
            bool starts=source.AsSpan().StartsWith(pattern);
            bool ends=source.AsSpan().EndsWith(pattern);
            packet.AddRange([source.Length,pattern.Length,starts?1:0,ends?1:0,
                Comparisons(source,pattern,false),Comparisons(source,pattern,true)]);
            packet.AddRange(source);packet.AddRange(pattern);
        }
        Run(Allocator+$$"""
            int calls=0;
            immutable(int)* leftOrigin=((nuint)1).reinterpret<immutable(int)*>();immutable(int)* rightOrigin=((nuint)1).reinterpret<immutable(int)*>();
            bool Equal(immutable(int)* left,immutable(int)* right){
                if(left!=leftOrigin+calls||right!=rightOrigin+calls)throw new Exception("address order",99);
                calls++;return *left==*right;
            }
            int main(){
                int[{{packet.Count}}] packet=[{{string.Join(',',packet)}}];nuint at=0;
                int[17] source;int[17] pattern;int[17] sourceSaved;int[17] patternSaved;
                int baseline=attempts;deny=true;
                for(int row=0;row<{{cases.Count}};row++){
                    nuint count=(nuint)packet[at++];nuint patternCount=(nuint)packet[at++];
                    bool prefix=packet[at++]!=0;bool suffix=packet[at++]!=0;
                    int prefixCalls=packet[at++];int suffixCalls=packet[at++];
                    for(nuint i=0;i<count;i++){source[i]=packet[at++];sourceSaved[i]=source[i];}
                    for(nuint i=0;i<patternCount;i++){pattern[i]=packet[at++];patternSaved[i]=pattern[i];}
                    Span<int> values=new(source,count);Span<int> wanted=new(pattern,patternCount);
                    leftOrigin=source;rightOrigin=pattern;calls=0;
                    if(StartsWith<int>(values.AsImmutable(),wanted.AsImmutable(),Equal)!=prefix||calls!=prefixCalls)return 1;
                    leftOrigin=source;if(patternCount<=count)leftOrigin+=count-patternCount;calls=0;
                    if(EndsWith<int>(values.AsImmutable(),wanted.AsImmutable(),Equal)!=suffix||calls!=suffixCalls)return 2;
                    if(StartsWith<int>(values.AsImmutable(),wanted.AsImmutable())!=prefix||EndsWith<int>(values.AsImmutable(),wanted.AsImmutable())!=suffix)return 3;
                    for(nuint i=0;i<count;i++)if(source[i]!=sourceSaved[i])return 4;
                    for(nuint i=0;i<patternCount;i++)if(pattern[i]!=patternSaved[i])return 5;
                }
                if(at!={{packet.Count}}||attempts!=baseline)return 6;
                deny=false;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EmptyOversizedAliasedAndNoncopyableRegionsKeepContracts(int optimization)
        =>Run(Contracts,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ExactDocumentedExampleAlsoKeepsTextExtensionCallsUnambiguous(int optimization)
    {
        string markdown=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Guides/Standard/core/Collections/ALGORITHMS.md"));
        int heading=markdown.IndexOf("## Prefix and suffix matching",StringComparison.Ordinal);
        Assert.True(heading>=0);
        int fence=markdown.IndexOf("```csharp",heading,StringComparison.Ordinal);Assert.True(fence>=0);
        int start=markdown.IndexOf('\n',fence)+1;
        int end=markdown.IndexOf("```",start,StringComparison.Ordinal);Assert.True(end>start);
        Run(markdown[start..end],optimization);
    }

    private static int Comparisons(int[] source,int[] pattern,bool suffix)
    {
        if(pattern.Length>source.Length)return 0;
        int start=suffix?source.Length-pattern.Length:0;
        for(int i=0;i<pattern.Length;i++)if(source[start+i]!=pattern[i])return i+1;
        return pattern.Length;
    }

    private static void Run(string program,int optimization)
    {
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Standard"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("span-prefix-suffix.gf",program));
        var result=CompilerTestHelper.Run(sources,optimization);
        Assert.True(result.ExitCode==0,$"Span prefix/suffix exit {result.ExitCode}: {result.StandardError}");
    }

    private const string Contracts = """
        using Standard; using Standard::Text; using Standard::Memory; using Standard::Collections;
        extern void*? malloc(nuint bytes); extern void free(void* storage);
        int attempts=0; int allocated=0; int released=0; bool deny=false;
        namespace Memory {
            public replace void*? Allocate(nuint bytes) {
                attempts++; if(deny)return null;
                void*? result=malloc(bytes);if(result!=null)allocated++;return result;
            }
            public replace void Free(void* storage) nothrow {released++;free(storage);}
        }
        int calls=0; int failAt=-1; nuint failureAddress=0;
        immutable(int)* leftOrigin=((nuint)1).reinterpret<immutable(int)*>();
        immutable(int)* rightOrigin=((nuint)1).reinterpret<immutable(int)*>();
        bool Equal(immutable(int)* left,immutable(int)* right) {
            if(left!=leftOrigin+calls||right!=rightOrigin+calls)throw new Exception("address order",99);
            int position=calls;calls++;
            if(position==failAt) {
                try{throw new Exception("original equality",77);}
                catch(ref Exception error){failureAddress=(&error).reinterpret<nuint>();throw;}
            }
            return *left==*right;
        }
        bool Unexpected(immutable(int)* left,immutable(int)* right){throw new Exception("unexpected comparison",99);}
        int made=0;int dead=0;int ownerCalls=0;
        struct Owner {
            public int Id;public String Token;
            public Owner(int id){Id=id;Token=s"owned";made++;}
            ~Owner(){dead++;}
        }
        bool SameOwner(immutable(Owner)* left,immutable(Owner)* right){ownerCalls++;return left.Id==right.Id;}
        int main() {
            int[5] ones=[1,1,1,1,1];Span<int> values=new(ones,5);
            Span<immutable(int)> empty=new(((nuint)1).reinterpret<immutable(int)*>(),0);
            int[2] live=[1,2];Span<int> small=new(live,1);Span<int> longer=new(live,2);
            int baseline=attempts;deny=true;
            if(!StartsWith<int>(empty,empty,Unexpected)||!EndsWith<int>(empty,empty,Unexpected))return 1;
            if(!StartsWith<int>(values.AsImmutable(),empty,Unexpected)||!EndsWith<int>(values.AsImmutable(),empty,Unexpected))return 2;
            if(StartsWith<int>(empty,small.AsImmutable(),Unexpected)||EndsWith<int>(empty,small.AsImmutable(),Unexpected))return 3;
            if(StartsWith<int>(small.AsImmutable(),longer.AsImmutable(),Unexpected)||EndsWith<int>(small.AsImmutable(),longer.AsImmutable(),Unexpected))return 4;
            if(!StartsWith<int>(values.AsImmutable(),empty)||!EndsWith<int>(values.AsImmutable(),empty))return 5;
            leftOrigin=ones;rightOrigin=&ones[1];calls=0;
            if(!StartsWith<int>(values.AsImmutable(),values.Slice(1,3).AsImmutable(),Equal)||calls!=3)return 6;
            leftOrigin=&ones[2];rightOrigin=&ones[1];calls=0;
            if(!EndsWith<int>(values.AsImmutable(),values.Slice(1,3).AsImmutable(),Equal)||calls!=3)return 7;
            for(nuint i=0;i<5;i++)if(ones[i]!=1)return 8;
            if(attempts!=baseline)return 9;
            deny=false;
            int[4] data=[1,2,3,4];int[2] mismatch=[1,9];
            Span<int> source=new(data,4);Span<int> pattern=new(mismatch,2);
            leftOrigin=data;rightOrigin=mismatch;calls=0;
            if(StartsWith<int>(source.AsImmutable(),pattern.AsImmutable(),Equal)||calls!=2)return 10;
            leftOrigin=&data[2];rightOrigin=mismatch;calls=0;
            if(EndsWith<int>(source.AsImmutable(),pattern.AsImmutable(),Equal)||calls!=1)return 11;
            for(int mode=0;mode<2;mode++)for(int failure=0;failure<2;failure++) {
                nuint start=0;if(mode==1)start=2;
                leftOrigin=&data[start];rightOrigin=&data[start];calls=0;failAt=failure;failureAddress=0;
                bool caught=false;
                try {
                    if(mode==0)StartsWith<int>(source.AsImmutable(),source.Slice(start,2).AsImmutable(),Equal);
                    else EndsWith<int>(source.AsImmutable(),source.Slice(start,2).AsImmutable(),Equal);
                }
                catch(ref Exception error) {
                    caught=(&error).reinterpret<nuint>()==failureAddress&&error.GetCode()==77&&CString::Equals(error.GetMessage(),"original equality");
                }
                if(!caught||calls!=failure+1)return 12;
                for(nuint i=0;i<4;i++)if(data[i]!=(int)i+1)return 13;
            }
            failAt=-1;
            {
                Vector<Owner> owners=new();owners.Reserve(4);
                for(int id=0;id<4;id++)owners.Add(new Owner(id));
                nuint[4] tokens;for(nuint i=0;i<4;i++)tokens[i]=(owners[i].Token.Data).reinterpret<nuint>();
                baseline=attempts;deny=true;ownerCalls=0;
                if(!StartsWith<Owner>(owners.AsImmutable(),owners.AsImmutable().Slice(0,2),SameOwner)||ownerCalls!=2)return 14;
                ownerCalls=0;
                if(!EndsWith<Owner>(owners.AsImmutable(),owners.AsImmutable().Slice(2,2),SameOwner)||ownerCalls!=2)return 15;
                if(attempts!=baseline||made!=4||dead!=0||owners.Count!=4)return 16;
                for(nuint i=0;i<4;i++)if(owners[i].Id!=(int)i||(owners[i].Token.Data).reinterpret<nuint>()!=tokens[i])return 17;
                deny=false;
            }
            if(made!=4||dead!=4||allocated!=released)return 18;
            return 0;
        }
        """;
}
