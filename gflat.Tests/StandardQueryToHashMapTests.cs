using System.Text;

namespace gflat.Tests;

public sealed class StandardQueryToHashMapTests
{
    private const string Allocator="""
        using std; using std::Text; using std::Queries; using std::Collections;extern void*? malloc(nuint count);extern void free(void* storage);
        int attempts=0;int allocated=0;int released=0;int denied=0;int failAfter=-1;bool deny=false;
        namespace Memory{
            public replace void*? Allocate(nuint count){attempts++;if(deny){denied++;return null;}if(failAfter==0){failAfter=-1;denied++;return null;}if(failAfter>0)failAfter--;void*? result=malloc(count);if(result!=null)allocated++;return result;}
            public replace void Free(void* storage)nothrow{released++;free(storage);}
        }
        """;
    private const string Owned="""
        int mode=0;int owners=0;int keys=0;int values=0;int copies=0;int starts=0;int ends=0;int keyCalls=0;int valueCalls=0;
        struct Owner{
            public int Id;private String token;
            public Owner(int id){Id=id;token=s"owner";owners++;}
            public copy Owner(immutable(Owner)* other){Id=other.Id;token=other.token;owners++;copies++;}
            ~Owner(){owners--;}
        }
        struct Key:IHashable,IEquatable<Key>{
            public int Id;private String token;public Key(int id){Id=id;token=s"key";keys++;}
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
                public bool MoveNext(){if(mode==2&&position==2)throw new Exception("advance",72);if(position==12)return false;position++;return true;}
                public Owner Current{get{if(mode==3&&position==3)throw new Exception("current",73);int id=position;if(mode==8&&position==3)id=1;return new Owner(id);}}
                ~Iterator(){ends++;}
            }
            public Iterator GetEnumerator()=>new Iterator();
        }
        Key MakeKey(Owner current){keyCalls++;int id=current.Id;if(mode==4&&id==3)throw new Exception("key",74);current.Id=-1;return new Key(id);}
        Value MakeValue(Owner current){valueCalls++;if(mode==5&&current.Id==3)throw new Exception("value",75);return new Value(current.Id*10);}
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SeededDictionaryOraclesVerifyOneCurrentAndOrderedSelectorsAndAllocationFreeEmptyResults(int optimization)
    {
        List<int[]> samples=[[],[0],[-1,0,1]];var random=new Random(179653);
        for(int sample=0;sample<40;sample++)samples.Add(Enumerable.Range(0,sample%25).Select(_=>random.Next(-1000,1001)).Distinct().ToArray());
        using var bytes=new MemoryStream();using(var writer=new BinaryWriter(bytes,Encoding.UTF8,true))foreach(var input in samples){
            var originals=input.ToDictionary(value=>value*3+1);var projected=input.ToDictionary(value=>value*3+1,value=>value*value-17);
            writer.Write((byte)input.Length);foreach(int value in input)writer.Write(value);
            foreach(var pair in originals){writer.Write(pair.Key);writer.Write(pair.Value);writer.Write(projected[pair.Key]);}
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
            int SelectKey(int value){keyCalls++;if(projected){if(phase!=0)bad=true;phase=1;}return value*3+1;}
            int SelectValue(int value){valueCalls++;if(phase!=1)bad=true;phase=0;return value*value-17;}
            int Digit(char value)nothrow{if(value<='9')return value-'0';return value-'A'+10;}
            int ReadByte(readonly(char)* data,ref nuint at)nothrow{int value=(Digit(data[at])<<4)|Digit(data[at+1]);at+=2;return value;}
            int ReadInt(readonly(char)* data,ref nuint at)nothrow{uint value=0u;for(int shift=0;shift<32;shift+=8)value=value|((uint)ReadByte(data,ref at)<<shift);return (int)value;}
            int main(){readonly(char)* packet="{{{hex}}}";nuint at=0;
                for(int row=0;row<{{{samples.Count}}};row++){
                    nuint length=(nuint)ReadByte(packet,ref at);int[32] input;for(nuint i=0;i<length;i++)input[i]=ReadInt(packet,ref at);
                    var query=new Enumerable<int,Source,Source::Iterator>(new Source(&input[0],length));int before=attempts;deny=length==0;
                    {
                        moves=0;reads=0;keyCalls=0;valueCalls=0;projected=false;
                        HashMap<int,int> originals=query.ToHashMap(&SelectKey);
                        if(originals.Count!=length||moves!=(int)length+1||reads!=(int)length||keyCalls!=(int)length||valueCalls!=0||starts!=ends)return 1;
                        moves=0;reads=0;keyCalls=0;valueCalls=0;projected=true;phase=0;bad=false;
                        HashMap<int,int> projections=query.ToHashMap(&SelectKey,&SelectValue);
                        if(projections.Count!=length||moves!=(int)length+1||reads!=(int)length||keyCalls!=(int)length||valueCalls!=(int)length||phase!=0||bad||starts!=ends)return 2;
                        deny=false;if(length==0&&attempts!=before)return 3;
                        for(nuint i=0;i<length;i++){int key=ReadInt(packet,ref at);int original=ReadInt(packet,ref at);int value=ReadInt(packet,ref at);if(originals[&key]!=original||projections[&key]!=value)return 4;}
                    }
                    if(allocated!=released)return 5;
                }
                if(at!={{{hex.Length}}})return 6;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void StoredOriginalsAndSelectedValuesOwnTheirResourcesWhileSelectorArgumentsRemainOrdinaryCopies(int optimization)
        =>Run(Allocator+Owned+"""
            int main(){
                {
                    var query=new Enumerable<Owner,Source,Source::Iterator>(new Source());
                    HashMap<Key,Owner> originals=query.ToHashMap(&MakeKey);
                    if(originals.Count!=12||owners!=12||keys!=12||values!=0||keyCalls!=12||valueCalls!=0||starts!=ends)return 1;
                    keyCalls=0;valueCalls=0;HashMap<Key,Value> projected=query.ToHashMap(&MakeKey,&MakeValue);
                    if(projected.Count!=12||owners!=12||keys!=24||values!=12||keyCalls!=12||valueCalls!=12||starts!=ends||copies==0)return 2;
                    for(int id=1;id<=12;id++){
                        Key lookup=new(id);Owner*? original=originals.Find(&lookup);Value*? selected=projected.Find(&lookup);
                        if(original==null||selected==null||original!.Id!=id||selected!.Id!=id*10||!selected!.Valid())return 3;
                    }
                    // The key selector mutates its argument copy; the original stored T,
                    // and the independent argument passed to valueSelector, retain Id.
                }
                if(owners!=0||keys!=0||values!=0||allocated!=released)return 4;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DuplicateCallbackAndMeasuredAllocationFailuresCleanPartialMapsAndPreserveOldAssignments(int optimization)
        =>Run(Allocator+Owned+"""
            int main(){
                {
                    HashMap<Key,Owner> originals=new();originals.Add(new Key(99),new Owner(99));
                    HashMap<Key,Value> projected=new();projected.Add(new Key(99),new Value(990));Key lookup=new(99);
                    Owner*? oldOriginal=originals.Find(&lookup);Value*? oldValue=projected.Find(&lookup);
                    nuint originalCapacity=originals.Capacity;nuint valueCapacity=projected.Capacity;
                    var query=new Enumerable<Owner,Source,Source::Iterator>(new Source());
                    for(int which=0;which<2;which++){
                        int before=attempts;int live=allocated-released;int points=0;
                        if(which==0){HashMap<Key,Owner> probe=query.ToHashMap(&MakeKey);points=attempts-before;}
                        else{HashMap<Key,Value> probe=query.ToHashMap(&MakeKey,&MakeValue);points=attempts-before;}
                        if(points==0||owners!=1||keys!=3||values!=1||starts!=ends||allocated-released!=live)return 1;
                        for(int point=0;point<points;point++){
                            live=allocated-released;int deniedBefore=denied;failAfter=point;bool caught=false;
                            try{if(which==0)originals=query.ToHashMap(&MakeKey);else projected=query.ToHashMap(&MakeKey,&MakeValue);}catch(ref BadAllocException error){caught=true;}failAfter=-1;
                            if(!caught||denied!=deniedBefore+1||owners!=1||keys!=3||values!=1||starts!=ends||allocated-released!=live)return 2;
                            if(originals.Count!=1||projected.Count!=1||originals.Capacity!=originalCapacity||projected.Capacity!=valueCapacity||originals.Find(&lookup)!=oldOriginal||projected.Find(&lookup)!=oldValue||oldOriginal!.Id!=99||oldValue!.Id!=990)return 3;
                        }
                        for(int failure=1;failure<=8;failure++){
                            if(which==0&&failure==5)continue;
                            mode=failure;keyCalls=0;valueCalls=0;live=allocated-released;bool caught=false;
                            try{if(which==0)originals=query.ToHashMap(&MakeKey);else projected=query.ToHashMap(&MakeKey,&MakeValue);}
                            catch(ref Exception error){caught=true;if(failure==8){if(!CString::Equals(error.GetMessage(),"Duplicate HashMap key"))return 4;}else if(error.GetCode()!=70+failure)return 5;}mode=0;
                            if(!caught||owners!=1||keys!=3||values!=1||starts!=ends||allocated-released!=live)return 6;
                            if(failure==8){if(keyCalls!=3)return 7;if(which==1&&valueCalls!=3)return 8;}
                            if(originals.Count!=1||projected.Count!=1||originals.Capacity!=originalCapacity||projected.Capacity!=valueCapacity||originals.Find(&lookup)!=oldOriginal||projected.Find(&lookup)!=oldValue||oldOriginal!.Id!=99||oldValue!.Id!=990)return 9;
                        }
                    }
                }
                if(owners!=0||keys!=0||values!=0||allocated!=released)return 10;return 0;
            }
            """,optimization);

    private static IEnumerable<SourceFile> Sources(string program)
        =>Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("query-to-hash-map.gf",program));
    private static void Run(string program,int optimization)
    {
        var result=CompilerTestHelper.Run(Sources(program),optimization);Assert.True(result.ExitCode==0,$"ToHashMap exit {result.ExitCode}: {result.StandardError}");
    }
}
