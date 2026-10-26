namespace gflat.Tests;

public sealed class StandardQueryAggregateTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SeedlessFoldsMatchLinqIncludingNoncommutativeOrderAndKeepSeededOverload(int optimization)
    {
        var random=new Random(619273);var data=new List<int>();var rows=new List<int>();
        for(int sample=0;sample<48;sample++){
            int[] values=Enumerable.Range(0,sample%12).Select(_=>random.Next(-20,21)).ToArray();
            rows.AddRange([data.Count,values.Length,values.Length==0?0:values.Aggregate((left,right)=>left*3-right),
                values.Length==0?0:values.Aggregate((left,right)=>left-right),values.Sum(),values.Aggregate(7,(left,right)=>left*3-right)]);
            data.AddRange(values);
        }
        data.Add(0);
        Run($$"""
            using std; using std::Memory; using std::Queries;int attempts=0;bool deny=false;
            extern void*? malloc(nuint count);extern void free(void* storage);
            namespace Memory{public replace void*? Allocate(nuint count){attempts++;if(deny)return null;return malloc(count);}public replace void Free(void* storage)nothrow{free(storage);} }
            int Fold(int left,int right)=>left*3-right;int Subtract(int left,int right)=>left-right;int Sum(int left,int right)=>left+right;
            int main(){
                int[{{data.Count}}] data=[{{string.Join(',',data)}}];int[{{rows.Count}}] rows=[{{string.Join(',',rows)}}];
                for(nuint row=0;row<{{rows.Count}};row+=6){
                    Span<int> view=new(&data[(nuint)rows[row]],(nuint)rows[row+1]);var query=view.AsEnumerable();
                    int before=attempts;deny=true;
                    if(query.Aggregate(7,&Fold)!=rows[row+5])return 1;
                    if(view.Length!=0){
                        if(query.Aggregate(&Fold)!=rows[row+2]||query.Aggregate(&Subtract)!=rows[row+3]||query.Aggregate(&Sum)!=rows[row+4]||query.Aggregate(&Fold)!=rows[row+2])return 2;
                    }
                    deny=false;if(attempts!=before)return 3;
                    if(view.Length==0){bool caught=false;try{query.Aggregate(&Fold);}catch(ref Exception error){if(!CString::Equals(error.GetMessage(),"Sequence is empty"))return 4;caught=true;}if(!caught)return 5;}
                }
                return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void IteratorAndCallbackEffectsReadEachElementOnceAndEmptyNeverInvokesTheCallback(int optimization)
        =>Run("""
            using std; using std::Memory; using std::Queries;int length=3;int mode=0;int starts=0;int ends=0;int moves=0;int reads=0;int calls=0;
            struct Source{
                private int* values;public Source(int* data){values=data;}
                public struct Iterator{
                    private int* values;private int position;
                    public Iterator(int* data){values=data;position=0;if(mode==1)throw new Exception("construction",71);starts++;}
                    public bool MoveNext(){moves++;if(mode==2&&position==1)throw new Exception("advance",72);if(position==length)return false;position++;return true;}
                    public int Current{get{reads++;if(mode==3&&position==2)throw new Exception("current",73);return values[position-1];}}
                    ~Iterator(){ends++;}
                }
                public Iterator GetEnumerator()=>new Iterator(values);
            }
            int Combine(int left,int right){calls++;if(mode==4)throw new Exception("callback",74);return left*10+right;}
            void Reset(){moves=0;reads=0;calls=0;}
            int main(){
                int[3] data=[1,2,3];var query=new Enumerable<int,Source,Source::Iterator>(new Source(&data[0]));
                if(query.Aggregate(&Combine)!=123||moves!=4||reads!=3||calls!=2||starts!=ends)return 1;
                length=1;Reset();if(query.Aggregate(&Combine)!=1||moves!=2||reads!=1||calls!=0||starts!=ends)return 2;
                length=0;Reset();bool caught=false;try{query.Aggregate(&Combine);}catch(ref Exception error){if(!CString::Equals(error.GetMessage(),"Sequence is empty"))return 3;caught=true;}
                if(!caught||moves!=1||reads!=0||calls!=0||starts!=ends)return 4;
                length=3;
                for(int failure=1;failure<=4;failure++){
                    mode=failure;Reset();caught=false;
                    try{query.Aggregate(&Combine);}catch(ref Exception error){if(error.GetCode()!=70+failure)return 5;caught=true;}
                    mode=0;if(!caught||starts!=ends||data[0]!=1||data[1]!=2||data[2]!=3)return 6;
                }
                return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NoncopyableFreshCurrentsMoveThroughTheAccumulatorAndUnwindOwnedResources(int optimization)
        =>Run("""
            using std; using std::Memory; using std::Queries;extern void*? malloc(nuint count);extern void free(void* storage);
            int allocated=0;int released=0;int made=0;int dead=0;int starts=0;int ends=0;int calls=0;int mode=0;int length=3;
            namespace Memory{
                public replace void*? Allocate(nuint count){void*? value=malloc(count);if(value!=null)allocated++;return value;}
                public replace void Free(void* storage)nothrow{released++;free(storage);}
            }
            struct Owner{
                public int Value;public String Token;
                public Owner(int value){Value=value;Token=s"owner";made++;}
                ~Owner(){dead++;}
            }
            struct Source{
                public struct Iterator{
                    private int position;private String token;
                    public Iterator(){position=0;token=s"iterator";if(mode==1)throw new Exception("construction",71);starts++;}
                    public bool MoveNext(){if(mode==2&&position==1)throw new Exception("advance",72);if(position==length)return false;position++;return true;}
                    public Owner Current{get{if(mode==3&&position==2)throw new Exception("current",73);return new Owner(position);}}
                    ~Iterator(){ends++;}
                }
                public Iterator GetEnumerator()=>new Iterator();
            }
            Owner Combine(Owner accumulator,Owner value){calls++;if(mode==4)throw new Exception("callback",74);accumulator.Value=accumulator.Value*10+value.Value;return move accumulator;}
            int main(){
                var query=new Enumerable<Owner,Source,Source::Iterator>(new Source());
                {Owner result=query.Aggregate(&Combine);if(result.Value!=123||result.Token.Length!=5||made!=3||dead!=2||calls!=2||starts!=ends)return 1;}
                if(made!=dead||allocated!=released)return 2;
                length=1;calls=0;int before=made;
                {Owner result=query.Aggregate(&Combine);if(result.Value!=1||made-before!=1||calls!=0||made-dead!=1)return 3;}
                if(made!=dead||allocated!=released)return 4;
                length=0;calls=0;before=made;bool caught=false;try{query.Aggregate(&Combine);}catch(ref Exception error){if(!CString::Equals(error.GetMessage(),"Sequence is empty"))return 5;caught=true;}
                if(!caught||made!=before||calls!=0||made!=dead||starts!=ends||allocated!=released)return 6;
                length=3;
                for(int failure=1;failure<=4;failure++){
                    mode=failure;calls=0;caught=false;
                    try{query.Aggregate(&Combine);}catch(ref Exception error){if(error.GetCode()!=70+failure)return 7;caught=true;}
                    mode=0;if(!caught||made!=dead||starts!=ends||allocated!=released)return 8;
                }
                return 0;
            }
            """,optimization);

    private static void Run(string source,int optimization)
    {
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("query-aggregate.gf",source));
        var result=CompilerTestHelper.Run(sources,optimization);Assert.True(result.ExitCode==0,$"Aggregate exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
