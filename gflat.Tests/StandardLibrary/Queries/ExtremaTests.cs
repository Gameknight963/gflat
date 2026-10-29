using gflat.CompileExceptions;

namespace gflat.Tests.Std.Queries;

public sealed class ExtremaTests
{
    private const string Allocator="""
        using std; using std::Text; using std::Memory; using std::Queries; using std::Collections;extern void*? malloc(nuint count);extern void free(void* storage);
        int allocated=0;int released=0;int attempts=0;bool deny=false;
        namespace Memory{
            public replace void*? Allocate(nuint count){attempts++;if(deny)return null;void*? value=malloc(count);if(value!=null)allocated++;return value;}
            public replace void Free(void* storage)nothrow{released++;free(storage);}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void IntegerOraclesAndFloatingTotalOrderUseTheirDeclaredComparisonWithoutAllocation(int optimization)
    {
        var random=new Random(615097);var data=new List<int>();var rows=new List<int>();
        for(int sample=0;sample<48;sample++){
            int[] values=Enumerable.Range(0,sample%16).Select(_=>random.Next(-1000,1001)).ToArray();
            rows.AddRange([data.Count,values.Length,values.Length==0?0:values.Min(),values.Length==0?0:values.Max()]);data.AddRange(values);
        }
        data.Add(0);
        double nanA=BitConverter.UInt64BitsToDouble(0x7ff8000000000001),nanB=BitConverter.UInt64BitsToDouble(0x7ff8000000000002);
        double negativeZero=BitConverter.UInt64BitsToDouble(0x8000000000000000);
        double[][] floats=[[nanA,0,double.NegativeInfinity],[nanB,nanA],[0,negativeZero],[negativeZero,0],
            [double.NegativeInfinity,-1,double.PositiveInfinity],[nanA,double.PositiveInfinity,nanB]];
        var bits=new List<ulong>();var floatRows=new List<int>();var expectedBits=new List<ulong>();
        foreach(double[] values in floats){
            floatRows.AddRange([bits.Count,values.Length]);bits.AddRange(values.Select(BitConverter.DoubleToUInt64Bits));
            // CompareTo has the required NaN-low/zero-equal total ordering.
            // Stable OrderBy preserves first ties; Enumerable.Min/Max's special
            // floating handling is deliberately not the oracle.
            expectedBits.Add(BitConverter.DoubleToUInt64Bits(values.OrderBy(value=>value,Comparer<double>.Default).First()));
            expectedBits.Add(BitConverter.DoubleToUInt64Bits(values.OrderByDescending(value=>value,Comparer<double>.Default).First()));
        }
        Run(Allocator+$$"""
            int Compare(immutable(int)* left,immutable(int)* right){if(*left<*right)return -1;if(*left>*right)return 1;return 0;}
            int Descending(immutable(int)* left,immutable(int)* right)=>Compare(right,left);
            int main(){
                int[{{data.Count}}] data=[{{string.Join(',',data)}}];int[{{rows.Count}}] rows=[{{string.Join(',',rows)}}];
                for(nuint row=0;row<{{rows.Count}};row+=4){
                    Span<int> view=new(&data[(nuint)rows[row]],(nuint)rows[row+1]);var query=view.AsEnumerable();
                    if(view.Length==0){
                        for(int which=0;which<2;which++){bool caught=false;try{if(which==0)query.Min();else query.Max();}catch(ref Exception error){if(!CString::Equals(error.GetMessage(),"Sequence is empty"))return 1;caught=true;}if(!caught)return 2;}
                    }else{
                        int before=attempts;deny=true;
                        if(query.Min()!=rows[row+2]||query.Max()!=rows[row+3]||query.Min(&Compare)!=rows[row+2]||query.Max(&Compare)!=rows[row+3])return 3;
                        if(query.Min(&Descending)!=rows[row+3]||query.Max(&Descending)!=rows[row+2]||query.Min()!=rows[row+2])return 4;
                        deny=false;if(attempts!=before)return 5;
                    }
                }
                ulong[{{bits.Count}}] bits=[{{string.Join(',',bits.Select(value=>value+"ul"))}}];
                int[{{floatRows.Count}}] floatRows=[{{string.Join(',',floatRows)}}];ulong[{{expectedBits.Count}}] expected=[{{string.Join(',',expectedBits.Select(value=>value+"ul"))}}];
                double[{{bits.Count}}] values;for(nuint i=0;i<{{bits.Count}};i++)values[i]=*((double*)&bits[i]);
                int before=attempts;deny=true;
                for(nuint row=0;row<{{floatRows.Count}};row+=2){
                    Span<double> view=new(&values[(nuint)floatRows[row]],(nuint)floatRows[row+1]);double low=view.AsEnumerable().Min();double high=view.AsEnumerable().Max();
                    if(*((ulong*)&low)!=expected[row]||*((ulong*)&high)!=expected[row+1])return 6;
                }
                deny=false;if(attempts!=before)return 7;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CountedAsciiStringOraclesAndTaggedEqualKeysRetainTheFirstWinner(int optimization)
    {
        // ASCII (including NUL) gives identical UTF8-byte and UTF16 ordinal order.
        string[] words=["","alpha","Alpha","same","same\0a","same\0b","\0","0","z","\u007f","a\r"];
        var blob=new List<byte>();var offsets=new List<int>();foreach(string word in words){offsets.Add(blob.Count);blob.AddRange(System.Text.Encoding.ASCII.GetBytes(word));}blob.Add(0);
        var random=new Random(618327);var indices=new List<int>();var rows=new List<int>();
        for(int sample=0;sample<32;sample++){
            int[] sequence=Enumerable.Range(0,sample%7+1).Select(_=>random.Next(words.Length)).ToArray();
            rows.AddRange([indices.Count,sequence.Length,sequence.OrderBy(index=>words[index],StringComparer.Ordinal).First(),sequence.OrderByDescending(index=>words[index],StringComparer.Ordinal).First()]);indices.AddRange(sequence);
        }
        Run(Allocator+$$"""
            struct Tagged:IComparable<Tagged>{
                public int Key;public int Id;public Tagged(int key,int id){Key=key;Id=id;}
                public immutable int CompareTo(immutable(Tagged)* other){if(Key<other.Key)return -1;if(Key>other.Key)return 1;return 0;}
            }
            int ReverseCompare(immutable(Tagged)* left,immutable(Tagged)* right)=>right.CompareTo(left);
            int main(){
                byte[{{blob.Count}}] blob=[{{string.Join(',',blob.Select(value=>"(byte)"+value))}}];int[{{offsets.Count}}] offsets=[{{string.Join(',',offsets)}}];int[{{words.Length}}] lengths=[{{string.Join(',',words.Select(word=>word.Length))}}];
                int[{{indices.Count}}] indices=[{{string.Join(',',indices)}}];int[{{rows.Count}}] rows=[{{string.Join(',',rows)}}];
                for(nuint row=0;row<{{rows.Count}};row+=4){
                    {
                        Vector<String> words=new();for(nuint i=0;i<(nuint)rows[row+1];i++){int index=indices[(nuint)rows[row]+i];words.Add(String::From((readonly(char)*)&blob[(nuint)offsets[(nuint)index]],(nuint)lengths[(nuint)index]));}
                        String low=words.AsEnumerable().Min();String high=words.AsEnumerable().Max();
                        int minIndex=rows[row+2];int maxIndex=rows[row+3];
                        if(!low.Equals((readonly(char)*)&blob[(nuint)offsets[(nuint)minIndex]],(nuint)lengths[(nuint)minIndex])||!high.Equals((readonly(char)*)&blob[(nuint)offsets[(nuint)maxIndex]],(nuint)lengths[(nuint)maxIndex]))return 1;
                        words.Clear();if(!low.Equals((readonly(char)*)&blob[(nuint)offsets[(nuint)minIndex]],(nuint)lengths[(nuint)minIndex])||!high.Equals((readonly(char)*)&blob[(nuint)offsets[(nuint)maxIndex]],(nuint)lengths[(nuint)maxIndex]))return 2;
                    }
                    if(allocated!=released)return 3;
                }
                Tagged[5] data=[new Tagged(2,10),new Tagged(1,20),new Tagged(1,21),new Tagged(3,30),new Tagged(3,31)];Span<Tagged> view=new(data,5);
                Tagged low=view.AsEnumerable().Min();Tagged high=view.AsEnumerable().Max();if(low.Id!=20||high.Id!=30)return 4;
                low=view.AsEnumerable().Min(&ReverseCompare);high=view.AsEnumerable().Max(&ReverseCompare);if(low.Id!=30||high.Id!=20||data[1].Id!=20||data[3].Id!=30)return 5;
                return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EachCurrentAndComparisonHasExactOrderAndIteratorFailuresPreserveTheSource(int optimization)
        =>Run("""
            using std; using std::Text; using std::Memory; using std::Queries; using std::Collections;int mode=0;int length=5;int starts=0;int ends=0;int moves=0;int reads=0;int calls=0;int[8] pairs;
            struct Source{
                private int* values;public Source(int* data){values=data;}
                public struct Iterator{
                    private int* values;private int position;public Iterator(int* data){values=data;position=0;if(mode==1)throw new Exception("constructor",71);starts++;}
                    public bool MoveNext(){moves++;if(mode==2&&position==1)throw new Exception("advance",72);if(position==length)return false;position++;return true;}
                    public int Current{get{reads++;if(mode==3&&position==2)throw new Exception("current",73);return values[position-1];}}
                    ~Iterator(){ends++;}
                }
                public Iterator GetEnumerator()=>new Iterator(values);
            }
            int Compare(immutable(int)* current,immutable(int)* best){pairs[(nuint)calls*2]=*current;pairs[(nuint)calls*2+1]=*best;calls++;if(mode==4)throw new Exception("comparison",74);if(*current<*best)return -1;if(*current>*best)return 1;return 0;}
            void Reset(){moves=0;reads=0;calls=0;}
            int main(){
                int[5] values=[3,1,4,1,4];var query=new Enumerable<int,Source,Source::Iterator>(new Source(&values[0]));
                int[8] minPairs=[1,3,4,1,1,1,4,1];int[8] maxPairs=[1,3,4,3,1,4,4,4];
                for(int which=0;which<2;which++){
                    Reset();int result=0;if(which==0)result=query.Min(&Compare);else result=query.Max(&Compare);
                    int expected=1;if(which==1)expected=4;if(result!=expected||moves!=6||reads!=5||calls!=4||starts!=ends)return 1;
                    for(nuint i=0;i<8;i++){int value=minPairs[i];if(which==1)value=maxPairs[i];if(pairs[i]!=value)return 2;}
                    length=1;Reset();if(which==0)result=query.Min(&Compare);else result=query.Max(&Compare);
                    if(result!=3||moves!=2||reads!=1||calls!=0||starts!=ends)return 3;
                    length=0;Reset();bool caught=false;try{if(which==0)query.Min(&Compare);else query.Max(&Compare);}catch(ref Exception error){if(!CString::Equals(error.GetMessage(),"Sequence is empty"))return 4;caught=true;}
                    if(!caught||moves!=1||reads!=0||calls!=0||starts!=ends)return 5;
                    length=5;
                    for(int failure=1;failure<=4;failure++){
                        mode=failure;Reset();caught=false;try{if(which==0)query.Min(&Compare);else query.Max(&Compare);}catch(ref Exception error){if(error.GetCode()!=70+failure)return 6;caught=true;}
                        mode=0;if(!caught||starts!=ends||values[0]!=3||values[1]!=1||values[2]!=4||values[3]!=1||values[4]!=4)return 7;
                    }
                }
                return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FreshNoncopyableOwnersAndBorrowedCopyFailuresCleanUpBothCandidates(int optimization)
        =>Run(Allocator+"""
            int mode=0;int made=0;int dead=0;int starts=0;int ends=0;int calls=0;int copiedLive=0;bool failCopy=false;
            struct Owner{
                public int Key;public int Id;private String token;
                public Owner(int key,int id){Key=key;Id=id;token=s"owner";made++;}~Owner(){dead++;}
            }
            struct Source{
                public struct Iterator{
                    private int position;private String token;public Iterator(){position=0;token=s"iterator";if(mode==1)throw new Exception("constructor",71);starts++;}
                    public bool MoveNext(){if(mode==2&&position==1)throw new Exception("advance",72);if(position==5)return false;position++;return true;}
                    public Owner Current{get{if(mode==3&&position==2)throw new Exception("current",73);int key=1;if(position==1)key=2;if(position>=4)key=3;return new Owner(key,position);}}
                    ~Iterator(){ends++;}
                }
                public Iterator GetEnumerator()=>new Iterator();
            }
            int Compare(immutable(Owner)* current,immutable(Owner)* best){calls++;if(mode==4)throw new Exception("comparison",74);if(current.Key<best.Key)return -1;if(current.Key>best.Key)return 1;return 0;}
            struct Copyable{
                public int Id;private String token;public Copyable(int id){Id=id;token=s"copy";copiedLive++;}
                public copy Copyable(immutable(Copyable)* source){Id=source.Id;token=source.token;if(failCopy&&Id==2)throw new Exception("copy",76);copiedLive++;}
                ~Copyable(){copiedLive--;}
            }
            int CopyCompare(immutable(Copyable)* left,immutable(Copyable)* right)=>left.Id-right.Id;
            int main(){
                var query=new Enumerable<Owner,Source,Source::Iterator>(new Source());
                for(int which=0;which<2;which++){
                    calls=0;
                    {Owner result=new(0,0);int initial=made;if(which==0)result=query.Min(&Compare);else result=query.Max(&Compare);
                     int expected=2;if(which==1)expected=4;if(result.Id!=expected||made-initial!=5||made-dead!=1||calls!=4||starts!=ends)return 1;}
                    if(made!=dead||allocated!=released)return 2;
                    for(int failure=1;failure<=4;failure++){
                        mode=failure;calls=0;bool caught=false;try{if(which==0)query.Min(&Compare);else query.Max(&Compare);}catch(ref Exception error){if(error.GetCode()!=70+failure)return 3;caught=true;}
                        mode=0;if(!caught||made!=dead||starts!=ends||allocated!=released)return 4;
                    }
                    {
                        Vector<Copyable> source=new();source.Add(new Copyable(1));source.Add(new Copyable(2));source.Add(new Copyable(3));int baseline=allocated-released;failCopy=true;bool caught=false;
                        try{if(which==0)source.AsEnumerable().Min(&CopyCompare);else source.AsEnumerable().Max(&CopyCompare);}catch(ref Exception error){if(error.GetCode()!=76)return 5;caught=true;}failCopy=false;
                        if(!caught||copiedLive!=3||allocated-released!=baseline||source[0].Id!=1||source[1].Id!=2||source[2].Id!=3)return 6;
                    }
                    if(copiedLive!=0||allocated!=released)return 7;
                }
                return 0;
            }
            """,optimization);

    [Fact]
    public void ExplicitComparisonDoesNotRequireTheDefaultComparableContract()
    {
        const string declarations="using std; using std::Text; using std::Memory; using std::Queries; using std::Collections;struct Plain{public int Key;public Plain(int key){Key=key;}}int Compare(immutable(Plain)* left,immutable(Plain)* right)=>left.Key-right.Key;";
        const string setup="int main(){Plain[2] values=[new Plain(2),new Plain(1)];Span<Plain> view=new(values,2);";
        Compiler.Check(Sources(declarations+setup+"Plain low=view.AsEnumerable().Min(&Compare);Plain high=view.AsEnumerable().Max(&Compare);return low.Key+high.Key;}"));
        foreach(string operation in new[]{"Min","Max"}){
            var error=Assert.Throws<TypeCheckException>(()=>Compiler.Check(Sources(declarations+setup+$"std::Queries::{operation}(view.AsEnumerable());return 0;}}")));
            Assert.Contains($"No matching overload for '{operation}'",error.Message);
        }
    }

    private static IEnumerable<SourceFile> Sources(string source)
        =>Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("query-extrema.gf",source));
    private static void Run(string source,int optimization)
    {
        var result=CompilerTestHelper.Run(Sources(source),optimization);Assert.True(result.ExitCode==0,$"Query extrema exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
