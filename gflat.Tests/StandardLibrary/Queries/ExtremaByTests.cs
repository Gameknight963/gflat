namespace gflat.Tests.Std.Queries;

// Selectors use the established ordinary by-value T contract.
public sealed class ExtremaByTests
{
    private const string Allocator="""
        using Standard; using Standard::Text; using Standard::Memory; using Standard::Queries; using Standard::Collections;extern void*? malloc(nuint count);extern void free(void* storage);
        int attempts=0;int allocated=0;int released=0;bool deny=false;
        namespace Memory{
            public replace void*? Allocate(nuint count){attempts++;if(deny)return null;void*? result=malloc(count);if(result!=null)allocated++;return result;}
            public replace void Free(void* storage)nothrow{released++;free(storage);}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void IndependentLinqOraclesRetainFirstTiesForDefaultAndReversedKeyComparersWithoutAllocation(int optimization)
    {
        var keys=new List<int>();var rows=new List<int>();var random=new Random(810917);
        for(int sample=0;sample<48;sample++){
            int[] values=Enumerable.Range(0,sample%15+1).Select(_=>random.Next(-3,4)).ToArray();
            var tagged=values.Select((key,id)=>(Key:key,Id:id)).ToArray();
            rows.AddRange([keys.Count,values.Length,tagged.MinBy(value=>value.Key).Id,tagged.MaxBy(value=>value.Key).Id]);keys.AddRange(values);
        }
        Run(Allocator+$$$"""
            struct Tagged{public int Key;public int Id;public Tagged(){Key=0;Id=-1;}public Tagged(int key,int id){Key=key;Id=id;}}
            int selections=0;
            int SelectKey(Tagged value){selections++;return value.Key;}
            int Compare(immutable(int)* left,immutable(int)* right){if(*left<*right)return -1;if(*left>*right)return 1;return 0;}
            int Reverse(immutable(int)* left,immutable(int)* right)=>Compare(right,left);
            int main(){
                int[{{{keys.Count}}}] keys=[{{{string.Join(',',keys)}}}];int[{{{rows.Count}}}] rows=[{{{string.Join(',',rows)}}}];
                int before=attempts;deny=true;
                for(nuint row=0;row<{{{rows.Count}}};row+=4){
                    Tagged[15] storage;for(nuint i=0;i<(nuint)rows[row+1];i++)storage[i]=new Tagged(keys[(nuint)rows[row]+i],(int)i);
                    Span<Tagged> view=new(&storage[0],(nuint)rows[row+1]);var query=view.AsEnumerable();
                    for(int operation=0;operation<6;operation++){
                        selections=0;Tagged result=new(0,-1);
                        if(operation==0)result=query.MinBy(&SelectKey);
                        else if(operation==1)result=query.MaxBy(&SelectKey);
                        else if(operation==2)result=query.MinBy(&SelectKey,&Compare);
                        else if(operation==3)result=query.MaxBy(&SelectKey,&Compare);
                        else if(operation==4)result=query.MinBy(&SelectKey,&Reverse);
                        else result=query.MaxBy(&SelectKey,&Reverse);
                        int expected=rows[row+2];if(operation==1||operation==3||operation==4)expected=rows[row+3];
                        if(result.Id!=expected||selections!=rows[row+1])return 1;
                        for(nuint i=0;i<view.Length;i++)if(view[i].Id!=(int)i||view[i].Key!=keys[(nuint)rows[row]+i])return 2;
                    }
                }
                if(attempts!=before)return 3;deny=false;if(allocated!=released)return 4;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CurrentSelectorAndCurrentVersusBestComparisonsHaveExactForwardOrderIncludingEmptyAndSingleton(int optimization)
        =>Run("""
            using Standard; using Standard::Text; using Standard::Memory; using Standard::Queries; using Standard::Collections;int length=5;int moves=0;int reads=0;int selected=0;int compared=0;int starts=0;int ends=0;int events=0;int[32] trace;
            void Event(int value){trace[(nuint)events]=value;events++;}
            struct Source{
                private int* values;public Source(int* input){values=input;}
                public struct Iterator{
                    private int* values;private int position;public Iterator(int* input){values=input;position=0;starts++;}
                    public bool MoveNext(){moves++;Event(10+position);if(position==length)return false;position++;return true;}
                    public int Current{get{reads++;Event(100+values[position-1]);return values[position-1];}}
                    ~Iterator(){ends++;}
                }
                public Iterator GetEnumerator()=>new Iterator(values);
            }
            int SelectKey(int value){selected++;Event(200+value);return value%3;}
            int Compare(immutable(int)* current,immutable(int)* best){compared++;Event(300+*current*10+*best);if(*current<*best)return -1;if(*current>*best)return 1;return 0;}
            void Reset(){moves=0;reads=0;selected=0;compared=0;events=0;}
            int main(){
                int[5] values=[8,4,7,1,5];var query=new Enumerable<int,Source,Source::Iterator>(new Source(&values[0]));
                int[20] minTrace=[10,108,208,11,104,204,312,12,107,207,311,13,101,201,311,14,105,205,321,15];
                int[20] maxTrace=[10,108,208,11,104,204,312,12,107,207,312,13,101,201,312,14,105,205,322,15];
                for(int which=0;which<2;which++){
                    length=5;Reset();int result=0;if(which==0)result=query.MinBy(&SelectKey,&Compare);else result=query.MaxBy(&SelectKey,&Compare);
                    int expected=4;if(which==1)expected=8;
                    if(result!=expected||moves!=6||reads!=5||selected!=5||compared!=4||events!=20||starts!=ends)return 1;
                    for(nuint i=0;i<20;i++){int expectedEvent=minTrace[i];if(which==1)expectedEvent=maxTrace[i];if(trace[i]!=expectedEvent)return 2;}
                    length=1;Reset();if(which==0)result=query.MinBy(&SelectKey,&Compare);else result=query.MaxBy(&SelectKey,&Compare);
                    if(result!=8||moves!=2||reads!=1||selected!=1||compared!=0||starts!=ends||events!=4||trace[0]!=10||trace[1]!=108||trace[2]!=208||trace[3]!=11)return 3;
                    length=0;Reset();bool caught=false;
                    try{if(which==0)query.MinBy(&SelectKey,&Compare);else query.MaxBy(&SelectKey,&Compare);}catch(ref Exception error){if(!CString::Equals(error.GetMessage(),"Sequence is empty"))return 4;caught=true;}
                    if(!caught||moves!=1||reads!=0||selected!=0||compared!=0||starts!=ends||events!=1||trace[0]!=10)return 5;
                }
                if(values[0]!=8||values[1]!=4||values[2]!=7||values[3]!=1||values[4]!=5)return 6;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void IndependentlyOwningElementsAndNoncopyableKeysCleanUpOnSelectorComparerCopyAndIteratorFailures(int optimization)
        =>Run(Allocator+"""
            int mode=0;int owners=0;int keys=0;int starts=0;int ends=0;int selections=0;int comparisons=0;int keyAttempts=0;
            struct Owner{
                public int Key;public int Id;private String token;
                public Owner(int key,int id){Key=key;Id=id;token=s"owner";owners++;}
                public copy Owner(immutable(Owner)* other){Key=other.Key;Id=other.Id;token=other.token;if(mode==7&&Id==2)throw new Exception("copy",77);owners++;}
                ~Owner(){owners--;}
            }
            // No copy constructor: the cached selector result must transfer ownership.
            struct Key{
                public int Value;private String token;
                public Key(int value){Value=value;token=s"key";keyAttempts++;if(mode==6&&keyAttempts==2)throw new Exception("key",76);keys++;}
                ~Key(){keys--;}
            }
            struct Source{
                public struct Iterator{
                    private int position;private String token;
                    public Iterator(){position=0;token=s"iterator";if(mode==1)throw new Exception("iterator",71);starts++;}
                    public bool MoveNext(){if(mode==2&&position==1)throw new Exception("advance",72);if(position==5)return false;position++;return true;}
                    public Owner Current{get{if(mode==3&&position==2)throw new Exception("current",73);int key=1;if(position==1)key=2;if(position>=4)key=3;return new Owner(key,position);}}
                    ~Iterator(){ends++;}
                }
                public Iterator GetEnumerator()=>new Iterator();
            }
            Key SelectKey(Owner value){selections++;if(mode==4&&selections==2)throw new Exception("selector",74);return new Key(value.Key);}
            int Compare(immutable(Key)* current,immutable(Key)* best){comparisons++;if(mode==5)throw new Exception("comparer",75);if(current.Value<best.Value)return -1;if(current.Value>best.Value)return 1;return 0;}
            void Reset(){selections=0;comparisons=0;keyAttempts=0;}
            int main(){
                var query=new Enumerable<Owner,Source,Source::Iterator>(new Source());
                for(int which=0;which<2;which++){
                    Reset();
                    {Owner result=new(0,99);if(which==0)result=query.MinBy(&SelectKey,&Compare);else result=query.MaxBy(&SelectKey,&Compare);
                        int expected=2;if(which==1)expected=4;
                        if(result.Id!=expected||owners!=1||keys!=0||selections!=5||comparisons!=4||starts!=ends)return 1;
                    }
                    if(owners!=0||keys!=0||allocated!=released)return 2;
                    for(int failure=1;failure<=7;failure++){
                        {
                            Owner target=new(0,99);int live=allocated-released;mode=failure;Reset();bool caught=false;
                            try{if(which==0)target=query.MinBy(&SelectKey,&Compare);else target=query.MaxBy(&SelectKey,&Compare);}
                            catch(ref Exception error){if(error.GetCode()!=70+failure)return 3;caught=true;}
                            mode=0;
                            if(!caught||target.Id!=99||target.Key!=0||owners!=1||keys!=0||starts!=ends||allocated-released!=live)return 4;
                        }
                        if(owners!=0||keys!=0||allocated!=released)return 5;
                    }
                }
                return 0;
            }
            """,optimization);

    private static IEnumerable<SourceFile> Sources(string program)
        =>Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Standard"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("query-extrema-by.gf",program));
    private static void Run(string program,int optimization)
    {
        var result=CompilerTestHelper.Run(Sources(program),optimization);Assert.True(result.ExitCode==0,$"Query extrema-by exit {result.ExitCode}: {result.StandardError}");
    }
}
