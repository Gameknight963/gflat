using System.Text;

namespace gflat.Tests;

public sealed class StandardQueryOrderTests
{
    private const string Allocator="""
        using Standard; using Standard::Text; using Standard::Memory; using Standard::Queries; using Standard::Collections;extern void*? malloc(nuint count);extern void free(void* storage);
        int attempts=0;int allocated=0;int released=0;int denied=0;int failAfter=-1;bool deny=false;
        namespace Memory{
            public replace void*? Allocate(nuint count){attempts++;if(deny){denied++;return null;}if(failAfter==0){failAfter=-1;denied++;return null;}if(failAfter>0)failAfter--;void*? value=malloc(count);if(value!=null)allocated++;return value;}
            public replace void Free(void* storage)nothrow{released++;free(storage);}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void StableDefaultAndCustomOrderingMatchLinqAndIndependentCursorsSnapshotAtFirstMove(int optimization)
    {
        var random=new Random(591317);var inputs=new List<int[]>();for(int row=0;row<42;row++)inputs.Add(Enumerable.Range(0,row%21).Select(_=>random.Next(-4,5)).ToArray());
        using var bytes=new MemoryStream();using(var writer=new BinaryWriter(bytes,Encoding.UTF8,true))foreach(var input in inputs){
            writer.Write((byte)input.Length);foreach(int value in input)writer.Write(value);
            foreach(var value in input.Select((key,id)=>(key,id)).OrderBy(value=>value.key))writer.Write((byte)value.id);
            foreach(var value in input.Select((key,id)=>(key,id)).OrderByDescending(value=>value.key))writer.Write((byte)value.id);
        }
        string hex=Convert.ToHexString(bytes.ToArray());
        Run(Allocator+$$$"""
            struct Tagged:IComparable<Tagged>{
                public int Key;public int Id;public Tagged(){Key=0;Id=-1;}public Tagged(int key,int id){Key=key;Id=id;}
                public immutable int CompareTo(immutable(Tagged)* other){if(Key<other.Key)return -1;if(Key>other.Key)return 1;return 0;}
            }
            int Compare(immutable(Tagged)* first,immutable(Tagged)* second)=>first.CompareTo(second);
            int Descending(immutable(Tagged)* first,immutable(Tagged)* second)=>second.CompareTo(first);
            int Ordinary(readonly(int) value)=>(int)value;
            int Digit(char value)nothrow{if(value<='9')return value-'0';return value-'A'+10;}
            int ReadByte(readonly(char)* data,ref nuint at)nothrow{int value=(Digit(data[at])<<4)|Digit(data[at+1]);at+=2;return value;}
            int ReadInt(readonly(char)* data,ref nuint at)nothrow{uint value=0u;for(int shift=0;shift<32;shift+=8)value=value|((uint)ReadByte(data,ref at)<<shift);return (int)value;}
            int main(){readonly(char)* packet="{{{hex}}}";nuint at=0;
                for(int row=0;row<{{{inputs.Count}}};row++){
                    {
                        nuint count=(nuint)ReadByte(packet,ref at);Tagged[21] data;int[21] keys;int[21] ascending;int[21] descending;
                        for(nuint i=0;i<count;i++){keys[i]=ReadInt(packet,ref at);data[i]=new Tagged(keys[i],(int)i);}
                        for(nuint i=0;i<count;i++)ascending[i]=ReadByte(packet,ref at);for(nuint i=0;i<count;i++)descending[i]=ReadByte(packet,ref at);
                        Span<Tagged> source=new(&data[0],count);var standard=source.AsEnumerable().Order();var custom=source.AsEnumerable().Order(&Compare);var reversed=source.AsEnumerable().Order(&Descending);
                        for(int pass=0;pass<2;pass++){
                            var a=standard.GetEnumerator();var b=custom.GetEnumerator();var c=reversed.GetEnumerator();
                            for(nuint i=0;i<count;i++){if(!a.MoveNext()||!b.MoveNext()||!c.MoveNext()||a.Current.Id!=ascending[i]||b.Current.Id!=ascending[i]||c.Current.Id!=descending[i])return 1;}
                            if(a.MoveNext()||b.MoveNext()||c.MoveNext())return 2;
                        }
                        for(nuint i=0;i<count;i++)if(data[i].Key!=keys[i]||data[i].Id!=(int)i)return 3;
                    }
                    if(allocated!=released)return 4;
                }
                {
                    Tagged[3] data=[new Tagged(3,0),new Tagged(1,1),new Tagged(2,2)];Span<Tagged> source=new(&data[0],3);var query=source.AsEnumerable().Order();
                    var first=query.GetEnumerator();if(!first.MoveNext()||first.Current.Id!=1)return 5;
                    data[1].Key=99;if(first.Current.Key!=1)return 6;var second=query.GetEnumerator();if(!second.MoveNext()||second.Current.Id!=2)return 7;
                    if(!first.MoveNext()||first.Current.Id!=2||!first.MoveNext()||first.Current.Id!=0||first.MoveNext())return 8;
                    int[3] integers=[3,1,2];Span<int> view=new(&integers[0],3);var projected=view.AsReadOnly().AsEnumerable().Select(&Ordinary).Order();
                    int expected=1;foreach(int value in projected){if(value!=expected)return 9;expected++;}if(expected!=4||integers[0]!=3)return 10;
                }
                if(at!={{{hex.Length}}}||allocated!=released)return 11;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MaterializationIsDeferredTakeZeroDoesNothingAndInitializationFailuresStayFinished(int optimization)
        =>Run(Allocator+"""
            int mode=0;int starts=0;int ends=0;int moves=0;int reads=0;int comparisons=0;bool bad=false;
            struct Source{
                public struct Iterator{
                    private int position;public Iterator(){position=0;if(mode==1)throw new Exception("constructor",71);starts++;}
                    public bool MoveNext(){moves++;if(mode==2&&position==1)throw new Exception("advance",72);if(position==4)return false;position++;return true;}
                    public int Current{get{reads++;if(mode==3&&position==2)throw new Exception("current",73);if(position==1)return 3;if(position==3)return 2;return 1;}}
                    ~Iterator(){ends++;}
                }
                public Iterator GetEnumerator()=>new Iterator();
            }
            int Compare(immutable(int)* first,immutable(int)* second){comparisons++;if(starts!=ends)bad=true;if(mode==4)throw new Exception("compare",74);if(*first<*second)return -1;if(*first>*second)return 1;return 0;}
            int main(){
                var source=new Enumerable<int,Source,Source::Iterator>(new Source());var query=source.Order(&Compare);
                int before=attempts;deny=true;
                {var unused=query.GetEnumerator();var empty=query.Take(0).GetEnumerator();if(empty.MoveNext())return 1;}
                deny=false;if(attempts!=before||starts!=0||moves!=0||reads!=0||comparisons!=0)return 2;
                {
                    var iterator=query.GetEnumerator();bool caught=false;try{iterator.Current;}catch(ref Exception error){caught=true;}if(!caught||starts!=0)return 3;
                    if(!iterator.MoveNext()||iterator.Current!=1||moves!=5||reads!=4||starts!=ends||comparisons==0||bad)return 4;
                    int oldMoves=moves;int oldReads=reads;int oldComparisons=comparisons;int sum=iterator.Current;
                    while(iterator.MoveNext())sum+=iterator.Current;
                    if(sum!=7||moves!=oldMoves||reads!=oldReads||comparisons!=oldComparisons||iterator.MoveNext())return 5;
                }
                int oldReads=reads;int oldMoves=moves;
                {var one=query.Take(1).GetEnumerator();if(!one.MoveNext()||one.Current!=1||one.MoveNext())return 6;}
                if(reads!=oldReads+4||moves!=oldMoves+5||starts!=ends||allocated!=released)return 7;
                for(int failure=1;failure<=4;failure++){
                    var iterator=query.GetEnumerator();int live=allocated-released;mode=failure;bool caught=false;
                    try{iterator.MoveNext();}catch(ref Exception error){if(error.GetCode()!=70+failure)return 8;caught=true;}mode=0;
                    if(!caught||starts!=ends||allocated-released!=live)return 9;
                    oldMoves=moves;oldReads=reads;int oldComparisons=comparisons;before=attempts;
                    if(iterator.MoveNext()||iterator.MoveNext()||moves!=oldMoves||reads!=oldReads||comparisons!=oldComparisons||attempts!=before)return 10;
                    caught=false;try{iterator.Current;}catch(ref Exception error){caught=true;}if(!caught)return 11;
                }
                if(allocated!=released)return 12;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BorrowedCurrentSupportsFreshNoncopyableOwnersAndEveryMeasuredFailureReleasesStaging(int optimization)
        =>Run(Allocator+"""
            int made=0;int dead=0;int starts=0;int ends=0;bool failCompare=false;bool failCopy=false;int copiedLive=0;
            struct Owner{
                public int Key;public int Id;private String token;public Owner(int key,int id){Key=key;Id=id;token=s"owner";made++;}~Owner(){dead++;}
            }
            struct Source{
                public struct Iterator{
                    private int position;private String token;public Iterator(){position=0;token=s"iterator";starts++;}
                    public bool MoveNext(){if(position==5)return false;position++;return true;}
                    public Owner Current{get{int key=2;if(position==2||position==3)key=1;else if(position==4)key=3;return new Owner(key,position);}}
                    ~Iterator(){ends++;}
                }
                public Iterator GetEnumerator()=>new Iterator();
            }
            int Compare(immutable(Owner)* first,immutable(Owner)* second){if(failCompare)throw new Exception("compare",74);if(first.Key<second.Key)return -1;if(first.Key>second.Key)return 1;return 0;}
            struct Copyable{
                public int Id;private String token;public Copyable(int id){Id=id;token=s"copy";copiedLive++;}
                public copy Copyable(immutable(Copyable)* other){Id=other.Id;token=other.token;if(failCopy&&Id==2)throw new Exception("copy",75);copiedLive++;}
                ~Copyable(){copiedLive--;}
            }
            int CompareCopy(immutable(Copyable)* first,immutable(Copyable)* second){if(first.Id<second.Id)return -1;if(first.Id>second.Id)return 1;return 0;}
            int main(){
                var source=new Enumerable<Owner,Source,Source::Iterator>(new Source());var query=source.Order(&Compare);int before=attempts;int points=0;
                {
                    var iterator=query.GetEnumerator();if(!iterator.MoveNext()||made-dead!=5||starts!=ends)return 1;points=attempts-before;
                    immutable(Owner)* first=&iterator.Current;if(first.Id!=2||&iterator.Current!=first||iterator.Current.Id!=2)return 2;
                    int[5] ids=[2,3,1,5,4];nuint index=0;
                    while(true){if(iterator.Current.Id!=ids[index])return 3;index++;if(!iterator.MoveNext())break;}
                    if(index!=5||made-dead!=5)return 4;
                }
                if(points==0||made!=dead||allocated!=released)return 5;
                for(int point=0;point<points;point++){
                    var iterator=query.GetEnumerator();int live=allocated-released;int deniedBefore=denied;failAfter=point;bool caught=false;
                    try{iterator.MoveNext();}catch(ref BadAllocException error){caught=true;}failAfter=-1;
                    if(!caught||denied!=deniedBefore+1||made!=dead||starts!=ends||allocated-released!=live)return 6;
                    before=attempts;if(iterator.MoveNext()||attempts!=before)return 7;
                }
                {
                    var iterator=query.GetEnumerator();failCompare=true;bool caught=false;try{iterator.MoveNext();}catch(ref Exception error){if(error.GetCode()!=74)return 8;caught=true;}failCompare=false;
                    if(!caught||made!=dead||starts!=ends||allocated!=released||iterator.MoveNext())return 9;
                }
                {
                    Vector<Copyable> values=new();values.Add(new Copyable(3));values.Add(new Copyable(2));values.Add(new Copyable(1));var ordered=values.AsEnumerable().Order(&CompareCopy);
                    {
                        var iterator=ordered.GetEnumerator();int live=allocated-released;failCopy=true;bool caught=false;try{iterator.MoveNext();}catch(ref Exception error){if(error.GetCode()!=75)return 10;caught=true;}failCopy=false;
                        if(!caught||copiedLive!=3||allocated-released!=live||iterator.MoveNext())return 11;
                    }
                    {
                        var iterator=ordered.GetEnumerator();if(!iterator.MoveNext()||iterator.Current.Id!=1||!iterator.MoveNext()||iterator.Current.Id!=2||copiedLive!=6)return 12;
                        failCopy=true;bool caught=false;int live=allocated-released;try{Copyable value=iterator.Current;}catch(ref Exception error){if(error.GetCode()!=75)return 13;caught=true;}failCopy=false;
                        if(!caught||copiedLive!=6||allocated-released!=live||iterator.Current.Id!=2)return 14;
                        {Copyable value=iterator.Current;if(value.Id!=2||copiedLive!=7)return 15;}
                        if(!iterator.MoveNext()||iterator.Current.Id!=3)return 16;
                    }
                    if(copiedLive!=3||values[0].Id!=3||values[1].Id!=2||values[2].Id!=1)return 17;
                }
                if(copiedLive!=0||made!=dead||allocated!=released)return 18;return 0;
            }
            """,optimization);

    private static IEnumerable<SourceFile> Sources(string program)
        =>Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Standard"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("query-order.gf",program));
    private static void Run(string program,int optimization)
    {
        var result=CompilerTestHelper.Run(Sources(program),optimization);Assert.True(result.ExitCode==0,$"Order exit {result.ExitCode}: {result.StandardError}");
    }
}
