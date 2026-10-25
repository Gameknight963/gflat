using System.Text;

namespace gflat.Tests;

public sealed class StandardQueryToLookupTests
{
    private const string Allocator="""
        using std;extern void*? malloc(nuint count);extern void free(void* storage);
        int attempts=0;int allocated=0;int released=0;int denied=0;int failAfter=-1;bool deny=false;
        namespace Memory{
            public replace void*? Allocate(nuint count){attempts++;if(deny){denied++;return null;}if(failAfter==0){failAfter=-1;denied++;return null;}if(failAfter>0)failAfter--;void*? result=malloc(count);if(result!=null)allocated++;return result;}
            public replace void Free(void* storage)nothrow{released++;free(storage);}
        }
        """;
    private const string Owned="""
        int mode=0;int owners=0;int keys=0;int values=0;int copies=0;int starts=0;int ends=0;int keyCalls=0;int valueCalls=0;
        struct Owner{
            public int Id;private String token;public Owner(int id){Id=id;token=s"owner";owners++;}
            public copy Owner(immutable(Owner)* other){Id=other.Id;token=other.token;if(mode==8&&Id==6)throw new Exception("copy",78);owners++;copies++;}
            ~Owner(){owners--;}
        }
        struct Key:IHashable,IEquatable<Key>{
            public int Id;public int First;private String token;
            public Key(int id,int first){Id=id;First=first;token=s"key";keys++;}
            public immutable nuint GetHashCode(){if(mode==6&&Id==3)throw new Exception("hash",76);return 0;}
            public immutable bool Equals(immutable(Key)* other){if(mode==7&&(Id==3||other.Id==3))throw new Exception("equal",77);return Id==other.Id;}
            ~Key(){keys--;}
        }
        struct Value{
            public int Id;private String token;public Value(int id){Id=id;token=String::From("v\0x",3);values++;}
            public immutable bool Valid()=>token.Length==3&&token.Data[0]=='v'&&token.Data[1]=='\0'&&token.Data[2]=='x';
            ~Value(){values--;}
        }
        struct Source{
            public struct Iterator{
                private int position;private String token;
                public Iterator(){position=0;token=s"iterator";if(mode==1)throw new Exception("constructor",71);starts++;}
                public bool MoveNext(){if(mode==2&&position==2)throw new Exception("advance",72);if(position==24)return false;position++;return true;}
                public Owner Current{get{if(mode==3&&position==3)throw new Exception("current",73);return new Owner(position);}}
                ~Iterator(){ends++;}
            }
            public Iterator GetEnumerator()=>new Iterator();
        }
        Key MakeKey(Owner current){keyCalls++;int id=current.Id;if(mode==4&&id==3)throw new Exception("key",74);current.Id=-1;return new Key(id%5,id);}
        Value MakeValue(Owner current){valueCalls++;if(mode==5&&current.Id==3)throw new Exception("value",75);return new Value(current.Id*10);}
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void GroupByOraclesKeepVectorEncounterOrderAndInvokeCurrentAndSelectorsExactlyOnce(int optimization)
    {
        List<int[]> samples=[[],[0],[1,1,1],[-1,0,1,int.MinValue,int.MaxValue]];var random=new Random(675239);
        for(int sample=0;sample<40;sample++)samples.Add(Enumerable.Range(0,sample%33).Select(_=>random.Next(-1000,1001)).ToArray());
        using var bytes=new MemoryStream();using(var writer=new BinaryWriter(bytes,Encoding.UTF8,true))foreach(var input in samples){
            var expected=input.GroupBy(value=>value%7).ToArray();writer.Write((byte)input.Length);writer.Write((byte)expected.Length);
            foreach(int value in input)writer.Write(value);
            foreach(var group in expected){writer.Write(group.Key);writer.Write((byte)group.Count());foreach(int value in group){writer.Write(value);writer.Write(value/3);}}
        }
        string hex=Convert.ToHexString(bytes.ToArray());
        Run(Allocator+$$$"""
            int starts=0;int ends=0;int moves=0;int reads=0;int keyCalls=0;int valueCalls=0;int phase=0;bool projected=false;bool bad=false;
            struct Source{
                private int* data;private nuint count;public Source(int* input,nuint length){data=input;count=length;}
                public struct Iterator{
                    private int* data;private nuint count;private nuint position;
                    public Iterator(int* input,nuint length){data=input;count=length;position=0;starts++;}
                    public bool MoveNext(){moves++;if(position==count)return false;position++;return true;}
                    public int Current{get{reads++;return data[position-1];}}
                    ~Iterator(){ends++;}
                }
                public Iterator GetEnumerator()=>new Iterator(data,count);
            }
            int SelectKey(int value){keyCalls++;if(projected){if(phase!=0)bad=true;phase=1;}return value%7;}
            int SelectValue(int value){valueCalls++;if(phase!=1)bad=true;phase=0;return value/3;}
            int Digit(char value)nothrow{if(value<='9')return value-'0';return value-'A'+10;}
            int ReadByte(readonly(char)* data,ref nuint at)nothrow{int value=(Digit(data[at])<<4)|Digit(data[at+1]);at+=2;return value;}
            int ReadInt(readonly(char)* data,ref nuint at)nothrow{uint value=0u;for(int shift=0;shift<32;shift+=8)value=value|((uint)ReadByte(data,ref at)<<shift);return (int)value;}
            int main(){readonly(char)* packet="{{{hex}}}";nuint at=0;
                for(int row=0;row<{{{samples.Count}}};row++){
                    nuint length=(nuint)ReadByte(packet,ref at);nuint groupCount=(nuint)ReadByte(packet,ref at);int[40] input;for(nuint i=0;i<length;i++)input[i]=ReadInt(packet,ref at);
                    var query=new Enumerable<int,Source,Source::Iterator>(new Source(&input[0],length));int before=attempts;deny=length==0;
                    {
                        moves=0;reads=0;keyCalls=0;valueCalls=0;projected=false;
                        HashMap<int,Vector<int>> originals=query.ToLookup(&SelectKey);
                        if(originals.Count!=groupCount||moves!=(int)length+1||reads!=(int)length||keyCalls!=(int)length||valueCalls!=0||starts!=ends)return 1;
                        moves=0;reads=0;keyCalls=0;valueCalls=0;projected=true;phase=0;bad=false;
                        HashMap<int,Vector<int>> projections=query.ToLookup(&SelectKey,&SelectValue);
                        if(projections.Count!=groupCount||moves!=(int)length+1||reads!=(int)length||keyCalls!=(int)length||valueCalls!=(int)length||phase!=0||bad||starts!=ends)return 2;
                        deny=false;if(length==0&&attempts!=before)return 3;
                        for(nuint group=0;group<groupCount;group++){
                            int key=ReadInt(packet,ref at);nuint count=(nuint)ReadByte(packet,ref at);Vector<int>*? a=originals.Find(&key);Vector<int>*? b=projections.Find(&key);
                            if(a==null||b==null||a!.Count!=count||b!.Count!=count)return 4;
                            for(nuint i=0;i<count;i++){int original=ReadInt(packet,ref at);int selected=ReadInt(packet,ref at);if((*a!)[i]!=original||(*b!)[i]!=selected)return 5;}
                        }
                    }
                    if(allocated!=released)return 6;
                }
                if(at!={{{hex.Length}}})return 7;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CollidingOwnedKeysRetainFirstRepresentativesAndMoveOnlySelectedValuesPreserveOrder(int optimization)
        =>Run(Allocator+Owned+"""
            int main(){
                {
                    var query=new Enumerable<Owner,Source,Source::Iterator>(new Source());
                    HashMap<Key,Vector<Owner>> originals=query.ToLookup(&MakeKey);
                    if(originals.Count!=5||owners!=24||keys!=5||values!=0||keyCalls!=24||valueCalls!=0||starts!=ends)return 1;
                    keyCalls=0;valueCalls=0;HashMap<Key,Vector<Value>> projected=query.ToLookup(&MakeKey,&MakeValue);
                    if(projected.Count!=5||owners!=24||keys!=10||values!=24||keyCalls!=24||valueCalls!=24||starts!=ends||copies==0)return 2;
                    for(int group=0;group<5;group++){
                        Key lookup=new(group,99);Vector<Owner>*? a=originals.Find(&lookup);Vector<Value>*? b=projected.Find(&lookup);if(a==null||b==null)return 3;
                        Span<Owner> indexedOwners=originals[&lookup].AsSpan();Span<Value> indexedValues=projected[&lookup].AsSpan();
                        Span<Owner> foundOwners=a!.AsSpan();Span<Value> foundValues=b!.AsSpan();
                        if(indexedOwners.Data!=foundOwners.Data||indexedValues.Data!=foundValues.Data||indexedOwners.Length!=a!.Count||indexedValues.Length!=b!.Count)return 8;
                        nuint index=0;for(int id=1;id<=24;id++)if(id%5==group){if(index>=a!.Count||index>=b!.Count||(*a!)[index].Id!=id||(*b!)[index].Id!=id*10||!(*b!)[index].Valid())return 4;index++;}
                        if(a!.Count!=index||b!.Count!=index)return 5;
                    }
                    var iterator=projected.GetEnumerator();while(iterator.MoveNext()){
                        var entry=iterator.Current;int expectedFirst=entry.Key.Id;if(expectedFirst==0)expectedFirst=5;if(entry.Key.First!=expectedFirst)return 6;
                    }
                }
                if(owners!=0||keys!=0||values!=0||allocated!=released)return 7;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryMeasuredGroupAndMapAllocationFailureAndCallbackFailureCleanUpAndPreserveOldTargets(int optimization)
        =>Run(Allocator+Owned+"""
            int main(){
                {
                    HashMap<Key,Vector<Owner>> originals=new();Vector<Owner> oldOwners=new();oldOwners.Add(new Owner(99));originals.Add(new Key(99,99),move oldOwners);
                    HashMap<Key,Vector<Value>> projected=new();Vector<Value> oldValues=new();oldValues.Add(new Value(990));projected.Add(new Key(99,99),move oldValues);Key lookup=new(99,99);
                    Vector<Owner>*? oldOriginal=originals.Find(&lookup);Vector<Value>*? oldValue=projected.Find(&lookup);
                    Span<Owner> oldOwnerStorage=oldOriginal!.AsSpan();Span<Value> oldValueStorage=oldValue!.AsSpan();
                    nuint originalCapacity=originals.Capacity;nuint valueCapacity=projected.Capacity;
                    var query=new Enumerable<Owner,Source,Source::Iterator>(new Source());
                    for(int which=0;which<2;which++){
                        int before=attempts;int live=allocated-released;int points=0;
                        if(which==0){HashMap<Key,Vector<Owner>> probe=query.ToLookup(&MakeKey);points=attempts-before;}
                        else{HashMap<Key,Vector<Value>> probe=query.ToLookup(&MakeKey,&MakeValue);points=attempts-before;}
                        if(points==0||owners!=1||keys!=3||values!=1||starts!=ends||allocated-released!=live)return 1;
                        for(int point=0;point<points;point++){
                            live=allocated-released;int deniedBefore=denied;failAfter=point;bool caught=false;
                            try{if(which==0)originals=query.ToLookup(&MakeKey);else projected=query.ToLookup(&MakeKey,&MakeValue);}catch(ref BadAllocException error){caught=true;}failAfter=-1;
                            if(!caught||denied!=deniedBefore+1||owners!=1||keys!=3||values!=1||starts!=ends||allocated-released!=live)return 2;
                            if(originals.Count!=1||projected.Count!=1||originals.Capacity!=originalCapacity||projected.Capacity!=valueCapacity||originals.Find(&lookup)!=oldOriginal||projected.Find(&lookup)!=oldValue)return 3;
                            Span<Owner> ownerStorage=oldOriginal!.AsSpan();Span<Value> valueStorage=oldValue!.AsSpan();
                            if(ownerStorage.Data!=oldOwnerStorage.Data||valueStorage.Data!=oldValueStorage.Data||oldOriginal!.Count!=1||oldValue!.Count!=1||(*oldOriginal!)[0].Id!=99||(*oldValue!)[0].Id!=990)return 4;
                        }
                        for(int failure=1;failure<=8;failure++){
                            if(which==0&&failure==5)continue;
                            mode=failure;live=allocated-released;bool caught=false;
                            try{if(which==0)originals=query.ToLookup(&MakeKey);else projected=query.ToLookup(&MakeKey,&MakeValue);}catch(ref Exception error){if(error.GetCode()!=70+failure)return 5;caught=true;}mode=0;
                            if(!caught||owners!=1||keys!=3||values!=1||starts!=ends||allocated-released!=live)return 6;
                            if(originals.Count!=1||projected.Count!=1||originals.Find(&lookup)!=oldOriginal||projected.Find(&lookup)!=oldValue||(*oldOriginal!)[0].Id!=99||(*oldValue!)[0].Id!=990)return 7;
                        }
                    }
                }
                if(owners!=0||keys!=0||values!=0||allocated!=released)return 8;return 0;
            }
            """,optimization);

    private static IEnumerable<SourceFile> Sources(string program)
        =>Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("query-to-lookup.gf",program));
    private static void Run(string program,int optimization)
    {
        var result=CompilerTestHelper.Run(Sources(program),optimization);Assert.True(result.ExitCode==0,$"ToLookup exit {result.ExitCode}: {result.StandardError}");
    }
}
