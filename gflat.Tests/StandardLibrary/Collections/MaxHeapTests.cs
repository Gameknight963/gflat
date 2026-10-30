using System.Text;

namespace gflat.Tests.Std.Collections;

public sealed class MaxHeapTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BuildPushAndPopMatchIndependentSortedOraclesForBothComparatorDirections(int optimization)
    {
        var random = new Random(681482);
        var inputs = new List<int[]> { Array.Empty<int>(), new[] { int.MinValue }, new[] { int.MaxValue },
            new[] { int.MinValue, int.MaxValue, 0, int.MinValue, -1, int.MaxValue, 1 } };
        for (int count = 0; count <= 32; count++)
        {
            inputs.Add(Enumerable.Range(0, count).ToArray());
            inputs.Add(Enumerable.Range(0, count).Select(index => count - index).ToArray());
            inputs.Add(Enumerable.Range(0, count).Select(_ => random.Next(-9, 10)).ToArray());
        }
        inputs.Add(Enumerable.Range(0, 64).Select(_ => (int)random.NextInt64(int.MinValue, (long)int.MaxValue + 1)).ToArray());
        using var packet = new MemoryStream();
        int rows = 0;
        using (var writer = new BinaryWriter(packet, Encoding.UTF8, true))
        foreach (bool reverse in new[] { false, true })
        foreach (int[] values in inputs)
        {
            var comparer = reverse ? Comparer<int>.Create((left, right) => right.CompareTo(left)) : Comparer<int>.Default;
            writer.Write((byte)(reverse ? 1 : 0));writer.Write((byte)values.Length);
            foreach (int value in values) writer.Write(value);
            for (int prefix = 1; prefix <= values.Length; prefix++)
                writer.Write(values.Take(prefix).OrderByDescending(value => value, comparer).First());
            foreach (int value in values.OrderByDescending(value => value, comparer)) writer.Write(value);
            rows++;
        }
        string hex = Convert.ToHexString(packet.ToArray());
        Run(Allocator + $$"""
            bool reversed=false;int calls=0;
            int Compare(immutable(int)* left,immutable(int)* right){
                calls++;int result=0;if(*left<*right)result=-1;else if(*left>*right)result=1;
                if(reversed)return -result;return result;
            }
            int Digit(char value)nothrow{if(value<='9')return value-'0';return value-'A'+10;}
            byte ReadByte(readonly(char)* text,ref nuint at)nothrow{
                byte value=(byte)((Digit(text[at])<<4)|Digit(text[at+1]));at+=2;return value;
            }
            int ReadInt(readonly(char)* text,ref nuint at)nothrow{
                uint value=0u;for(int index=0;index<4;index++)value=value|((uint)ReadByte(text,ref at)<<(index*8));
                return (int)value;
            }
            bool Drain(Span<int> values,immutable(int)* expected){
                nuint end=values.Length;
                for(nuint index=0;index<values.Length;index++){
                    PopMaxHeap<int>(values.Slice(0,end),Compare);
                    if(values[end-1]!=expected[index])return false;
                    end--;if(!IsMaxHeap<int>(values.Slice(0,end).AsImmutable(),Compare))return false;
                }
                return true;
            }
            int main(){
                readonly(char)* records="{{hex}}";nuint at=0;
                for(int row=0;row<{{rows}};row++){
                    reversed=ReadByte(records,ref at)!=0;
                    nuint count=(nuint)ReadByte(records,ref at);
                    int[64] input;int[64] prefix;int[64] expected;
                    for(nuint index=0;index<count;index++)input[index]=ReadInt(records,ref at);
                    for(nuint index=0;index<count;index++)prefix[index]=ReadInt(records,ref at);
                    for(nuint index=0;index<count;index++)expected[index]=ReadInt(records,ref at);
                    int[66] data;for(nuint index=0;index<66;index++)data[index]=31415926;
                    for(nuint index=0;index<count;index++)data[index+1]=input[index];
                    Span<int> all=new(data,66);Span<int> work=all.Slice(1,count);
                    calls=0;int before=attempts;deny=true;
                    MakeMaxHeap<int>(work,Compare);
                    if(!IsMaxHeap<int>(work.AsImmutable(),Compare))return 1;
                    if(count<2&&calls!=0)return 2;
                    if(count!=0&&work[0]!=prefix[count-1])return 3;
                    if(!Drain(work,expected))return 4;
                    for(nuint index=0;index<count;index++)data[index+1]=input[index];
                    calls=0;
                    for(nuint end=1;end<=count;end++){
                        Span<int> heap=work.Slice(0,end);PushMaxHeap<int>(heap,Compare);
                        if(!IsMaxHeap<int>(heap.AsImmutable(),Compare)||heap[0]!=prefix[end-1])return 5;
                        if(end==1&&calls!=0)return 6;
                    }
                    if(!Drain(work,expected)||attempts!=before)return 7;deny=false;
                    if(work.Length!=count||all.Length!=66||data[0]!=31415926)return 8;
                    for(nuint index=count+1;index<66;index++)if(data[index]!=31415926)return 9;
                }
                reversed=false;int[2] invalid=[1,2];Span<int> invalidView=new(invalid,2);
                if(IsMaxHeap<int>(invalidView.AsImmutable(),Compare)||invalid[0]!=1||invalid[1]!=2)return 10;
                reversed=true;invalid[0]=2;invalid[1]=1;
                if(IsMaxHeap<int>(invalidView.AsImmutable(),Compare)||invalid[0]!=2||invalid[1]!=1)return 11;
                reversed=false;int[31] deep;
                for(nuint index=0;index<31;index++)deep[index]=31-(int)index;
                Span<int> deepView=new(deep,31);int invalidBefore=attempts;deny=true;
                calls=0;if(!IsMaxHeap<int>(deepView.AsImmutable(),Compare)||calls!=30)return 15;
                for(nuint child=1;child<31;child++){
                    int saved=deep[child];deep[child]=1000;calls=0;
                    if(IsMaxHeap<int>(deepView.AsImmutable(),Compare))return 16;
                    if(calls!=(int)child)return 17;
                    for(nuint index=0;index<31;index++){
                        int expectedValue=31-(int)index;if(index==child)expectedValue=1000;
                        if(deep[index]!=expectedValue)return 18;
                    }
                    deep[child]=saved;
                }
                if(attempts!=invalidBefore)return 19;deny=false;
                Span<int> empty=new();calls=0;int emptyBefore=attempts;deny=true;
                MakeMaxHeap<int>(empty,Compare);PushMaxHeap<int>(empty,Compare);PopMaxHeap<int>(empty,Compare);
                if(!IsMaxHeap<int>(empty.AsImmutable(),Compare)||calls!=0||attempts!=emptyBefore)return 12;deny=false;
                int[1] one=[73];Span<int> singleton=new(one,1);calls=0;int singleBefore=attempts;deny=true;
                MakeMaxHeap<int>(singleton,Compare);PushMaxHeap<int>(singleton,Compare);PopMaxHeap<int>(singleton,Compare);
                if(!IsMaxHeap<int>(singleton.AsImmutable(),Compare)||calls!=0||one[0]!=73||attempts!=singleBefore)return 13;deny=false;
                if(at!={{hex.Length}}||allocated!=released)return 14;return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NoncopyableOwnersSurviveEveryComparisonFailureWithoutLostOrDuplicatedResources(int optimization)
        => Run(Allocator + """
            int made=0;int dead=0;int calls=0;int failAt=-1;
            struct Owner{
                public int Id;public String Token;
                public Owner(int id){Id=id;Token=s"owner";made++;}
                ~Owner(){dead++;}
            }
            int Compare(immutable(Owner)* left,immutable(Owner)* right){
                int call=calls;calls++;if(call==failAt)throw new Exception("original heap comparator",77);
                if(left.Id<right.Id)return -1;if(left.Id>right.Id)return 1;return 0;
            }
            void Fill(Vector<Owner>* values){
                values.Reserve(33);values.Add(new Owner(31));
                for(int id=0;id<31;id++)values.Add(new Owner(id));values.Add(new Owner(32));
            }
            void Prepare(int operation,Span<Owner> values){
                if(operation==1)MakeMaxHeap<Owner>(values.Slice(0,30),Compare);
                else if(operation==2||operation==3)MakeMaxHeap<Owner>(values,Compare);
            }
            bool Apply(int operation,Span<Owner> values){
                if(operation==0){MakeMaxHeap<Owner>(values,Compare);return true;}
                if(operation==1){PushMaxHeap<Owner>(values,Compare);return true;}
                if(operation==2){PopMaxHeap<Owner>(values,Compare);return true;}
                return IsMaxHeap<Owner>(values.AsImmutable(),Compare);
            }
            int main(){
                for(int operation=0;operation<4;operation++){
                    int comparisons=0;
                    {
                        Vector<Owner> values=new();Fill(&values);Span<Owner> work=values.AsSpan().Slice(1,31);
                        nuint[33] identities;for(nuint index=0;index<33;index++)identities[(nuint)values[index].Id]=(values[index].Token.Data).reinterpret<nuint>();
                        Prepare(operation,work);calls=0;failAt=-1;
                        int before=attempts;deny=true;bool success=Apply(operation,work);comparisons=calls;deny=false;
                        if(!success||comparisons==0||attempts!=before)return 1;
                        if(operation==2){if(work[30].Id!=30||!IsMaxHeap<Owner>(work.Slice(0,30).AsImmutable(),Compare))return 2;}
                        else if(!IsMaxHeap<Owner>(work.AsImmutable(),Compare)||work[0].Id!=30)return 3;
                        if(values.Count!=33||values[0].Id!=31||values[32].Id!=32)return 4;
                        ulong live=0ul;
                        for(nuint index=0;index<33;index++){
                            int id=values[index].Id;if(id<0||id>=33||(live&(1ul<<id))!=0ul)return 13;
                            live=live|(1ul<<id);
                            if((values[index].Token.Data).reinterpret<nuint>()!=identities[(nuint)id]||values[index].Token.Length!=5)return 14;
                        }
                        if(live!=8589934591ul)return 15;
                    }
                    if(made!=dead||allocated!=released)return 5;
                    for(int failure=0;failure<comparisons;failure++){
                        {
                            int deadBefore=dead;Vector<Owner> values=new();Fill(&values);
                            Span<Owner> work=values.AsSpan().Slice(1,31);Prepare(operation,work);
                            nuint[33] tokens;int[33] positions;
                            for(nuint index=0;index<33;index++){
                                int id=values[index].Id;tokens[(nuint)id]=(values[index].Token.Data).reinterpret<nuint>();positions[index]=id;
                            }
                            nuint capacity=values.Capacity;calls=0;failAt=failure;bool caught=false;
                            try{Apply(operation,work);}
                            catch(ref Exception error){caught=error.GetCode()==77&&CString::Equals(error.GetMessage(),"original heap comparator");}
                            failAt=-1;
                            if(!caught||calls!=failure+1||values.Count!=33||values.Capacity!=capacity)return 6;
                            if(dead!=deadBefore||made-dead!=33||values[0].Id!=31||values[32].Id!=32)return 7;
                            ulong seen=0ul;
                            for(nuint index=0;index<33;index++){
                                int id=values[index].Id;if(id<0||id>=33||(seen&(1ul<<id))!=0ul)return 8;
                                seen=seen|(1ul<<id);
                                if((values[index].Token.Data).reinterpret<nuint>()!=tokens[(nuint)id]||values[index].Token.Length!=5)return 9;
                                if(operation==3&&id!=positions[index])return 10;
                            }
                            if(seen!=8589934591ul)return 11;
                        }
                        if(made!=dead||allocated!=released)return 12;
                    }
                }
                return 0;
            }
            """, optimization);

    private const string Allocator = """
        using std;using std::Text;using std::Memory;using std::Collections;
        extern void*? malloc(nuint count);extern void free(void* storage);
        int allocated=0;int released=0;int attempts=0;bool deny=false;
        namespace Memory{
            public replace void*? Allocate(nuint count){
                attempts++;if(deny)return null;void*? storage=malloc(count);if(storage!=null)allocated++;return storage;
            }
            public replace void Free(void* storage)nothrow{released++;free(storage);}
        }
        """;

    private static void Run(string program,int optimization)
    {
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path)))
            .Append(new SourceFile("max-heap.gf",program));
        string ir=Compiler.Emit(sources,options:new(){InternalizeFunctions=true});
        _=CompilerTestHelper.RunIr(ir,optimization,expectedExitCode:0);
    }
}
