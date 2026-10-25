using System.Text;

namespace gflat.Tests;

public sealed class StandardDefaultSearchTests
{
    private const string Allocator="""
        using std; using std::Collections;extern void*? malloc(nuint count);extern void free(void* storage);
        int attempts=0;int allocated=0;int released=0;bool deny=false;
        namespace Memory{
            public replace void*? Allocate(nuint count){attempts++;if(deny)return null;void*? result=malloc(count);if(result!=null)allocated++;return result;}
            public replace void Free(void* storage)nothrow{released++;free(storage);}
        }
        int Digit(char value)nothrow{if(value<='9')return value-'0';return value-'A'+10;}
        int ReadByte(readonly(char)* data,ref nuint at)nothrow{int value=(Digit(data[at])<<4)|Digit(data[at+1]);at+=2;return value;}
        int ReadInt(readonly(char)* data,ref nuint at)nothrow{uint value=0u;for(int shift=0;shift<32;shift+=8)value=value|((uint)ReadByte(data,ref at)<<shift);return (int)value;}
        ulong ReadULong(readonly(char)* data,ref nuint at)nothrow{ulong value=0ul;for(int shift=0;shift<64;shift+=8)value=value|((ulong)ReadByte(data,ref at)<<shift);return value;}
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DefaultBoundsFirstMatchAndSortednessMatchIndependentIntegerAndFloatingOracles(int optimization)
    {
        List<int[]> inputs=[[],[2],[1,2,2,2,4],[-1,0,1],[int.MinValue,0,int.MaxValue]];
        var random=new Random(831597);for(int row=0;row<24;row++)inputs.Add(Enumerable.Range(0,random.Next(0,49)).Select(_=>random.Next(-10,11)).Order().ToArray());
        int[] keys=[int.MinValue,-11,-3,0,2,3,11,int.MaxValue];
        using var bytes=new MemoryStream();using(var writer=new BinaryWriter(bytes,Encoding.UTF8,true)){
            foreach(var input in inputs)foreach(int key in keys){
                writer.Write((byte)input.Length);writer.Write(key);foreach(int value in input)writer.Write(value);
                writer.Write((byte)Sentinel(Array.FindIndex(input,value=>value>=key),input.Length));
                writer.Write((byte)Sentinel(Array.FindIndex(input,value=>value>key),input.Length));
                writer.Write((byte)Sentinel(Array.IndexOf(input,key),input.Length));
            }
            ulong[] raw=[0x7ff8000000000001UL,0xfff8000000000002UL,0x8000000000000000UL,0UL,0xfff0000000000000UL,0x7ff0000000000000UL,
                unchecked((ulong)BitConverter.DoubleToInt64Bits(-2.5)),unchecked((ulong)BitConverter.DoubleToInt64Bits(3)),0UL];
            var values=raw.Select(bits=>BitConverter.Int64BitsToDouble(unchecked((long)bits))).Order().ToArray();
            double[] floatingKeys=[double.NaN,double.NegativeInfinity,-3,-2.5,-0.0,0.0,1,3,double.PositiveInfinity];
            writer.Write((byte)values.Length);foreach(double value in values)writer.Write(unchecked((ulong)BitConverter.DoubleToInt64Bits(value)));
            writer.Write((byte)floatingKeys.Length);foreach(double key in floatingKeys){
                writer.Write(unchecked((ulong)BitConverter.DoubleToInt64Bits(key)));
                writer.Write((byte)Sentinel(Array.FindIndex(values,value=>value.CompareTo(key)>=0),values.Length));
                writer.Write((byte)Sentinel(Array.FindIndex(values,value=>value.CompareTo(key)>0),values.Length));
                writer.Write((byte)Sentinel(Array.FindIndex(values,value=>value.CompareTo(key)==0),values.Length));
            }
        }
        string hex=Convert.ToHexString(bytes.ToArray());
        Run(Allocator+$$$"""
            int Descending(immutable(int)* first,immutable(int)* second){if(*first>*second)return -1;if(*first<*second)return 1;return 0;}
            int main(){readonly(char)* packet="{{{hex}}}";nuint at=0;int before=attempts;deny=true;
                for(int row=0;row<{{{inputs.Count*keys.Length}}};row++){
                    nuint count=(nuint)ReadByte(packet,ref at);int key=ReadInt(packet,ref at);int[48] data;for(nuint i=0;i<count;i++)data[i]=ReadInt(packet,ref at);
                    nuint lower=(nuint)ReadByte(packet,ref at);nuint upper=(nuint)ReadByte(packet,ref at);nuint match=(nuint)ReadByte(packet,ref at);
                    Span<int> values=new(data,count);
                    if(!IsSorted(values.AsImmutable())||LowerBound(values.AsImmutable(),&key)!=lower||UpperBound(values.AsImmutable(),&key)!=upper||BinarySearch(values.AsImmutable(),&key)!=match)return 1;
                }
                {
                    nuint count=(nuint)ReadByte(packet,ref at);double[9] data;
                    for(nuint i=0;i<count;i++){ulong bits=ReadULong(packet,ref at);data[i]=*((double*)&bits);}
                    Span<double> values=new(data,count);if(!IsSorted(values.AsImmutable()))return 2;
                    int countKeys=ReadByte(packet,ref at);
                    for(int row=0;row<countKeys;row++){
                        ulong bits=ReadULong(packet,ref at);double key=*((double*)&bits);
                        nuint lower=(nuint)ReadByte(packet,ref at);nuint upper=(nuint)ReadByte(packet,ref at);nuint match=(nuint)ReadByte(packet,ref at);
                        if(LowerBound(values.AsImmutable(),&key)!=lower||UpperBound(values.AsImmutable(),&key)!=upper||BinarySearch(values.AsImmutable(),&key)!=match)return 3;
                    }
                }
                int[4] reverse=[5,3,3,1];Span<int> descending=new(reverse,4);int key=3;
                if(IsSorted(descending.AsImmutable())||!IsSorted(descending.AsImmutable(),Descending))return 4;
                if(LowerBound(descending.AsImmutable(),&key,Descending)!=1||UpperBound(descending.AsImmutable(),&key,Descending)!=3||BinarySearch(descending.AsImmutable(),&key,Descending)!=1)return 5;
                deny=false;if(at!={{{hex.Length}}}||attempts!=before||allocated!=released)return 6;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ComparableKeysAndElementsAreBorrowedAndOriginalComparisonFailuresPreserveStorage(int optimization)
        =>Run(Allocator+"""
            int made=0;int dead=0;int comparisons=0;bool fail=false;
            struct Owner:IComparable<Owner>{
                public int Id;public int Key;public String Token;
                public Owner(int id,int key){Id=id;Key=key;Token=s"owner";made++;}
                public immutable int CompareTo(immutable(Owner)* other){comparisons++;if(fail)throw new Exception("original comparison",77);if(Key<other.Key)return -1;if(Key>other.Key)return 1;return 0;}
                ~Owner(){dead++;}
            }
            int main(){
                {
                    Vector<Owner> values=new();values.Reserve(5);int[5] keys=[1,2,2,4,7];
                    for(int i=0;i<5;i++)values.Add(new Owner(i,keys[(nuint)i]));Owner key=new(99,2);
                    nuint[5] addresses;nuint[5] tokens;for(nuint i=0;i<5;i++){addresses[i]=(nuint)&values[i];tokens[i]=(nuint)values[i].Token.Data;}
                    nuint keyAddress=(nuint)&key;nuint keyToken=(nuint)key.Token.Data;
                    int before=attempts;int live=allocated-released;deny=true;
                    if(LowerBound(values.AsImmutable(),&key)!=1||UpperBound(values.AsImmutable(),&key)!=3||BinarySearch(values.AsImmutable(),&key)!=1)return 1;
                    comparisons=0;if(!IsSorted(values.AsImmutable())||comparisons!=4)return 2;
                    Span<immutable(Owner)> empty=new();comparisons=0;
                    if(LowerBound(empty,&key)!=0||UpperBound(empty,&key)!=0||BinarySearch(empty,&key)!=0||!IsSorted(empty)||!IsSorted(values.AsImmutable().Slice(0,1))||comparisons!=0)return 3;
                    deny=false;if(attempts!=before||made!=6||dead!=0||allocated-released!=live)return 4;
                    for(int which=0;which<4;which++){
                        comparisons=0;fail=true;nuint index=99;bool sorted=false;bool caught=false;
                        try{if(which==0)index=LowerBound(values.AsImmutable(),&key);else if(which==1)index=UpperBound(values.AsImmutable(),&key);else if(which==2)index=BinarySearch(values.AsImmutable(),&key);else sorted=IsSorted(values.AsImmutable());}
                        catch(ref Exception error){if(error.GetCode()!=77||!CString::Equals(error.GetMessage(),"original comparison"))return 5;caught=true;}fail=false;
                        if(!caught||comparisons!=1||index!=99||sorted||values.Count!=5||made!=6||dead!=0||allocated-released!=live)return 6;
                        for(nuint i=0;i<5;i++)if((nuint)&values[i]!=addresses[i]||(nuint)values[i].Token.Data!=tokens[i]||values[i].Id!=(int)i||values[i].Key!=keys[i])return 7;
                        if((nuint)&key!=keyAddress||(nuint)key.Token.Data!=keyToken||key.Id!=99||key.Key!=2)return 8;
                    }
                    if(BinarySearch(values.AsImmutable(),&key)!=1)return 9;
                }
                if(made!=dead||allocated!=released)return 10;return 0;
            }
            """,optimization);

    private static int Sentinel(int index,int length)=>index<0?length:index;
    private static void Run(string program,int optimization)
    {
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("default-search.gf",program));
        var result=CompilerTestHelper.Run(sources,optimization);Assert.True(result.ExitCode==0,$"Default search exit {result.ExitCode}: {result.StandardError}");
    }
}
