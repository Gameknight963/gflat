using System.Text;

namespace gflat.Tests;

public sealed class StandardQueryCountByTests
{
    private const string Allocator="""
        using Standard; using Standard::Text; using Standard::Queries; using Standard::Collections;extern void*? malloc(nuint count);extern void free(void* storage);
        int attempts=0;int allocated=0;int released=0;int denied=0;int failAfter=-1;bool deny=false;
        namespace Memory{
            public replace void*? Allocate(nuint count){attempts++;if(deny){denied++;return null;}if(failAfter==0){failAfter=-1;denied++;return null;}if(failAfter>0)failAfter--;void*? value=malloc(count);if(value!=null)allocated++;return value;}
            public replace void Free(void* storage)nothrow{released++;free(storage);}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SeededGroupByOraclesReadCurrentAndSelectExactlyOnceAndEmptyMaterializationAllocatesNothing(int optimization)
    {
        List<int[]> samples=[[],[0],[1,1,1],[-1,0,1,int.MinValue,int.MaxValue]];var random=new Random(719153);
        for(int sample=0;sample<40;sample++)samples.Add(Enumerable.Range(0,sample%33).Select(_=>random.Next(-1000,1001)).ToArray());
        using var bytes=new MemoryStream();using(var writer=new BinaryWriter(bytes,Encoding.UTF8,true))foreach(var input in samples){
            var expected=input.GroupBy(value=>value%7).ToDictionary(group=>group.Key,group=>group.Count());
            writer.Write((byte)input.Length);writer.Write((byte)expected.Count);foreach(int value in input)writer.Write(value);
            foreach(var pair in expected){writer.Write(pair.Key);writer.Write((byte)pair.Value);}
        }
        string hex=Convert.ToHexString(bytes.ToArray());
        Run(Allocator+$$$"""
            int starts=0;int ends=0;int moves=0;int reads=0;int selections=0;
            struct Source{
                private int* values;private nuint length;public Source(int* input,nuint count){values=input;length=count;}
                public struct Iterator{
                    private int* values;private nuint length;private nuint position;
                    public Iterator(int* input,nuint count){values=input;length=count;position=0;starts++;}
                    public bool MoveNext(){moves++;if(position==length)return false;position++;return true;}
                    public int Current{get{reads++;return values[position-1];}}
                    ~Iterator(){ends++;}
                }
                public Iterator GetEnumerator()=>new Iterator(values,length);
            }
            int SelectKey(int value){selections++;return value%7;}
            int Digit(char value)nothrow{if(value<='9')return value-'0';return value-'A'+10;}
            int ReadByte(readonly(char)* text,ref nuint at)nothrow{int value=(Digit(text[at])<<4)|Digit(text[at+1]);at+=2;return value;}
            int ReadInt(readonly(char)* text,ref nuint at)nothrow{uint value=0u;for(int shift=0;shift<32;shift+=8)value=value|((uint)ReadByte(text,ref at)<<shift);return (int)value;}
            int main(){readonly(char)* data="{{{hex}}}";nuint at=0;
                for(int row=0;row<{{{samples.Count}}};row++){
                    nuint length=(nuint)ReadByte(data,ref at);nuint expectedCount=(nuint)ReadByte(data,ref at);int[40] input;for(nuint i=0;i<length;i++)input[i]=ReadInt(data,ref at);
                    moves=0;reads=0;selections=0;int before=attempts;deny=length==0;
                    {
                        var query=new Enumerable<int,Source,Source::Iterator>(new Source(&input[0],length));
                        HashMap<int,nuint> counts=query.CountBy(&SelectKey);deny=false;
                        if(counts.Count!=expectedCount||reads!=(int)length||selections!=(int)length||moves!=(int)length+1||starts!=ends)return 1;
                        if(length==0&&attempts!=before)return 2;
                        for(nuint i=0;i<expectedCount;i++){int key=ReadInt(data,ref at);nuint count=(nuint)ReadByte(data,ref at);nuint*? found=counts.Find(&key);if(found==null||*found! !=count)return 3;}
                    }
                    if(allocated!=released)return 4;
                }
                if(at!={{{hex.Length}}})return 5;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OwningStringAndCollidingOwnedKeysRemainIndependentAndDuplicatesReleaseTemporaryKeys(int optimization)
        =>Run(Allocator+"""
            int keys=0;int copies=0;int selected=0;
            struct Key:IHashable,IEquatable<Key>{
                public String Text;public int First;
                public Key(String text,int position){Text=move text;First=position;keys++;}
                public copy Key(immutable(Key)* other){Text=other.Text;First=other.First;keys++;copies++;}
                public immutable nuint GetHashCode()=>0;
                public immutable bool Equals(immutable(Key)* other)=>Text.Equals(&other.Text);
                ~Key(){keys--;}
            }
            String CopyWord(String value)=>move value;
            Key SelectKey(String value){int position=selected;selected++;return new Key(move value,position);}
            int main(){
                {
                    Vector<String> words=new();words.Add(s"alpha");words.Add(s"beta");words.Add(s"alpha");words.Add(s"beta");words.Add(new String());
                    char[3] bytes=['a','\0',(char)(byte)255];words.Add(String::From(bytes,3));words.Add(String::From(bytes,3));
                    HashMap<String,nuint> plain=words.AsEnumerable().CountBy(&CopyWord);
                    HashMap<Key,nuint> collisions=words.AsEnumerable().CountBy(&SelectKey);
                    if(plain.Count!=4||collisions.Count!=4||selected!=7||keys!=4||copies!=0)return 1;
                    words[0].Append("changed",7);words.Clear();
                    String alpha=s"alpha";String beta=s"beta";String empty=new();String binary=String::From(bytes,3);
                    if(plain[&alpha]!=2||plain[&beta]!=2||plain[&empty]!=1||plain[&binary]!=2)return 2;
                    Key a=new(s"alpha",99);Key b=new(s"beta",99);Key e=new(new String(),99);Key raw=new(String::From(bytes,3),99);
                    if(collisions[&a]!=2||collisions[&b]!=2||collisions[&e]!=1||collisions[&raw]!=2)return 3;
                    var iterator=collisions.GetEnumerator();nuint total=0;
                    while(iterator.MoveNext()){
                        var entry=iterator.Current;total+=*entry.ValueImmutable;
                        if(entry.Key.Text.Equals("alpha")){if(entry.Key.First!=0)return 4;}
                        else if(entry.Key.Text.Equals("beta")){if(entry.Key.First!=1)return 5;}
                        else if(entry.Key.Text.IsEmpty){if(entry.Key.First!=4)return 6;}
                        else if(!entry.Key.Text.Equals(&binary)||entry.Key.First!=5)return 7;
                    }
                    if(total!=7||copies!=0||keys!=8)return 8;
                }
                if(keys!=0||allocated!=released)return 9;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CallbackIteratorAndEveryMeasuredAllocationFailureReleasePartialMapsAndPreserveAssignedTarget(int optimization)
        =>Run(Allocator+"""
            int mode=0;int keys=0;int starts=0;int ends=0;int selections=0;
            struct Key:IHashable,IEquatable<Key>{
                public int Id;private String token;public Key(int id){Id=id;token=s"key";keys++;}
                public immutable nuint GetHashCode(){if(mode==5&&Id==2)throw new Exception("hash",75);return 0;}
                public immutable bool Equals(immutable(Key)* other){if(mode==6&&(Id==2||other.Id==2))throw new Exception("equal",76);return Id==other.Id;}
                ~Key(){keys--;}
            }
            struct Source{
                public struct Iterator{
                    private int position;private String token;
                    public Iterator(){position=0;token=s"iterator";if(mode==1)throw new Exception("constructor",71);starts++;}
                    public bool MoveNext(){if(mode==2&&position==1)throw new Exception("advance",72);if(position==24)return false;position++;return true;}
                    public int Current{get{if(mode==3&&position==2)throw new Exception("current",73);return position%9;}}
                    ~Iterator(){ends++;}
                }
                public Iterator GetEnumerator()=>new Iterator();
            }
            Key SelectKey(int value){selections++;if(mode==4&&selections==3)throw new Exception("selector",74);return new Key(value);}
            int main(){
                {
                    HashMap<Key,nuint> target=new();target.Add(new Key(99),9);Key lookup=new(99);nuint*? old=target.Find(&lookup);nuint capacity=target.Capacity;
                    var query=new Enumerable<int,Source,Source::Iterator>(new Source());
                    int live=allocated-released;int initialKeys=keys;int before=attempts;int points=0;selections=0;
                    {HashMap<Key,nuint> probe=query.CountBy(&SelectKey);if(probe.Count!=9||selections!=24)return 1;points=attempts-before;}
                    if(points==0||keys!=initialKeys||allocated-released!=live||starts!=ends)return 2;
                    for(int point=0;point<points;point++){
                        int deniedBefore=denied;live=allocated-released;failAfter=point;bool caught=false;selections=0;
                        try{target=query.CountBy(&SelectKey);}catch(ref BadAllocException error){caught=true;}failAfter=-1;
                        if(!caught||denied!=deniedBefore+1||keys!=initialKeys||allocated-released!=live||starts!=ends)return 3;
                        if(target.Count!=1||target.Capacity!=capacity||target.Find(&lookup)!=old||*old! !=9)return 4;
                    }
                    for(int failure=1;failure<=6;failure++){
                        live=allocated-released;mode=failure;selections=0;bool caught=false;
                        try{target=query.CountBy(&SelectKey);}catch(ref Exception error){if(error.GetCode()!=70+failure)return 5;caught=true;}mode=0;
                        if(!caught||keys!=initialKeys||allocated-released!=live||starts!=ends)return 6;
                        if(target.Count!=1||target.Capacity!=capacity||target.Find(&lookup)!=old||*old! !=9)return 7;
                    }
                    target=query.CountBy(&SelectKey);if(target.Count!=9||starts!=ends)return 8;
                }
                if(keys!=0||allocated!=released)return 9;return 0;
            }
            """,optimization);

    private static IEnumerable<SourceFile> Sources(string program)
        =>Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Standard"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("query-count-by.gf",program));
    private static void Run(string program,int optimization)
    {
        var result=CompilerTestHelper.Run(Sources(program),optimization);Assert.True(result.ExitCode==0,$"CountBy exit {result.ExitCode}: {result.StandardError}");
    }
}
