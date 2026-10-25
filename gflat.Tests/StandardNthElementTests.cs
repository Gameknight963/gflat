namespace gflat.Tests;

public sealed class StandardNthElementTests
{
    private static IEnumerable<SourceFile> Sources(string code)
        => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path)))
            .Append(new SourceFile("nth-element.gf",code));

    private static void Run(string code,int optimization)
    {
        var result=CompilerTestHelper.Run(Sources(code),optimization);
        Assert.True(result.ExitCode==0,$"Exit {result.ExitCode}: {result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SortedRankPartitionsPermutationAndGuardsMatchHostOracle(int optimization)
    {
        var random=new Random(274193);
        var cases=new List<(int[] Values,int Rank,bool Descending)>();
        for(int length=0;length<=16;length++)
        for(int rank=0;rank<=length;rank++)
            cases.Add((Enumerable.Range(0,length).Select(_=>random.Next(32)).ToArray(),rank,rank%2!=0));
        foreach(int length in new[]{31,32,33,64,129})
        for(int mode=0;mode<5;mode++)
        {
            int[] values=Enumerable.Range(0,length).Select(i=>mode switch {
                0=>i%32,1=>(length-1-i)%32,2=>7,3=>i%2,_=>random.Next(32) }).ToArray();
            foreach(int rank in new[]{0,length/2,length-1,length})cases.Add((values,rank,mode==1));
        }
        using var packet=new MemoryStream();
        foreach(var sample in cases)
        {
            packet.WriteByte((byte)sample.Values.Length);packet.WriteByte((byte)sample.Rank);
            packet.WriteByte(sample.Descending?(byte)1:(byte)0);
            int[] sorted=sample.Descending?sample.Values.OrderDescending().ToArray():sample.Values.Order().ToArray();
            packet.WriteByte(sample.Rank<sorted.Length?(byte)sorted[sample.Rank]:(byte)0);
            foreach(int value in sample.Values)packet.WriteByte((byte)value);
        }
        string hex=Convert.ToHexString(packet.ToArray());
        Run(Memory+$$$"""
            int calls=0;
            int Ascending(readonly(int)* left,readonly(int)* right){calls++;if(*left<*right)return -7;if(*left>*right)return 11;return 0;}
            int Descending(readonly(int)* left,readonly(int)* right){calls++;if(*left>*right)return -7;if(*left<*right)return 11;return 0;}
            int Never(readonly(int)* left,readonly(int)* right){calls++;throw new Exception("unexpected comparison");}
            nuint ReadByte(readonly(char)* text,ref nuint offset){
                nuint value=0;for(int i=0;i<2;i++){
                    char digit=text[offset];offset++;nuint part=(nuint)((int)digit-(int)'0');
                    if(digit>='A')part=(nuint)((int)digit-(int)'A'+10);value=value*16+part;
                }return value;
            }
            int main(){
                readonly(char)* packet="{{{hex}}}";nuint offset=0;
                int[131] storage;int[131] saved;int[32] beforeCounts;int[32] afterCounts;
                for(int row=0;row<{{{cases.Count}}};row++){
                    nuint length=ReadByte(packet,ref offset);nuint rank=ReadByte(packet,ref offset);
                    bool descending=ReadByte(packet,ref offset)!=0;int expected=(int)ReadByte(packet,ref offset);
                    for(nuint i=0;i<131;i++)storage[i]=-91;
                    for(nuint i=0;i<32;i++){beforeCounts[i]=0;afterCounts[i]=0;}
                    for(nuint i=0;i<length;i++){storage[i+1]=(int)ReadByte(packet,ref offset);beforeCounts[(nuint)storage[i+1]]++;}
                    for(nuint i=0;i<131;i++)saved[i]=storage[i];
                    Span<int> values=new(&storage[1],length);
                    int(readonly(int)*,readonly(int)*)* compare=Ascending;if(descending)compare=Descending;
                    int oldCalls=calls;int oldAttempts=attempts;deny=true;
                    NthElement<int>(values,rank,compare);deny=false;
                    if(attempts!=oldAttempts)return 1;
                    for(nuint i=0;i<131;i++)if((i==0||i>length)&&storage[i]!=saved[i])return 2;
                    for(nuint i=0;i<length;i++){
                        if(values[i]<0||values[i]>=32)return 3;afterCounts[(nuint)values[i]]++;
                        if(rank==length&&values[i]!=saved[i+1])return 4;
                    }
                    for(nuint i=0;i<32;i++)if(beforeCounts[i]!=afterCounts[i])return 5;
                    if(rank==length){if(calls!=oldCalls)return 6;}
                    else{
                        if(values[rank]!=expected)return 7;
                        for(nuint i=0;i<rank;i++)if(compare(&values[i],&values[rank])>0)return 8;
                        for(nuint i=rank+1;i<length;i++)if(compare(&values[rank],&values[i])>0)return 9;
                    }
                }
                if(offset!={{{hex.Length}}})return 10;
                int minimum=-2147483647-1;int maximum=2147483647;
                int[5] signed=[maximum,-7,0,2,minimum];Span<int> signedValues=new(signed,5);
                NthElement<int>(signedValues,2,Ascending);
                if(signedValues[2]!=0)return 14;
                for(nuint i=0;i<2;i++)if(signedValues[i]>0)return 15;
                for(nuint i=3;i<5;i++)if(signedValues[i]<0)return 16;
                Span<int> empty=new();int oldCalls=calls;NthElement<int>(empty,0,Never);
                Span<int> unread=new((int*)4096,16);NthElement<int>(unread,16,Never);
                bool caught=false;try{NthElement<int>(unread,17,Never);}catch(ref Exception error){if(error.GetCode()!=3)return 11;caught=true;}
                if(!caught||calls!=oldCalls)return 12;
                int[1] one=[7];Span<int> singleton=new(one,1);NthElement<int>(singleton,0,Never);
                if(one[0]!=7||calls!=oldCalls||allocated!=released)return 13;
                return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OrganPipeAdversaryHasBoundedComparisonsAndExactPermutation(int optimization)
        => Run(Memory+"""
            int calls=0;
            int Compare(readonly(int)* left,readonly(int)* right){calls++;if(*left<*right)return -7;if(*left>*right)return 11;return 0;}
            int main(){
                int[1026] storage;storage[0]=-91;storage[1025]=-92;
                for(nuint i=0;i<1024;i++){
                    if(i<512)storage[i+1]=(int)(i*2);
                    else storage[i+1]=1023-(int)((i-512)*2);
                }
                Span<int> values=new(&storage[1],1024);int oldAttempts=attempts;deny=true;
                NthElement<int>(values,512,Compare);deny=false;
                // Conservative partition-budget plus heapsort comparison ceiling.
                if(calls>8*1024*10||attempts!=oldAttempts||values[512]!=512)return 1;
                bool[1024] seen;for(nuint i=0;i<1024;i++)seen[i]=false;
                for(nuint i=0;i<1024;i++){
                    int value=values[i];if(value<0||value>=1024||seen[(nuint)value])return 2;seen[(nuint)value]=true;
                    if(i<512&&value>512)return 3;if(i>512&&value<512)return 4;
                }
                if(storage[0]!=-91||storage[1025]!=-92)return 5;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryComparatorFailurePreservesInitializedNoncopyableOwners(int optimization)
        => Run(Memory+"""
            int dead=0;int calls=0;int throwAt=0;bool bad=false;Vector<Owner>*? watched=null;
            struct Owner{
                public int Id;public int* Resource;
                public Owner(int id){Id=id;Resource=new* int(id);}
                ~Owner(){dead++;delete Resource;}
            }
            int Compare(immutable(Owner)* left,immutable(Owner)* right){
                Span<immutable(Owner)> all=watched!.AsImmutable();bool[9] seen;
                for(nuint i=0;i<9;i++)seen[i]=false;
                for(nuint i=0;i<all.Length;i++){
                    int id=all[i].Id;
                    if(id<0||id>=9)bad=true;
                    else{if(seen[(nuint)id]||*all[i].Resource!=id)bad=true;seen[(nuint)id]=true;}
                }
                calls++;if(calls==throwAt)throw new Exception("compare failed",37);
                int a=left.Id%4;int b=right.Id%4;if(a<b)return -1;if(a>b)return 1;return 0;
            }
            void Fill(ref Vector<Owner> values){for(int i=0;i<9;i++)values.Add(new Owner((i*5)%9));}
            bool Check(immutable(Vector<Owner>)* values){
                bool[9] seen;for(nuint i=0;i<9;i++)seen[i]=false;
                for(nuint i=0;i<values.Count;i++){
                    int id=(*values)[i].Id;
                    if(id<0||id>=9)return false;if(seen[(nuint)id]||*(*values)[i].Resource!=id)return false;seen[(nuint)id]=true;
                }
                return values.Count==9;
            }
            int main(){
                int baseline=0;
                {
                    Vector<Owner> values=new();Fill(ref values);watched=&values;
                    int oldAttempts=attempts;deny=true;NthElement<Owner>(values.AsSpan(),4,Compare);deny=false;
                    baseline=calls;if(bad||dead!=0||!Check(&values)||attempts!=oldAttempts||values[4].Id%4!=1)return 1;
                }
                if(dead!=9||allocated!=released)return 2;
                for(int point=1;point<=baseline;point++){
                    int oldDead=dead;
                    {
                        Vector<Owner> values=new();Fill(ref values);watched=&values;calls=0;throwAt=point;bool caught=false;
                        try{NthElement<Owner>(values.AsSpan(),4,Compare);}
                        catch(ref Exception error){if(error.GetCode()!=37)return 3;caught=true;}
                        throwAt=0;if(!caught||bad||dead!=oldDead||!Check(&values))return 4;
                    }
                    if(dead!=oldDead+9||allocated!=released)return 5;
                }
                return 0;
            }
            """,optimization);

    private const string Memory="""
        using std; using std::Collections;
        int attempts=0;int allocated=0;int released=0;bool deny=false;
        extern void*? malloc(nuint count);extern void free(void* value);
        namespace Memory{
            public replace void*? Allocate(nuint count){attempts++;if(deny)return null;void*? value=malloc(count);if(value!=null)allocated++;return value;}
            public replace void Free(void* value)nothrow{released++;free(value);}
        }
        """;
}
