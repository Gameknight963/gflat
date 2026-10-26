namespace gflat.Tests;

public sealed class StandardQueryReverseTests
{
    private const string Allocator="""
        using std; using std::Memory; using std::Queries; using std::Collections;extern void*? malloc(nuint count);extern void free(void* storage);
        int attempts=0;int allocations=0;int releases=0;int failAt=0;bool deny=false;
        namespace Memory{
            public replace void*? Allocate(nuint count){attempts++;if(deny||(failAt!=0&&attempts==failAt))return null;void*? value=malloc(count);if(value!=null)allocations++;return value;}
            public replace void Free(void* storage)nothrow{releases++;free(storage);}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ReversedValuesAndChainsMatchLinqAndIndependentEnumerationsPreserveSources(int optimization)
    {
        var random=new Random(183627);var data=new List<int>();var expected=new List<int>();var rows=new List<int>();
        for(int sample=0;sample<48;sample++){
            int[] values=Enumerable.Range(0,sample%24).Select(_=>random.Next(-100,101)).ToArray();
            int[] chain=values.Where(value=>value%2==0).Select(value=>value+1).Reverse().ToArray();
            rows.AddRange([data.Count,values.Length,expected.Count,chain.Length]);
            data.AddRange(values);expected.AddRange(values.Reverse());expected.AddRange(chain);
        }
        data.Add(0);expected.Add(0);
        Run(Allocator+$$"""
            bool Even(int value)=>value%2==0;int Increment(int value)=>value+1;int Ordinary(readonly(int) value)=>(int)value;
            int main(){
                int[{{data.Count}}] data=[{{string.Join(',',data)}}];int[{{expected.Count}}] expected=[{{string.Join(',',expected)}}];
                int[{{rows.Count}}] rows=[{{string.Join(',',rows)}}];
                for(nuint row=0;row<{{rows.Count}};row+=4){
                    Span<int> view=new(&data[(nuint)rows[row]],(nuint)rows[row+1]);nuint offset=(nuint)rows[row+2];
                    {
                        var query=view.AsEnumerable().Reverse();var first=query.GetEnumerator();var second=query.GetEnumerator();
                        for(nuint i=0;i<view.Length;i++){
                            if(!first.MoveNext()||first.Current!=expected[offset+i]||!second.MoveNext()||second.Current!=expected[offset+i])return 1;
                            if(view[view.Length-1-i]!=expected[offset+i])return 2;
                        }
                        if(first.MoveNext()||second.MoveNext())return 3;
                    }
                    nuint seen=0;var chain=view.AsEnumerable().Where(&Even).Select(&Increment).Reverse();
                    foreach(int value in chain){if(seen>=(nuint)rows[row+3]||value!=expected[offset+view.Length+seen])return 4;seen++;}
                    if(seen!=(nuint)rows[row+3])return 5;
                    seen=0;foreach(int value in view.AsReadOnly().AsEnumerable().Select(&Ordinary).Reverse()){
                        if(value!=expected[offset+seen])return 6;seen++;
                    }
                    if(seen!=view.Length||allocations!=releases)return 7;
                }
                int[3] items=[1,2,3];Span<int> span=new(items,3);std::Collections::Reverse<int>(span);if(items[0]!=3||items[1]!=2||items[2]!=1)return 8;
                return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FirstMoveMaterializesEverythingWhileConstructionAndTakeZeroStayDeferred(int optimization)
        =>Run(Allocator+"""
            int starts=0;int ends=0;int moves=0;int reads=0;int length=3;
            struct Source{
                private int* data;public Source(int* values){data=values;}
                public struct Iterator{
                    private int* data;private int position;
                    public Iterator(int* values){data=values;position=0;starts++;}
                    public bool MoveNext(){moves++;if(position==length)return false;position++;return true;}
                    public ref int Current{get{reads++;return data[position-1];}}
                    ~Iterator(){ends++;}
                }
                public Iterator GetEnumerator()=>new Iterator(data);
            }
            int main(){
                int[3] values=[1,2,3];deny=true;
                var query=new Enumerable<int,Source,Source::Iterator>(new Source(&values[0])).Reverse();
                if(query.Take(0).Count()!=0||starts!=0||moves!=0||reads!=0||attempts!=0)return 1;
                {
                    var first=query.GetEnumerator();var second=query.GetEnumerator();
                    if(starts!=0||attempts!=0)return 2;deny=false;
                    bool caught=false;try{first.Current;}catch{caught=true;}if(!caught||starts!=0)return 3;
                    values[2]=30;
                    if(!first.MoveNext()||first.Current!=30||starts!=1||ends!=1||moves!=4||reads!=3)return 4;
                    values[2]=300;
                    if(first.Current!=30||!second.MoveNext()||second.Current!=300||starts!=2||ends!=2||moves!=8||reads!=6)return 5;
                    if(!first.MoveNext()||first.Current!=2||!first.MoveNext()||first.Current!=1||first.MoveNext()||first.MoveNext()||moves!=8||reads!=6)return 6;
                    caught=false;try{first.Current;}catch{caught=true;}if(!caught)return 7;
                    if(allocations==releases)return 8;
                }
                if(allocations!=releases)return 9;
                starts=0;ends=0;moves=0;reads=0;
                if(query.Take(1).Count()!=1||starts!=1||ends!=1||moves!=4||reads!=3||allocations!=releases)return 10;
                starts=0;ends=0;moves=0;reads=0;length=0;int baseline=attempts;deny=true;
                {var empty=query.GetEnumerator();if(empty.MoveNext()||empty.MoveNext()||starts!=1||ends!=1||moves!=1||reads!=0||attempts!=baseline)return 11;}
                if(allocations!=releases)return 12;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OwnerBuffersUnwindOnFailuresRemainStickyAndRetryOnlyWithFreshIterators(int optimization)
        =>Run(Allocator+"""
            int live=0;int starts=0;int ends=0;int mode=0;int moves=0;
            struct Owner{
                public int Id;public String Text;
                public Owner(int id){Id=id;Text=s"owner";live++;}
                public copy Owner(immutable(Owner)* source){Id=source.Id;Text=source.Text;if((mode==4&&Id==2)||(mode==5&&Id==3))throw new Exception("copy",70+mode);live++;}
                ~Owner(){live--;}
            }
            struct Source{
                private Span<Owner> values;public Source(Span<Owner> input){values=input;}
                public struct Iterator{
                    private Span<Owner> values;private nuint position;private String token;
                    public Iterator(Span<Owner> input){values=input;position=0;token=s"iterator";if(mode==1)throw new Exception("construction",71);starts++;}
                    public bool MoveNext(){moves++;if(mode==2&&position==1)throw new Exception("advance",72);if(position==values.Length)return false;position++;return true;}
                    public ref Owner Current{get{if(mode==3&&position==2)throw new Exception("current",73);return values[position-1];}}
                    ~Iterator(){ends++;}
                }
                public Iterator GetEnumerator()=>new Iterator(values);
            }
            bool Fail(Owner value){throw new Exception("downstream",77);}
            int main(){
                {
                    Vector<Owner> values=new();for(int id=1;id<=3;id++)values.Add(new Owner(id));
                    var query=new Enumerable<Owner,Source,Source::Iterator>(new Source(values.AsSpan())).Reverse();int baseline=allocations-releases;
                    for(int failure=1;failure<=4;failure++){
                        {
                            var iterator=query.GetEnumerator();mode=failure;bool caught=false;
                            try{iterator.MoveNext();}catch(ref Exception error){if(error.GetCode()!=70+failure)return 1;caught=true;}
                            mode=0;int moved=moves;int started=starts;
                            if(!caught||iterator.MoveNext()||iterator.MoveNext()||moves!=moved||starts!=started||starts!=ends||live!=3||allocations-releases!=baseline)return 2;
                            caught=false;try{iterator.Current;}catch{caught=true;}if(!caught)return 3;
                        }
                        {var fresh=query.GetEnumerator();if(!fresh.MoveNext())return 4;Owner item=fresh.Current;if(item.Id!=3||item.Text.Data==values[2].Text.Data)return 5;}
                        if(live!=3||allocations-releases!=baseline)return 6;
                    }
                    int points=0;attempts=0;{var measured=query.GetEnumerator();if(!measured.MoveNext())return 7;points=attempts;}
                    if(points==0||live!=3||allocations-releases!=baseline)return 8;
                    for(int failure=1;failure<=points;failure++){
                        var iterator=query.GetEnumerator();attempts=0;failAt=failure;bool caught=false;
                        try{iterator.MoveNext();}catch(ref BadAllocException error){caught=true;}failAt=0;
                        int moved=moves;if(!caught||iterator.MoveNext()||moves!=moved||live!=3||starts!=ends||allocations-releases!=baseline)return 9;
                    }
                    {
                        var iterator=query.GetEnumerator();if(!iterator.MoveNext()||live!=6)return 10;
                        mode=5;bool caught=false;try{iterator.Current;}catch(ref Exception error){if(error.GetCode()!=75)return 11;caught=true;}mode=0;
                        if(!caught||live!=6)return 12;{Owner item=iterator.Current;if(item.Id!=3)return 13;}
                        while(iterator.MoveNext()){}if(live!=6||allocations-releases<=baseline)return 14;
                    }
                    if(live!=3||allocations-releases!=baseline)return 15;
                    foreach(Owner item in query){if(item.Id!=3)return 16;break;}
                    bool caught=false;try{query.Where(&Fail).Any();}catch(ref Exception error){if(error.GetCode()!=77)return 17;caught=true;}
                    if(!caught||live!=3||allocations-releases!=baseline||values[0].Id!=1||values[1].Id!=2||values[2].Id!=3)return 18;
                }
                if(live!=0||starts!=ends||allocations!=releases)return 19;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FreshManagedProjectionResultsStayRootedByTheReverseBuffer(int optimization)
        =>Run("""
            using std; using std::Memory; using std::Queries; using std::Collections;class Item{public int Value;public Item(int value){Value=value;}}
            Item^ Make(int value){Item^ result=new^ Item(value);GC::Collect();return result;}
            int main(){
                IntRange range=new(1,4);var query=range.AsEnumerable().Select(&Make).Reverse();
                {
                    var iterator=query.GetEnumerator();int expected=4;
                    while(iterator.MoveNext()){
                        Item^ value=iterator.Current;GC::Collect();
                        if(value.Value!=expected||TracingCollector::ManagedCount!=4)return 1;expected--;
                    }
                    if(expected!=0)return 2;
                }
                return 0;
            }
            """,optimization,managed:true);

    private static void Run(string source,int optimization,bool managed=false)
    {
        string root=Path.Combine(AppContext.BaseDirectory,"Std");
        IEnumerable<string> paths=Directory.GetFiles(Path.Combine(root,"core"),"*.gf",SearchOption.AllDirectories);
        if(managed)paths=paths.Concat(new[]{Path.Combine(root,"libc/GC.gf"),Path.Combine(root,"libc/TracingCollector.gf")});
        var sources=paths.Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("query-reverse.gf",source));
        var result=CompilerTestHelper.Run(sources,optimization);
        Assert.True(result.ExitCode==0,$"Reverse exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
