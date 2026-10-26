namespace gflat.Tests;

public sealed class StandardMapRemoveOutputTests
{
    private const string Owners="""
        using std; using std::Text; using std::Collections;extern void*? malloc(nuint bytes);extern void free(void* storage);
        int allocated=0;int released=0;int attempts=0;bool deny=false;
        bool capture=false;void*? backing=null;nuint backingBytes=0;void*? watchedStorage=null;int watchedFrees=0;
        int madeKeys=0;int deadKeys=0;int madeValues=0;int deadValues=0;int hashCalls=0;int equalCalls=0;int compareCalls=0;
        int hashBudget=-1;int equalBudget=-1;int compareBudget=-1;
        bool observe=false;nuint expectedCount=0;int badCount=0;
        HashMap<Key,Value>*? watchedHash=null;SortedMap<Key,Value>*? watchedSorted=null;
        namespace Memory {
            public replace void*? Allocate(nuint bytes) {
                attempts++;if(deny)return null;void*? result=malloc(bytes);
                if(result!=null){allocated++;if(capture){backing=result;backingBytes=bytes;}}return result;
            }
            public replace void Free(void* storage) nothrow {
                if(storage==watchedStorage)watchedFrees++;released++;free(storage);
            }
        }
        void CheckCount() {
            if(!observe)return;
            nuint actual=0;if(watchedHash!=null)actual=watchedHash!.Count;else actual=watchedSorted!.Count;
            if(actual!=expectedCount)badCount++;
        }
        struct Key:IHashable,IEquatable<Key> {
            public int Id;public Key(int id){Id=id;madeKeys++;}
            ~Key(){CheckCount();deadKeys++;}
            public immutable nuint GetHashCode() {
                hashCalls++;if(hashBudget==0)throw new Exception("hash",71);if(hashBudget>0)hashBudget--;return 1;
            }
            public immutable bool Equals(immutable(Key)* other) {
                equalCalls++;if(equalBudget==0)throw new Exception("equality",72);if(equalBudget>0)equalBudget--;return Id==other.Id;
            }
        }
        struct Value {
            public Key Lookup;public int Id;public String Text;
            public Value(int id,int lookup){Lookup=new Key(lookup);Id=id;Text=s"value";madeValues++;}
            ~Value(){CheckCount();deadValues++;}
        }
        int Compare(immutable(Key)* first,immutable(Key)* second) {
            compareCalls++;if(compareBudget==0)throw new Exception("compare",73);if(compareBudget>0)compareBudget--;
            if(first.Id<second.Id)return -1;if(first.Id>second.Id)return 1;return 0;
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NoncopyableValueTransfersOnceWithBorrowedOldOutputKeyAndCommittedCountBeforeCleanup(int optimization)
    {
        foreach(bool sorted in new[]{false,true})Run(Owners+Template("""
            int main() {
                {
                    Value output=new(900,3);
                    {
                        $MAP$<Key,Value> map=$NEW$;map.Reserve(8);
                        for(int id=1;id<=5;id++)map.Add(new Key(id),new Value(id*10,id));
                        Value* original=map.Find(&output.Lookup)!;readonly(char)* identity=original.Text.Data;
                        hashCalls=0;equalCalls=0;compareCalls=0;map.Find(&output.Lookup);
                        int expectedHash=hashCalls;int expectedEqual=equalCalls;int expectedCompare=compareCalls;
                        hashCalls=0;equalCalls=0;compareCalls=0;
                        int before=attempts;int keys=deadKeys;int values=deadValues;
                        watchedStorage=(void*)output.Text.Data;watchedFrees=0;
                        $WATCH$=&map;expectedCount=4;observe=true;deny=true;
                        bool removed=map.Remove(&output.Lookup,ref output);
                        deny=false;observe=false;$WATCH$=null;watchedStorage=null;
                        if(!removed||output.Id!=30||output.Text.Data!=identity||map.Count!=4)return 1;
                        if(hashCalls!=expectedHash||equalCalls!=expectedEqual||compareCalls!=expectedCompare)return 2;
                        if(attempts!=before||deadKeys!=keys+2||deadValues!=values+1||watchedFrees!=1||badCount!=0)return 3;
                        before=attempts;identity=output.Text.Data;keys=deadKeys;values=deadValues;deny=true;
                        if(map.Remove(&output.Lookup,ref output)||map.Count!=4||output.Id!=30||output.Text.Data!=identity)return 4;
                        deny=false;if(attempts!=before||deadKeys!=keys||deadValues!=values)return 5;
                        map.Add(new Key(3),new Value(300,3));map.Reserve(40);
                        for(int id=1;id<=5;id++) {
                            Key query=new(id);Value* found=map.Find(&query)!;int expected=id*10;if(id==3)expected=300;
                            if(found.Id!=expected)return 6;
                        }
                        if(!map.Remove(&output.Lookup)||map.Count!=4||output.Id!=30)return 7;
                        map.Clear();
                        if(output.Text.Data!=identity||output.Id!=30||output.Text.Length!=5)return 8;
                    }
                    if(output.Id!=30||output.Text.Length!=5)return 9;
                }
                if(madeKeys!=deadKeys||madeValues!=deadValues||allocated!=released)return 10;return 0;
            }
            """,sorted),optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ThrowingLookupsAndLiveOrConstructedSpareOutputAliasesLeaveAllOwnersUnchanged(int optimization)
    {
        foreach(bool sorted in new[]{false,true}) {
            string failures=sorted?"""
                for(int point=0;point<expectedCompare;point++) {
                    compareBudget=point;bool caught=false;
                    try{map.Remove(&output.Lookup,ref output);}catch(ref Exception error){if(error.GetCode()!=73)return 1;caught=true;}
                    compareBudget=-1;if(!caught||map.Count!=3||output.Id!=900||output.Text.Data!=identity||deadValues!=values||deadKeys!=keys)return 2;
                }
                """:"""
                hashBudget=0;bool hashCaught=false;
                try{map.Remove(&output.Lookup,ref output);}catch(ref Exception error){if(error.GetCode()!=71)return 1;hashCaught=true;}
                hashBudget=-1;if(!hashCaught||map.Count!=3||output.Id!=900||output.Text.Data!=identity||deadValues!=values||deadKeys!=keys)return 2;
                for(int point=0;point<expectedEqual;point++) {
                    equalBudget=point;bool caught=false;
                    try{map.Remove(&output.Lookup,ref output);}catch(ref Exception error){if(error.GetCode()!=72)return 3;caught=true;}
                    equalBudget=-1;if(!caught||map.Count!=3||output.Id!=900||output.Text.Data!=identity||deadValues!=values||deadKeys!=keys)return 4;
                }
                """;
            Run(Owners+Template("""
                int main() {
                    {
                        $MAP$<Key,Value> map=$NEW$;capture=true;map.Reserve(8);capture=false;
                        void* storage=backing!;
                        for(int id=1;id<=3;id++)map.Add(new Key(id),new Value(id*10,id));
                        Value output=new(900,2);readonly(char)* identity=output.Text.Data;int values=deadValues;int keys=deadKeys;
                        hashCalls=0;equalCalls=0;compareCalls=0;map.Find(&output.Lookup);
                        int expectedEqual=equalCalls;int expectedCompare=compareCalls;
                        int balance=allocated-released;
                        $FAILURES$
                        if(allocated-released!=balance)return 5;
                        for(int id=1;id<=3;id++) {
                            Key query=new(id);Value* stored=map.Find(&query)!;bool caught=false;
                            int before=deadValues;int keyBefore=deadKeys;
                            try{map.Remove(&query,ref *stored);}catch(ref Exception error){caught=true;}
                            if(!caught||map.Count!=3||stored.Id!=id*10||deadValues!=before||deadKeys!=keyBefore)return 6;
                        }
                        // Hash bucket zero is unused because every key hashes to1. Sorted slot3 is unused.
                        $MAP$<Key,Value>::Entry* spareEntry=($MAP$<Key,Value>::Entry*)storage;
                        $SPARE_OFFSET$
                        Value* spare=new* Value(777,2) at &spareEntry.Value;
                        int before=deadValues;int keyBefore=deadKeys;bool caught=false;
                        try{map.Remove(&output.Lookup,ref *spare);}catch(ref Exception error){caught=true;}
                        if(!caught||map.Count!=3||spare.Id!=777||deadValues!=before||deadKeys!=keyBefore)return 7;
                        spare.~Value();
                        for(int id=1;id<=3;id++){Key query=new(id);if(map.Find(&query)!.Id!=id*10)return 8;}
                    }
                    if(madeKeys!=deadKeys||madeValues!=deadValues||allocated!=released)return 9;return 0;
                }
                """,sorted).Replace("$FAILURES$",failures).Replace("$SPARE_OFFSET$",sorted?"spareEntry=spareEntry+3;":""),optimization);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ZeroSizedValuesRejectLastFullCapacityAliasAndHashStateStorageAndStillTransferNormally(int optimization)
    {
        foreach(bool sorted in new[]{false,true}) {
            string flags=sorted?"":"""
                {
                // Construct a zero-size owner at the final state byte: no map bytes are overwritten.
                Empty* placed=new* Empty() at (Empty*)((byte*)backing!+backingBytes-1);
                int before=deadEmpty;bool caught=false;
                try{map.Remove(&query,ref *placed);}catch(ref Exception error){caught=true;}
                if(!caught||map.Count!=3||deadEmpty!=before)return 4;
                placed.~Empty();
                }
                """;
            Run(Owners+Template("""
                int madeEmpty=0;int deadEmpty=0;
                struct Empty { public Empty(){madeEmpty++;}~Empty(){deadEmpty++;} }
                int main() {
                    if(sizeof(Empty)!=0)return 1;
                    {
                        $MAP$<Key,Empty> map=$NEW$;capture=true;map.Reserve(3);capture=false;
                        for(int id=1;id<=3;id++)map.Add(new Key(id),new Empty());
                        Key query=new(3);Empty* last=map.Find(&query)!;
                        $LAST_ENDPOINT$
                        {
                            int before=deadEmpty;bool caught=false;
                            try{map.Remove(&query,ref *last);}catch(ref Exception error){caught=true;}
                            if(!caught||map.Count!=3||deadEmpty!=before)return 2;
                        }
                        $FLAGS$
                        Empty output=new();int before=deadEmpty;int requests=attempts;deny=true;
                        if(!map.Remove(&query,ref output)||map.Count!=2||deadEmpty!=before+1||attempts!=requests)return 3;
                        deny=false;
                    }
                    if(madeEmpty!=deadEmpty||madeKeys!=deadKeys||allocated!=released)return 5;return 0;
                }
                """,sorted).Replace("$FLAGS$",flags).Replace("$LAST_ENDPOINT$",sorted?
                    "if((nuint)last!=(nuint)backing!+backingBytes)return 6;":""),optimization);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ManagedRemovalReleasesOldOutputAndRetainsOnlySelectedReferenceWithoutStaleMapRoots(int optimization)
    {
        foreach(bool sorted in new[]{false,true}) {
            string source="""
                using std; using std::Text; using std::Collections;
                public static replace nuint std::TracingCollector::CollectionThreshold()=>0;
                class Box {
                    public Box^? Self;public int Id;
                    public Box(int id){Self=null;Id=id;}
                }
                int Compare(immutable(int)* first,immutable(int)* second) {
                    if(*first<*second)return -1;if(*first>*second)return 1;return 0;
                }
                Box^ Make(int id) {
                    Box^ value=new^ Box(id);value.Self=value;return value;
                }
                void Fill($MAP$<int,Box^>* map) {
                    Box^ selected=Make(42);map.Add(1,selected);
                }
                void RemoveSelected($MAP$<int,Box^>* map,ref Box^ output) {
                    int key=1;
                    if(!map.Remove(&key,ref output)||map.Count!=0||map.ContainsKey(&key))throw new Exception("managed removal failed");
                }
                int Read(Box^* output) {
                    Box^ value=*output;
                    if(value.Self==null||value.Self!.Id!=value.Id)throw new Exception("self reference lost");
                    return value.Id;
                }
                void Replace(ref Box^ output) { output=Make(77); }
                int Exercise() {
                    $MAP$<int,Box^> map=$NEW$;map.Reserve(4);nuint capacity=map.Capacity;
                    Box^ output=Make(99);Fill(&map);
                    if(TracingCollector::ManagedCount!=2||GC::Collect()!=0)return 1;
                    RemoveSelected(&map,ref output);
                    if(map.Count!=0||map.Capacity!=capacity||capacity==0||Read(&output)!=42)return 2;
                    if(GC::Collect()!=1||TracingCollector::ManagedCount!=1)return 3;
                    if(Read(&output)!=42)return 4;
                    Replace(ref output);
                    // Keep the empty map's allocation alive while collecting its former value.
                    // Hash tombstones / Sorted vacated tail must retain neither old managed owner.
                    if(map.Count!=0||map.Capacity!=capacity||Read(&output)!=77)return 5;
                    if(GC::Collect()!=1||TracingCollector::ManagedCount!=1)return 6;
                    if(Read(&output)!=77)return 7;
                    return 0;
                }
                int main() {
                    int result=Exercise();if(result!=0)return result;
                    if(GC::Collect()!=1||TracingCollector::ManagedCount!=0)return 8;
                    return 0;
                }
                """;
            Run(Template(source,sorted),optimization);
        }
    }

    private static string Template(string source,bool sorted)
        =>source.Replace("$MAP$",sorted?"SortedMap":"HashMap").Replace("$NEW$",sorted?"new(Compare)":"new()")
            .Replace("$WATCH$",sorted?"watchedSorted":"watchedHash");
    private static void Run(string program,int optimization)
    {
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("map-remove-output.gf",program));
        var result=CompilerTestHelper.Run(sources,optimization);
        Assert.True(result.ExitCode==0,$"Map Remove output exit {result.ExitCode}: {result.StandardError}");
    }
}
