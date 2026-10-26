namespace gflat.Tests;

public sealed class StandardDefaultIfEmptyTests
{
    private const string Counters="""
        using std; using std::Text; using std::Queries;extern void*? malloc(nuint bytes);extern void free(void* storage);
        int allocated=0;int released=0;int attempts=0;
        namespace Memory {
            public replace void*? Allocate(nuint bytes){attempts++;void*? p=malloc(bytes);if(p!=null)allocated++;return p;}
            public replace void Free(void* storage)nothrow{released++;free(storage);}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void HostLinqCompositionsAndSourceEffectsDistinguishStickyMovesFromRetryableCurrent(int optimization)
    {
        int[][] oracle=Enumerable.Range(0,6).Select(count=>Enumerable.Range(1,count).Where(value=>value%2==0)
            .DefaultIfEmpty(99).Take(3).Append(101).ToArray()).ToArray();
        int[] expected=oracle.SelectMany(row=>row).ToArray();
        Run(Counters+$$$"""
            int length=0;int starts=0;int ends=0;int moves=0;int reads=0;bool failMove=false;bool failRead=false;
            struct Source {
                public struct Iterator {
                    private int position;
                    public Iterator(){position=0;starts++;}
                    public bool MoveNext(){moves++;if(failMove)throw new Exception("advance",71);if(position==length)return false;position++;return true;}
                    public int Current{get{reads++;if(failRead)throw new Exception("current",72);return position;}}
                    ~Iterator(){ends++;}
                }
                public Iterator GetEnumerator()=>new Iterator();
            }
            bool Even(int value)=>value%2==0;
            void Reset(){starts=0;ends=0;moves=0;reads=0;}
            int main(){
                int[{{{expected.Length}}}] expected=[{{{string.Join(',',expected)}}}];
                int[6] counts=[{{{string.Join(',',oracle.Select(row=>row.Length))}}}];nuint offset=0;
                var source=new Enumerable<int,Source,Source::Iterator>(new Source());
                int before=attempts;
                for(length=0;length<6;length++){
                    var query=source.Where(&Even).DefaultIfEmpty(99).Take(3).Append(101);
                    {
                        var first=query.GetEnumerator();var second=query.GetEnumerator();
                        for(int index=0;index<counts[length];index++){
                            if(!first.MoveNext()||!second.MoveNext()||first.Current!=expected[offset+(nuint)index]||second.Current!=expected[offset+(nuint)index])return 1;
                        }
                        if(first.MoveNext()||first.MoveNext()||second.MoveNext())return 2;
                    }
                    if(starts!=ends||query.Count()!=(nuint)counts[length])return 3;
                    offset+=(nuint)counts[length];
                }
                Reset();length=3;
                if(source.DefaultIfEmpty(99).Take(0).Count()!=0||starts!=1||ends!=1||moves!=0||reads!=0)return 4;
                if(attempts!=before)return 5;
                for(int count=0;count<=3;count+=3){
                    Reset();length=count;var query=source.DefaultIfEmpty(99);
                    if(starts!=0||moves!=0||reads!=0)return 6;
                    {
                        var cursor=query.GetEnumerator();if(starts!=1||moves!=0||reads!=0)return 7;
                        bool caught=false;try{cursor.Current;}catch{caught=true;}if(!caught)return 8;
                        if(!cursor.MoveNext())return 9;
                        int expectedFirst=1;if(count==0)expectedFirst=99;
                        if(cursor.Current!=expectedFirst||cursor.Current!=expectedFirst||moves!=1)return 10;
                        if(count==0){if(reads!=0)return 11;}
                        else{
                            if(reads!=2)return 12;
                            failRead=true;caught=false;try{cursor.Current;}catch(ref Exception error){if(error.GetCode()!=72)return 13;caught=true;}failRead=false;
                            if(!caught||cursor.Current!=1||moves!=1||reads!=4)return 14;
                        }
                        int produced=1;while(cursor.MoveNext()){produced++;if(cursor.Current!=produced)return 15;}
                        int expectedCount=count;if(count==0)expectedCount=1;
                        int expectedMoves=count+1;
                        if(produced!=expectedCount||moves!=expectedMoves||cursor.MoveNext()||cursor.MoveNext()||moves!=expectedMoves)return 16;
                        caught=false;try{cursor.Current;}catch{caught=true;}if(!caught)return 17;
                    }
                    if(starts!=ends)return 18;
                }
                for(int count=0;count<=2;count+=2){
                    Reset();length=count;
                    {
                        var cursor=source.DefaultIfEmpty(99).GetEnumerator();failMove=true;bool caught=false;
                        try{cursor.MoveNext();}catch(ref Exception error){if(error.GetCode()!=71)return 19;caught=true;}failMove=false;
                        int beforeMoves=moves;
                        if(!caught||cursor.MoveNext()||cursor.MoveNext()||moves!=beforeMoves)return 20;
                        caught=false;try{cursor.Current;}catch{caught=true;}if(!caught)return 21;
                    }
                    if(starts!=ends)return 22;
                }
                if(allocated!=released)return 23;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OwningFallbackCopiesStayCapturedForNonemptyEarlyExitAndCopyFailuresReleaseSourceCursor(int optimization)
        =>Run(Counters+"""
            int live=0;int fallbackLive=0;int starts=0;int ends=0;bool failCopy=false;bool failSource=false;bool failIterator=false;
            struct Item {
                public int Id;public String Text;
                public Item(int id,String text){Id=id;Text=move text;live++;if(Id==99)fallbackLive++;}
                public copy Item(immutable(Item)* source){Id=source.Id;Text=source.Text;if(failCopy&&Id==99)throw new Exception("fallback copy",73);live++;if(Id==99)fallbackLive++;}
                ~Item(){live--;if(Id==99)fallbackLive--;}
            }
            struct Source {
                private int count;private String token;
                public Source(int length){count=length;token=s"source";}
                public copy Source(immutable(Source)* other){count=other.count;token=other.token;if(failSource)throw new Exception("source copy",74);}
                public struct Iterator {
                    private int count;private int position;private String token;
                    public Iterator(int length){count=length;position=0;token=s"cursor";if(failIterator)throw new Exception("cursor",75);starts++;}
                    public bool MoveNext(){if(position==count)return false;position++;return true;}
                    public Item Current{get=>new Item(position,s"source item");}
                    ~Iterator(){ends++;}
                }
                public Iterator GetEnumerator()=>new Iterator(count);
            }
            DefaultIfEmptySource<Item,Source,Source::Iterator>::Enumerator Detached(){
                var source=new Enumerable<Item,Source,Source::Iterator>(new Source(0));
                var query=source.DefaultIfEmpty(new Item(99,s"detached"));
                return query.GetEnumerator();
            }
            int main(){
                {
                    Item fallback=new(99,s"a\0b");
                    var source=new Enumerable<Item,Source,Source::Iterator>(new Source(1));
                    var query=source.DefaultIfEmpty(fallback);fallback.Text[0]='X';
                    int owners=live;int captured=fallbackLive;int balance=allocated-released;
                    {
                        var cursor=query.GetEnumerator();if(fallbackLive!=captured+1||!cursor.MoveNext())return 1;
                        {Item value=cursor.Current;if(value.Id!=1||fallbackLive!=captured+1)return 2;}
                        // Nonempty source suppresses fallback emission, not the owned fallback capture.
                        if(fallbackLive!=captured+1)return 3;
                    }
                    if(fallbackLive!=captured||live!=owners||allocated-released!=balance||starts!=ends)return 4;
                    foreach(Item value in query){if(value.Id!=1)return 5;break;}
                    if(fallbackLive!=captured||live!=owners||allocated-released!=balance||starts!=ends)return 6;
                    int beforeStarts=starts;int beforeEnds=ends;failCopy=true;bool caught=false;
                    try{query.GetEnumerator();}catch(ref Exception error){if(error.GetCode()!=73)return 7;caught=true;}failCopy=false;
                    if(!caught||starts!=beforeStarts+1||ends!=beforeEnds+1||live!=owners||fallbackLive!=captured||allocated-released!=balance)return 8;
                    failSource=true;caught=false;try{var copy=query;}catch(ref Exception error){if(error.GetCode()!=74)return 9;caught=true;}failSource=false;
                    if(!caught||live!=owners||fallbackLive!=captured||allocated-released!=balance)return 10;
                    beforeStarts=starts;failIterator=true;caught=false;
                    try{query.GetEnumerator();}catch(ref Exception error){if(error.GetCode()!=75)return 11;caught=true;}failIterator=false;
                    if(!caught||starts!=beforeStarts||starts!=ends||live!=owners||allocated-released!=balance)return 12;
                    if(query.Take(0).Count()!=0||live!=owners||fallbackLive!=captured||starts!=ends||allocated-released!=balance)return 13;
                    {
                        var detached=Detached();if(!detached.MoveNext())return 14;
                        Item first=detached.Current;Item second=detached.Current;
                        if(first.Id!=99||second.Id!=99||first.Text.Data==second.Text.Data||!CString::Equals(first.Text.Data,"detached"))return 15;
                        first.Text[0]='X';if(second.Text[0]!='d')return 16;
                        int currentBalance=allocated-released;int currentOwners=live;failCopy=true;caught=false;
                        try{detached.Current;}catch(ref Exception error){if(error.GetCode()!=73)return 17;caught=true;}failCopy=false;
                        if(!caught||allocated-released!=currentBalance||live!=currentOwners)return 18;
                        {Item retry=detached.Current;if(retry.Text[0]!='d')return 19;}
                        if(detached.MoveNext()||detached.MoveNext())return 20;
                    }
                    if(live!=owners||fallbackLive!=captured||starts!=ends||allocated-released!=balance)return 21;
                }
                if(live!=0||fallbackLive!=0||starts!=ends||allocated!=released)return 22;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ManagedSourceAndFallbackCapturesRemainRootedAfterQueryScopeEndsUntilIteratorCleanup(int optimization)
        =>Run("""
            using std; using std::Text; using std::Queries;
            public static replace nuint std::TracingCollector::CollectionThreshold()=>0;
            class Box {public Box^? Self;public int Id;public Box(int id){Self=null;Id=id;}}
            alias Cursor=DefaultIfEmptySource<Box^,Repeated<Box^>,Repeated<Box^>::Enumerator>::Enumerator;
            Box^ Make(int id){Box^ value=new^ Box(id);value.Self=value;return value;}
            Cursor Detached(bool empty){
                Box^ sourceValue=Make(1);Box^ fallback=Make(99);nuint count=1;if(empty)count=0;
                Repeated<Box^> source=new(sourceValue,count);var query=source.AsEnumerable().DefaultIfEmpty(fallback);
                return query.GetEnumerator();
            }
            int Read(Cursor* cursor){Box^ value=cursor.Current;if(value.Self==null||value.Self!.Id!=value.Id)throw new Exception("lost cycle");return value.Id;}
            int Exercise(bool empty){
                var cursor=Detached(empty);
                if(GC::Collect()!=0||TracingCollector::ManagedCount!=2||!cursor.MoveNext())return 1;
                int expected=1;if(empty)expected=99;
                if(Read(&cursor)!=expected||Read(&cursor)!=expected)return 2;
                if(GC::Collect()!=0||TracingCollector::ManagedCount!=2)return 3;
                if(cursor.MoveNext()||cursor.MoveNext())return 4;
                // Even a nonemitted fallback remains owned until iterator cleanup.
                if(GC::Collect()!=0||TracingCollector::ManagedCount!=2)return 5;
                return 0;
            }
            int main(){
                int result=Exercise(false);if(result!=0)return result;
                if(GC::Collect()!=2||TracingCollector::ManagedCount!=0)return 6;
                result=Exercise(true);if(result!=0)return result;
                if(GC::Collect()!=2||TracingCollector::ManagedCount!=0)return 7;
                return 0;
            }
            """,optimization);

    private static void Run(string program,int optimization){
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("default-if-empty.gf",program));
        var result=CompilerTestHelper.Run(sources,optimization);
        Assert.True(result.ExitCode==0,$"DefaultIfEmpty exit {result.ExitCode}: {result.StandardError}");
    }
}
