namespace gflat.Tests;

public sealed class StandardQueryTryElementAtTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void IndexedResultsMatchArrayOraclesAndMissingIndicesPreserveOutputsWithoutAllocation(int optimization)
    {
        var random=new Random(713095);var data=new List<int>();var rows=new List<int>();
        for(int sample=0;sample<32;sample++){
            int[] values=Enumerable.Range(0,sample%16).Select(_=>random.Next(-5000,5001)).ToArray();
            foreach(int index in Enumerable.Range(0,values.Length+3).Append(-1)){
                bool valid=index>=0&&index<values.Length;
                rows.AddRange([data.Count,values.Length,index,valid?1:0,valid?values[index]:112233]);
            }
            data.AddRange(values);
        }
        data.Add(0);
        Run($$"""
            using Standard; using Standard::Text; using Standard::Memory; using Standard::Queries; using Standard::Collections;int allocations=0;
            namespace Memory{public replace void*? Allocate(nuint count){allocations++;return null;}public replace void Free(void* storage)nothrow{} }
            int main(){
                int[{{data.Count}}] data=[{{string.Join(',',data)}}];int[{{rows.Count}}] rows=[{{string.Join(',',rows)}}];
                for(nuint row=0;row<{{rows.Count}};row+=5){
                    Span<int> view=new(&data[(nuint)rows[row]],(nuint)rows[row+1]);var query=view.AsEnumerable();
                    nuint index=(nuint)rows[row+2];if(rows[row+2]<0)index=(nuint)0-(nuint)1;
                    int output=112233;bool success=query.TryElementAt(index,ref output);
                    if(success!=(rows[row+3]!=0)||output!=rows[row+4])return 1;
                    if(success&&query.ElementAt(index)!=rows[row+4])return 2;
                    output=112233;if(query.TryElementAt(index,ref output)!=success||output!=rows[row+4])return 3;
                }
                if(allocations!=0)return 4;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SkippedCurrentsAreNotReadAndSelectionStopsBeforeAThrowingSuffix(int optimization)
        =>Run("""
            using Standard; using Standard::Text; using Standard::Memory; using Standard::Queries; using Standard::Collections;int moves=0;int reads=0;int projections=0;int starts=0;int ends=0;bool poison=false;bool failProjection=false;
            struct Source{
                public struct Iterator{
                    private int position;public Iterator(){position=0;starts++;}
                    public bool MoveNext(){moves++;if(poison&&position==2)throw new Exception("suffix",71);if(position==5)return false;position++;return true;}
                    public int Current{get{reads++;return position;}}
                    ~Iterator(){ends++;}
                }
                public Iterator GetEnumerator()=>new Iterator();
            }
            int Project(int value){projections++;if(failProjection)throw new Exception("projection",72);return value*10;}
            bool Even(int value)=>value%2==0;
            void Reset(){moves=0;reads=0;projections=0;}
            int main(){
                var query=new Enumerable<int,Source,Source::Iterator>(new Source());int[6] indices=[0,1,4,5,9,-1];
                for(nuint sample=0;sample<6;sample++){
                    nuint index=(nuint)indices[sample];if(indices[sample]<0)index=(nuint)0-(nuint)1;
                    bool valid=index<5;int expectedMoves=6;int expectedReads=0;int expected=99;
                    if(valid){expectedMoves=(int)index+1;expectedReads=1;expected=(int)index+1;}
                    int output=99;Reset();
                    if(query.TryElementAt(index,ref output)!=valid||output!=expected||moves!=expectedMoves||reads!=expectedReads||starts!=ends)return 1;
                    output=99;Reset();if(valid)expected*=10;
                    if(query.Select(&Project).TryElementAt(index,ref output)!=valid||output!=expected||moves!=expectedMoves||reads!=expectedReads||projections!=expectedReads||starts!=ends)return 2;
                }
                int output=99;poison=true;Reset();if(!query.TryElementAt(1,ref output)||output!=2||moves!=2||reads!=1||starts!=ends)return 3;
                poison=false;failProjection=true;output=99;Reset();bool caught=false;
                try{query.Select(&Project).TryElementAt(2,ref output);}catch(ref Exception error){if(error.GetCode()!=72)return 4;caught=true;}
                if(!caught||output!=99||moves!=3||reads!=1||projections!=1||starts!=ends)return 5;
                failProjection=false;Reset();if(!query.Where(&Even).TryElementAt(1,ref output)||output!=4||moves!=4||reads!=5||starts!=ends)return 6;
                return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OwningOutputsCommitOnceAndStayUnchangedAcrossFalseAndConstructionReadCopyFailures(int optimization)
        =>Run("""
            using Standard; using Standard::Text; using Standard::Memory; using Standard::Queries; using Standard::Collections;extern void*? malloc(nuint count);extern void free(void* storage);
            int allocations=0;int releases=0;int starts=0;int ends=0;int live=0;int oldReleased=0;int mode=0;int freshMade=0;int freshDead=0;
            namespace Memory{
                public replace void*? Allocate(nuint count){void*? value=malloc(count);if(value!=null)allocations++;return value;}
                public replace void Free(void* storage)nothrow{releases++;free(storage);}
            }
            struct Owner{
                public int Id;public String Text;
                public Owner(int id){Id=id;Text=s"owner";live++;}
                public copy Owner(immutable(Owner)* source){Id=source.Id;Text=source.Text;if(mode==4&&Id==2)throw new Exception("copy",74);live++;}
                ~Owner(){if(Id==99)oldReleased++;live--;}
            }
            struct Source{
                public struct Iterator{
                    private int position;private String token;
                    public Iterator(){position=0;token=s"iterator";if(mode==1)throw new Exception("construction",71);starts++;}
                    public bool MoveNext(){if(mode==2&&position==1)throw new Exception("advance",72);if(position==3)return false;position++;return true;}
                    public Owner Current{get{if(mode==3&&position==2)throw new Exception("current",73);return new Owner(position);}}
                    ~Iterator(){ends++;}
                }
                public Iterator GetEnumerator()=>new Iterator();
            }
            struct Fresh{
                public int Value;public Fresh(int value){Value=value;freshMade++;}~Fresh(){freshDead++;}
            }
            struct FreshSource{
                public struct Iterator{
                    private bool moved;public Iterator(){moved=false;}
                    public bool MoveNext(){if(moved)return false;moved=true;return true;}
                    public Fresh Current{get=>new Fresh(42);}
                }
                public Iterator GetEnumerator()=>new Iterator();
            }
            int main(){
                {
                    Owner output=new(99);readonly(char)* identity=output.Text.Data;int baseline=allocations-releases;
                    var query=new Enumerable<Owner,Source,Source::Iterator>(new Source());
                    if(query.TryElementAt((nuint)0-(nuint)1,ref output)||output.Id!=99||output.Text.Data!=identity||oldReleased!=0||starts!=ends||live!=1||allocations-releases!=baseline)return 1;
                    for(int failure=1;failure<=3;failure++){
                        mode=failure;bool caught=false;
                        try{query.TryElementAt(1,ref output);}catch(ref Exception error){if(error.GetCode()!=70+failure)return 2;caught=true;}
                        mode=0;if(!caught||output.Id!=99||output.Text.Data!=identity||oldReleased!=0||starts!=ends||live!=1||allocations-releases!=baseline)return 3;
                    }
                    {
                        Vector<Owner> values=new();values.Add(new Owner(1));values.Add(new Owner(2));int ownedBaseline=allocations-releases;
                        mode=4;bool caught=false;try{values.AsEnumerable().TryElementAt(1,ref output);}catch(ref Exception error){if(error.GetCode()!=74)return 4;caught=true;}
                        mode=0;if(!caught||output.Id!=99||output.Text.Data!=identity||oldReleased!=0||live!=3||allocations-releases!=ownedBaseline||values[0].Id!=1||values[1].Id!=2)return 5;
                        if(!values.AsEnumerable().TryElementAt(0,ref output)||output.Id!=1||oldReleased!=1||output.Text.Data==values[0].Text.Data||values.Count!=2||live!=3)return 6;
                    }
                    if(output.Id!=1||output.Text.Length!=5||live!=1)return 7;
                }
                if(live!=0||oldReleased!=1||starts!=ends||allocations!=releases)return 8;
                {
                    Fresh result=new(99);var query=new Enumerable<Fresh,FreshSource,FreshSource::Iterator>(new FreshSource());
                    if(query.TryElementAt(1,ref result)||result.Value!=99||freshMade!=1||freshDead!=0)return 9;
                    if(!query.TryElementAt(0,ref result)||result.Value!=42||freshMade!=2||freshDead!=1)return 10;
                }
                if(freshMade!=2||freshDead!=2)return 11;return 0;
            }
            """,optimization);

    private static void Run(string source,int optimization)
    {
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Standard/core"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("query-try-element-at.gf",source));
        var result=CompilerTestHelper.Run(sources,optimization);
        Assert.True(result.ExitCode==0,$"TryElementAt exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
