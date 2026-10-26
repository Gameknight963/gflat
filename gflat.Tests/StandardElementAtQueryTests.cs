namespace gflat.Tests;

public sealed class StandardElementAtQueryTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryPositionMatchesLinqAndProjectionReadsOnlySelectedElement(int optimization)
    {
        int[] values=Enumerable.Range(0,16).Select(i=>i*13-81).ToArray();
        int[] expected=Enumerable.Range(0,17).SelectMany(length=>values.Take(length).Select(value=>value*2)).ToArray();
        Run($$"""
            using std; using std::Text; using std::Memory; using std::Queries;int callbacks=0;
            int Project(int value){callbacks++;return value*2;}
            int main(){
                int[16] values=[{{string.Join(",",values)}}];
                int[{{expected.Length}}] expected=[{{string.Join(",",expected)}}];
                nuint position=0;
                for(nuint length=0;length<=16;length++){
                    Span<int> source=new(values,length);var query=source.AsEnumerable().Select(Project);
                    for(nuint index=0;index<length;index++){
                        callbacks=0;if(query.ElementAt(index)!=expected[position]||callbacks!=1)return 1;position++;
                    }
                    for(int huge=0;huge<2;huge++){
                        nuint index=length;if(huge==1)index=(nuint)0-(nuint)1;
                        callbacks=0;int caught=0;
                        try{query.ElementAt(index);}catch(ref Exception error){if(error.GetCode()!=3)return 2;caught++;}
                        if(caught!=1||callbacks!=0)return 3;
                    }
                }
                return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void QualifiedViewsReturnIndependentOwningValues(int optimization)
        =>Run("""
            using std; using std::Text; using std::Memory; using std::Queries;
            int main(){
                String[2] values=[s"one",s"two"];Span<String> source=new(values,2);
                readonly(String) shallow=source.AsReadOnly().AsEnumerable().ElementAt(1);
                immutable(String) deep=source.AsImmutable().AsEnumerable().ElementAt(0);
                if(shallow.Length!=3||deep.Length!=3||shallow.Data==values[1].Data||deep.Data==values[0].Data)return 1;
                values[0][0]='O';values[1].Clear();
                if(shallow[0]!='t'||deep[0]!='o')return 2;
                int[3] numbers=[10,20,30];Span<int> ints=new(numbers,3);
                if(ints.AsReadOnly().AsEnumerable().ElementAt(1)!=20||ints.AsImmutable().AsEnumerable().ElementAt(2)!=30)return 3;
                return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NoncopyableIteratorsAndOwningResultsCleanUpOnReturnAndFailures(int optimization)
        =>Run("""
            using std; using std::Text; using std::Memory; using std::Queries;
            extern void*? malloc(nuint size);extern void free(void* pointer);
            int allocations=0;int releases=0;int active=0;int created=0;int destroyed=0;
            int moves=0;int reads=0;int mode=0;
            namespace Memory{
                public replace void*? Allocate(nuint size){void*? result=malloc(size);if(result!=null)allocations++;return result;}
                public replace void Free(void* pointer) nothrow{releases++;free(pointer);}
            }
            struct Source{
                public Source::Iterator GetEnumerator()=>new Source::Iterator();
                public struct Iterator{
                    private String token;private int index;
                    public Iterator(){token=s"iterator";if(mode==1)throw new Exception("construction",71);index=0;active++;created++;}
                    public bool MoveNext(){moves++;if(mode==2&&moves==2)throw new Exception("advance",72);if(index==3)return false;index++;return true;}
                    public String Current{
                        get{reads++;String scratch=s"scratch";if(mode==3)throw new Exception("current",73);
                            if(index==1)return s"one";if(index==2)return s"two";return s"three";}
                    }
                    ~Iterator(){active--;destroyed++;}
                }
            }
            int main(){
                var query=new Enumerable<String,Source,Source::Iterator>(new Source());
                {
                    moves=0;reads=0;String last=query.ElementAt(2);
                    if(last.Length!=5||last[0]!='t'||moves!=3||reads!=1||active!=0)return 1;
                    moves=0;reads=0;String first=query.ElementAt(0);
                    if(first.Length!=3||first[0]!='o'||moves!=1||reads!=1||active!=0)return 2;
                }
                if(active!=0||created!=destroyed||allocations!=releases)return 3;
                {
                    String output=s"keep";readonly(char)* old=output.Data;int baseline=allocations-releases;
                    for(int failure=0;failure<=3;failure++){
                        mode=failure;moves=0;reads=0;int caught=0;
                        nuint index=1;if(failure==0)index=(nuint)0-(nuint)1;
                        try{output=query.ElementAt(index);}catch(ref Exception error){
                            int code=70+failure;if(failure==0)code=3;if(error.GetCode()!=code)return 4;caught++;
                        }
                        if(caught!=1||output.Data!=old||output.Length!=4||active!=0||created!=destroyed||allocations-releases!=baseline)return 5;
                        if(failure==0&&(moves!=4||reads!=0))return 6;
                    }
                }
                if(active!=0||created!=destroyed||allocations!=releases)return 7;return 0;
            }
            """,optimization);

    private static void Run(string program,int optimization)
    {
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("element-at.gf",program));
        var result=CompilerTestHelper.Run(sources,optimization);
        Assert.True(result.ExitCode==0,$"Exit {result.ExitCode}: {result.StandardError}");
    }
}
