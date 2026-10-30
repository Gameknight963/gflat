namespace gflat.Tests;

public sealed class StandardQueryEndpointTests
{
    private const string Allocator="""
        using Standard; using Standard::Text; using Standard::Memory; using Standard::Queries;extern void*? malloc(nuint count);extern void free(void* storage);
        int allocated=0;int released=0;
        namespace Memory{
            public replace void*? Allocate(nuint count){void*? value=malloc(count);if(value!=null)allocated++;return value;}
            public replace void Free(void* storage)nothrow{released++;free(storage);}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ChainedEndpointQueriesMatchLinqAndEnumerateIndependentlyWithoutScalarAllocation(int optimization)
    {
        var random=new Random(925317);var data=new List<int>();var expected=new List<int>();var rows=new List<int>();
        for(int sample=0;sample<48;sample++){
            int[] values=Enumerable.Range(0,sample%8).Select(_=>random.Next(-20,21)).ToArray();
            int front=random.Next(-20,21),back=random.Next(-20,21),front2=random.Next(-20,21),back2=random.Next(-20,21);
            int[] answer=values.Where(value=>value%2==0).Select(value=>value*2).Prepend(front).Append(back).Prepend(front2).Append(back2).ToArray();
            rows.AddRange([data.Count,values.Length,expected.Count,answer.Length,front,back,front2,back2]);data.AddRange(values);expected.AddRange(answer);
        }
        data.Add(0);
        Run($$"""
            using Standard; using Standard::Text; using Standard::Memory; using Standard::Queries;int attempts=0;
            namespace Memory{public replace void*? Allocate(nuint count){attempts++;return null;}public replace void Free(void* storage)nothrow{} }
            bool Even(int value)=>value%2==0;int Twice(int value)=>value*2;
            int main(){
                int[{{data.Count}}] data=[{{string.Join(',',data)}}];int[{{data.Count}}] original=data;
                int[{{expected.Count}}] expected=[{{string.Join(',',expected)}}];int[{{rows.Count}}] rows=[{{string.Join(',',rows)}}];
                for(nuint row=0;row<{{rows.Count}};row+=8){
                    Span<int> source=new(&data[(nuint)rows[row]],(nuint)rows[row+1]);
                    var query=source.AsEnumerable().Where(&Even).Select(&Twice).Prepend(rows[row+4]).Append(rows[row+5]).Prepend(rows[row+6]).Append(rows[row+7]);
                    {
                        var first=query.GetEnumerator();var second=query.GetEnumerator();
                        for(nuint i=0;i<(nuint)rows[row+3];i++){
                            int value=expected[(nuint)rows[row+2]+i];if(!first.MoveNext()||first.Current!=value||!second.MoveNext()||second.Current!=value)return 1;
                        }
                        if(first.MoveNext()||second.MoveNext())return 2;
                    }
                    if(query.Count()!=(nuint)rows[row+3]||query.Take(0).Count()!=0)return 3;
                }
                for(nuint i=0;i<{{data.Count}};i++)if(data[i]!=original[i])return 4;
                if(attempts!=0)return 5;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SourceEffectsAndStickyMoveFailuresDifferFromRetryableCurrentFailures(int optimization)
        =>Run("""
            using Standard; using Standard::Text; using Standard::Memory; using Standard::Queries;int length=3;int mode=0;int starts=0;int ends=0;int moves=0;int reads=0;
            struct Source{
                public struct Iterator{
                    private int position;public Iterator(){position=0;starts++;}
                    public bool MoveNext(){moves++;if(mode==2)throw new Exception("advance",72);if(position==length)return false;position++;return true;}
                    public int Current{get{reads++;if(mode==3)throw new Exception("current",73);return position;}}
                    ~Iterator(){ends++;}
                }
                public Iterator GetEnumerator()=>new Iterator();
            }
            void Reset(){starts=0;ends=0;moves=0;reads=0;}
            int main(){
                var source=new Enumerable<int,Source,Source::Iterator>(new Source());
                for(int which=0;which<2;which++){
                    Reset();var query=source.Append(99);if(which==1)query=source.Prepend(99);
                    if(starts!=0||moves!=0||reads!=0)return 1;
                    {
                        var iterator=query.GetEnumerator();if(starts!=1||moves!=0||reads!=0)return 2;
                        bool caught=false;try{iterator.Current;}catch{caught=true;}if(!caught)return 3;
                        if(!iterator.MoveNext())return 4;int first=iterator.Current;int repeated=iterator.Current;
                        if(which==0){if(first!=1||repeated!=1||moves!=1||reads!=2)return 5;}
                        else if(first!=99||repeated!=99||moves!=0||reads!=0)return 6;
                        int produced=1;while(iterator.MoveNext()){
                            produced++;int expected=produced;if(which==1)expected=produced-1;else if(produced==4)expected=99;
                            if(iterator.Current!=expected)return 7;
                        }
                        int expectedReads=4;if(which==1)expectedReads=3;
                        if(produced!=4||moves!=4||reads!=expectedReads||iterator.MoveNext()||iterator.MoveNext()||moves!=4)return 8;
                        caught=false;try{iterator.Current;}catch{caught=true;}if(!caught)return 9;
                    }
                    if(starts!=ends)return 10;
                    Reset();if(query.Take(0).Count()!=0||starts!=1||ends!=1||moves!=0||reads!=0)return 11;
                    Reset();if(query.Take(1).Count()!=1||starts!=1||ends!=1||reads!=0)return 12;
                    int expectedMoves=1;if(which==1)expectedMoves=0;if(moves!=expectedMoves)return 13;
                    Reset();length=0;if(query.Count()!=1||starts!=1||ends!=1||moves!=1||reads!=0)return 14;length=3;
                    Reset();
                    {
                        var iterator=query.GetEnumerator();if(which==1&&!iterator.MoveNext())return 15;
                        mode=2;bool caught=false;try{iterator.MoveNext();}catch(ref Exception error){if(error.GetCode()!=72)return 16;caught=true;}mode=0;
                        int before=moves;if(!caught||iterator.MoveNext()||iterator.MoveNext()||moves!=before)return 17;
                        caught=false;try{iterator.Current;}catch{caught=true;}if(!caught)return 18;
                    }
                    if(starts!=ends||query.Count()!=4)return 19;
                    Reset();
                    {
                        var iterator=query.GetEnumerator();if(!iterator.MoveNext())return 20;if(which==1&&!iterator.MoveNext())return 21;
                        mode=3;bool caught=false;try{iterator.Current;}catch(ref Exception error){if(error.GetCode()!=73)return 22;caught=true;}mode=0;
                        int before=moves;if(!caught||iterator.Current!=1||moves!=before||reads!=2)return 23;
                    }
                    if(starts!=ends)return 24;
                }
                return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OwnedCaptureCopiesAndPartiallyConstructedIteratorsCleanUpWithoutConsumingTheQuery(int optimization)
        =>Run(Allocator+"""
            int live=0;int starts=0;int ends=0;int mode=0;int endpointCopies=0;bool failEndpoint=false;bool failSource=false;
            struct Item{
                public int Id;public String Text;
                public Item(int id,String text){Id=id;Text=move text;live++;}
                public copy Item(immutable(Item)* source){Id=source.Id;Text=source.Text;if(Id==99){if(failEndpoint)throw new Exception("endpoint copy",76);endpointCopies++;}live++;}
                ~Item(){live--;}
            }
            struct Source{
                private String token;public Source(){token=s"source";}
                public copy Source(immutable(Source)* source){token=source.token;if(failSource)throw new Exception("source copy",77);}
                public struct Iterator{
                    private String token;private int position;
                    public Iterator(){token=s"iterator";position=0;if(mode==1)throw new Exception("constructor",71);starts++;}
                    public bool MoveNext(){if(position==2)return false;position++;return true;}
                    public Item Current{get=>new Item(position,s"source item");}
                    ~Iterator(){ends++;}
                }
                public Iterator GetEnumerator()=>new Iterator();
            }
            bool Fail(Item value){throw new Exception("downstream",79);}
            int main(){
                {
                    var source=new Enumerable<Item,Source,Source::Iterator>(new Source());
                    for(int which=0;which<2;which++){
                        Item endpoint=new(99,s"tail");var query=source.Append(endpoint);if(which==1)query=source.Prepend(endpoint);
                        endpoint.Text[0]='X';int baseline=allocated-released;int owners=live;
                        {
                            var first=query.GetEnumerator();var second=query.GetEnumerator();
                            if(!first.MoveNext()||!second.MoveNext())return 1;
                            if(which==0){for(int i=0;i<2;i++)if(!first.MoveNext()||!second.MoveNext())return 2;}
                            Item a=first.Current;Item b=second.Current;if(a.Id!=99||b.Id!=99||!CString::Equals(a.Text.Data,"tail")||a.Text.Data==b.Text.Data)return 3;
                            a.Text[0]='Y';{Item again=first.Current;if(!CString::Equals(again.Text.Data,"tail")||!CString::Equals(b.Text.Data,"tail"))return 4;}
                        }
                        if(live!=owners||starts!=ends||allocated-released!=baseline)return 5;
                        readonly(char)* identity=endpoint.Text.Data;int before=starts;failEndpoint=true;bool caught=false;
                        try{source.Append(endpoint);}catch(ref Exception error){if(error.GetCode()!=76)return 6;caught=true;}failEndpoint=false;
                        if(!caught||endpoint.Text.Data!=identity||endpoint.Id!=99||starts!=before||live!=owners||allocated-released!=baseline)return 7;
                        before=starts;int closed=ends;failEndpoint=true;caught=false;
                        try{query.GetEnumerator();}catch(ref Exception error){if(error.GetCode()!=76)return 8;caught=true;}failEndpoint=false;
                        if(!caught||starts!=before+1||ends!=closed+1||live!=owners||allocated-released!=baseline)return 9;
                        int copied=endpointCopies;before=starts;mode=1;caught=false;
                        try{query.GetEnumerator();}catch(ref Exception error){if(error.GetCode()!=71)return 10;caught=true;}mode=0;
                        if(!caught||endpointCopies!=copied||starts!=before||starts!=ends||live!=owners||allocated-released!=baseline)return 11;
                        failSource=true;caught=false;try{var copy=query;}catch(ref Exception error){if(error.GetCode()!=77)return 12;caught=true;}failSource=false;
                        if(!caught||live!=owners||allocated-released!=baseline)return 13;
                        {
                            var iterator=query.GetEnumerator();if(!iterator.MoveNext())return 14;if(which==0)for(int i=0;i<2;i++)if(!iterator.MoveNext())return 15;
                            int currentBalance=allocated-released;failEndpoint=true;caught=false;try{iterator.Current;}catch(ref Exception error){if(error.GetCode()!=76)return 16;caught=true;}failEndpoint=false;
                            if(!caught||allocated-released!=currentBalance)return 17;
                            {Item value=iterator.Current;if(value.Id!=99||!CString::Equals(value.Text.Data,"tail"))return 18;}
                        }
                        nuint emptyCount=query.Take(0).Count();nuint oneCount=query.Take(1).Count();
                        if(emptyCount!=0||oneCount!=1||live!=owners||starts!=ends||allocated-released!=baseline)return 19;
                        caught=false;try{query.Where(&Fail).Count();}catch(ref Exception error){if(error.GetCode()!=79)return 20;caught=true;}
                        if(!caught||live!=owners||starts!=ends||allocated-released!=baseline)return 21;
                        Item moving=new(77,s"moved");var moved=source.Prepend(move moving);moving=new Item(88,s"replacement");
                        {Item value=moved.First();if(value.Id!=77||!CString::Equals(value.Text.Data,"moved")||moving.Id!=88)return 22;}
                        {Item binary=new(98,s"a\0b");var captured=source.Prepend(move binary);Item value=captured.First();if(value.Text.Length!=3||value.Text[0]!='a'||value.Text[1]!='\0'||value.Text[2]!='b')return 24;}
                        {Item empty=new(98,s"");var captured=source.Prepend(move empty);Item value=captured.First();if(value.Text.Length!=0)return 25;}
                    }
                }
                if(live!=0||starts!=ends||allocated!=released)return 23;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ManagedEndpointsRemainRootedAndRawPointerEndpointsDoNotOwnTheirPointees(int optimization)
        =>Run("""
            using Standard; using Standard::Text; using Standard::Memory; using Standard::Queries;int dead=0;
            class Node{public int Value;public Node(int value){Value=value;}}
            struct Raw{public int Value;public Raw(int value){Value=value;}~Raw(){dead++;}}
            Node^ Make(int value){Node^ result=new^ Node(value);GC::Collect();return result;}
            int main(){
                IntRange range=new(1,2);var query=range.AsEnumerable().Select(&Make).Append(new^ Node(99)).Prepend(new^ Node(0));GC::Collect();
                int[4] expected=[0,1,2,99];
                for(int pass=0;pass<2;pass++){
                    nuint seen=0;var iterator=query.GetEnumerator();GC::Collect();
                    while(iterator.MoveNext()){GC::Collect();Node^ value=iterator.Current;GC::Collect();if(seen>=4||value.Value!=expected[seen])return 1;seen++;}
                    if(seen!=4)return 2;
                }
                Raw* pointer=new* Raw(77);
                {Span<Raw*> empty=new();var endpoints=empty.AsEnumerable().Append(pointer).Prepend(pointer);
                 if(endpoints.Count()!=2||dead!=0)return 3;foreach(Raw* value in endpoints)if(value!=pointer||value.Value!=77)return 4;}
                if(dead!=0||pointer.Value!=77)return 5;delete pointer;if(dead!=1)return 6;return 0;
            }
            """,optimization);

    private static void Run(string source,int optimization)
    {
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Standard"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("query-endpoint.gf",source));
        var result=CompilerTestHelper.Run(sources,optimization);Assert.True(result.ExitCode==0,$"Query endpoint exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
