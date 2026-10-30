using System.Text;

namespace gflat.Tests;

public sealed class StandardDefaultOrderingTests
{
    private const string Allocator="""
        using std; using std::Text; using std::Memory; using std::Collections;extern void*? malloc(nuint count);extern void free(void* storage);
        int attempts=0;int allocated=0;int released=0;bool deny=false;
        namespace Memory{
            public replace void*? Allocate(nuint count){attempts++;if(deny)return null;void*? result=malloc(count);if(result!=null)allocated++;return result;}
            public replace void Free(void* storage)nothrow{released++;free(storage);}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PrimitiveAndFloatingDefaultsMatchIndependentOraclesAndKeepExplicitComparersAvailable(int optimization)
    {
        List<int[]> inputs=[[],[0],[3,1,2,1],[int.MinValue,0,int.MaxValue,-1,1]];
        var random=new Random(815937);for(int i=0;i<24;i++)inputs.Add(Enumerable.Range(0,random.Next(0,34)).Select(_=>random.Next(-20,21)).ToArray());
        using var bytes=new MemoryStream();using(var writer=new BinaryWriter(bytes,Encoding.UTF8,true))foreach(var input in inputs){
            writer.Write((byte)input.Length);foreach(int value in input)writer.Write(value);
            foreach(int value in input.Order())writer.Write(value);foreach(int value in input.OrderDescending())writer.Write(value);
        }
        string hex=Convert.ToHexString(bytes.ToArray());
        ulong[] doubles=[0x7ff8000000000001UL,0xfff8000000000002UL,0x8000000000000000UL,0UL,0xfff0000000000000UL,0x7ff0000000000000UL,
            unchecked((ulong)BitConverter.DoubleToInt64Bits(3.5)),unchecked((ulong)BitConverter.DoubleToInt64Bits(-2.25)),0UL];
        ulong[] orderedDoubles=doubles.OrderBy(bits=>BitConverter.Int64BitsToDouble(unchecked((long)bits))).ToArray();
        ulong[] singles=[0x7fc00001,0xffc00002,0x80000000,0,0xff800000,0x7f800000,
            unchecked((uint)BitConverter.SingleToInt32Bits(3.5f)),unchecked((uint)BitConverter.SingleToInt32Bits(-2.25f)),0];
        ulong[] orderedSingles=singles.OrderBy(bits=>BitConverter.Int32BitsToSingle(unchecked((int)(uint)bits))).ToArray();
        string floating=FloatingBlock("double","ulong","ul",doubles,orderedDoubles,10)+FloatingBlock("float","uint","u",singles,orderedSingles,20);
        Run(Allocator+$$$"""
            int Descending(immutable(int)* first,immutable(int)* second){if(*first>*second)return -1;if(*first<*second)return 1;return 0;}
            int Digit(char value)nothrow{if(value<='9')return value-'0';return value-'A'+10;}
            int ReadByte(readonly(char)* data,ref nuint at)nothrow{int value=(Digit(data[at])<<4)|Digit(data[at+1]);at+=2;return value;}
            int ReadInt(readonly(char)* data,ref nuint at)nothrow{uint value=0u;for(int shift=0;shift<32;shift+=8)value=value|((uint)ReadByte(data,ref at)<<shift);return (int)value;}
            int main(){readonly(char)* packet="{{{hex}}}";nuint at=0;
                for(int row=0;row<{{{inputs.Count}}};row++){
                    nuint count=(nuint)ReadByte(packet,ref at);int[33] a;int[33] b;int[33] c;int[33] d;int[33] ascending;int[33] descending;
                    for(nuint i=0;i<count;i++){int value=ReadInt(packet,ref at);a[i]=value;b[i]=value;c[i]=value;d[i]=value;}
                    for(nuint i=0;i<count;i++)ascending[i]=ReadInt(packet,ref at);for(nuint i=0;i<count;i++)descending[i]=ReadInt(packet,ref at);
                    Span<int> first=new(a,count);Span<int> second=new(b,count);Span<int> third=new(c,count);Span<int> fourth=new(d,count);
                    int before=attempts;deny=true;Sort(first);Sort(third,Descending);deny=false;if(attempts!=before)return 1;
                    before=attempts;StableSort(second);StableSort(fourth,Descending);
                    int expectedAllocations=0;if(count>=2)expectedAllocations=4;if(attempts-before!=expectedAllocations)return 2;
                    for(nuint i=0;i<count;i++)if(a[i]!=ascending[i]||b[i]!=ascending[i]||c[i]!=descending[i]||d[i]!=descending[i])return 3;
                }
                if(at!={{{hex.Length}}})return 4;
                {{{floating}}}
                if(allocated!=released)return 30;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NoncopyableComparableOwnersRetainStableTiesAndOriginalFailuresKeepEveryOwner(int optimization)
        =>Run(Allocator+"""
            int made=0;int dead=0;int comparisons=0;int failAt=0;int[8] keys=[2,1,2,0,1,0,2,1];
            struct Owner:IComparable<Owner>{
                public int Id;public int Key;public String Token;
                public Owner(int id,int key){Id=id;Key=key;Token=s"owner";made++;}
                public immutable int CompareTo(immutable(Owner)* other){comparisons++;if(comparisons==failAt)throw new Exception("original comparison",77);if(Key<other.Key)return -1;if(Key>other.Key)return 1;return 0;}
                ~Owner(){dead++;}
            }
            Vector<Owner> Make(){Vector<Owner> result=new();result.Reserve(8);for(int i=0;i<8;i++)result.Add(new Owner(i,keys[(nuint)i]));return move result;}
            int main(){int[8] stable=[3,5,1,4,7,0,2,6];
                for(int which=0;which<2;which++){
                    {
                        Vector<Owner> values=Make();nuint[8] tokens;for(nuint i=0;i<8;i++)tokens[i]=(values[i].Token.Data).reinterpret<nuint>();
                        int before=attempts;int live=allocated-released;comparisons=0;
                        if(which==0){deny=true;Sort(values.AsSpan());deny=false;if(attempts!=before)return 1;}
                        else{StableSort(values.AsSpan());if(attempts-before!=2)return 2;}
                        if(made-dead!=8||values.Count!=8||allocated-released!=live)return 3;
                        bool[8] seen;for(nuint i=0;i<8;i++)seen[i]=false;
                        for(nuint i=0;i<8;i++){int id=values[i].Id;if(id<0||id>=8||seen[(nuint)id]||(values[i].Token.Data).reinterpret<nuint>()!=tokens[(nuint)id])return 4;seen[(nuint)id]=true;if(i!=0&&values[i-1].Key>values[i].Key)return 5;if(which==1&&id!=stable[i])return 6;}
                    }
                    if(made!=dead||allocated!=released)return 7;
                    {
                        Vector<Owner> values=Make();nuint[8] tokens;for(nuint i=0;i<8;i++)tokens[i]=(values[i].Token.Data).reinterpret<nuint>();
                        int live=allocated-released;comparisons=0;failAt=4;bool caught=false;
                        try{if(which==0)Sort(values.AsSpan());else StableSort(values.AsSpan());}
                        catch(ref Exception error){if(error.GetCode()!=77||!CString::Equals(error.GetMessage(),"original comparison"))return 8;caught=true;}failAt=0;
                        if(!caught||values.Count!=8||made-dead!=8||allocated-released!=live)return 9;
                        bool[8] seen;for(nuint i=0;i<8;i++)seen[i]=false;
                        for(nuint i=0;i<8;i++){int id=values[i].Id;if(id<0||id>=8||seen[(nuint)id]||(values[i].Token.Data).reinterpret<nuint>()!=tokens[(nuint)id])return 10;seen[(nuint)id]=true;if(which==1&&id!=(int)i)return 11;}
                        if(which==0)Sort(values.AsSpan());else StableSort(values.AsSpan());
                        for(nuint i=1;i<8;i++)if(values[i-1].Key>values[i].Key)return 12;
                    }
                    if(made!=dead||allocated!=released)return 13;
                }
                return 0;
            }
            """,optimization);

    private static string FloatingBlock(string scalar,string word,string suffix,ulong[] input,ulong[] expected,int code)
    {
        string Values(ulong[] values)=>string.Join(",",values.Select(value=>"0x"+value.ToString("x",System.Globalization.CultureInfo.InvariantCulture)+suffix));
        return $$"""
            {
                {{word}}[{{input.Length}}] raw=[{{Values(input)}}];{{word}}[{{input.Length}}] expected=[{{Values(expected)}}];
                {{scalar}}[{{input.Length}}] values;{{scalar}}[{{input.Length}}] stable;
                for(nuint i=0;i<{{input.Length}};i++){values[i]=(raw[i]).reinterpret<{{scalar}}>();stable[i]=values[i];}
                Span<{{scalar}}> first=new(values,{{input.Length}});Span<{{scalar}}> second=new(stable,{{input.Length}});
                int before=attempts;deny=true;Sort(first);deny=false;if(attempts!=before)return {{code}};
                before=attempts;StableSort(second);if(attempts-before!=2)return {{code+1}};
                bool[{{input.Length}}] seen;for(nuint i=0;i<{{input.Length}};i++)seen[i]=false;
                for(nuint i=0;i<{{input.Length}};i++){
                    {{scalar}} wanted=(expected[i]).reinterpret<{{scalar}}>();
                    if(wanted!=wanted){if(values[i]==values[i])return {{code+2}};}else if(values[i]!=wanted)return {{code+3}};
                    if((stable[i]).reinterpret<{{word}}>()!=expected[i])return {{code+4}};
                    {{word}} actual=(values[i]).reinterpret<{{word}}>();bool found=false;
                    for(nuint j=0;j<{{input.Length}};j++)if(!seen[j]&&raw[j]==actual){seen[j]=true;found=true;break;}
                    if(!found)return {{code+5}};
                }
            }
            """;
    }
    private static void Run(string program,int optimization)
    {
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("default-ordering.gf",program));
        var result=CompilerTestHelper.Run(sources,optimization);Assert.True(result.ExitCode==0,$"Default ordering exit {result.ExitCode}: {result.StandardError}");
    }
}
