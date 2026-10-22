namespace gflat.Tests;

public sealed class StandardZipQueryTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DifferentSourcesAndElementTypesMatchLinqForEveryShorterLength(int optimization)
    {
        short[] left=Enumerable.Range(0,16).Select(i=>(short)(i*13-81)).ToArray();
        var expected=new List<long>();
        for(int leftCount=0;leftCount<=16;leftCount++)for(int rightCount=0;rightCount<=20;rightCount++)
            expected.Add(left.Take(leftCount).Zip(Enumerable.Range(100,rightCount),(a,b)=>(long)a*1000+b).Sum());
        Run($$"""
            using std;int allocations=0;int callbacks=0;
            namespace Memory{
                public replace void*? Allocate(nuint size){allocations++;return null;}
                public replace void Free(void* pointer){}
            }
            long Combine(short left,int right){callbacks++;return (long)left*1000l+(long)right;}
            long Sum(long total,long item)=>total+item;
            int main(){
                short[16] values=[{{string.Join(",",left.Select(value=>"(short)"+value))}}];
                long[{{expected.Count}}] expected=[{{string.Join(",",expected.Select(value=>value+"l"))}}];
                nuint index=0;
                for(nuint leftCount=0;leftCount<=16;leftCount++)for(int rightCount=0;rightCount<=20;rightCount++){
                    Span<short> first=new(values,leftCount);
                    var query=first.AsEnumerable().Zip(new IntRange(100,rightCount).AsEnumerable(),Combine);
                    callbacks=0;nuint minimum=leftCount;if((nuint)rightCount<minimum)minimum=(nuint)rightCount;
                    if(query.Count()!=minimum||callbacks!=0)return 1;
                    if(query.Aggregate(0l,Sum)!=expected[index]||callbacks!=(int)minimum)return 2;
                    if(query.Aggregate(0l,Sum)!=expected[index]||callbacks!=(int)minimum*2)return 3;
                    index++;
                }
                Span<short> first=new(values,2);
                var lambda=first.AsEnumerable().Zip(new IntRange(100,2).AsEnumerable(),(short a,int b)=>(long)a+(long)b);
                if(lambda.Aggregate(0l,Sum)!=52l)return 4;
                if(allocations!=0)return 5;return 0;
            }
            """,optimization);
    }

    private const string OwningSources="""
        using std;
        extern void*? malloc(nuint bytes);extern void free(void* pointer);
        int allocations=0;int releases=0;int active=0;int created=0;int destroyed=0;
        int starts=0;int advances=0;int callbacks=0;int failure=0;
        namespace Memory{
            public replace void*? Allocate(nuint bytes){void*? result=malloc(bytes);if(result!=null)allocations++;return result;}
            public replace void Free(void* pointer){releases++;free(pointer);}
        }
        struct Source{
            private int tag;private int length;
            public Source(int id,int count){tag=id;length=count;}
            public Source::Iterator GetEnumerator(){starts=starts*10+tag;return new Source::Iterator(tag,length);}
            public struct Iterator{
                private String token;private int tag;private int length;private int index;
                public Iterator(int id,int count){
                    token=s"iterator";
                    if(failure==1&&id==2)throw new Exception("right construction",71);
                    tag=id;length=count;index=0;active++;created++;
                }
                public bool MoveNext(){
                    advances=advances*10+tag;
                    if(failure==2&&tag==1)throw new Exception("left advance",72);
                    if(failure==3&&tag==2)throw new Exception("right advance",73);
                    if(index==length)return false;index++;return true;
                }
                public int Current{get{if(index==0||index>length)throw new Exception("invalid current");return tag*10+index;}}
                ~Iterator(){active--;destroyed++;}
            }
        }
        int Add(int left,int right){callbacks++;return left+right;}
        String Text(int left,int right){callbacks++;String result=s"result";if(failure==4)throw new Exception("selector",74);return move result;}
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ConstructionAdvanceOrderAndEarlyTerminationCleanNoncopyableIterators(int optimization)
        => Run(OwningSources+"""
            int main(){
                var left=new Enumerable<int,Source,Source::Iterator>(new Source(1,3));
                var right=new Enumerable<int,Source,Source::Iterator>(new Source(2,2));
                var query=left.Zip(right,Add);
                if(starts!=0||active!=0||allocations!=0)return 1;
                {
                    var first=query.GetEnumerator();if(starts!=12||active!=2)return 2;
                    if(!first.MoveNext()||first.Current!=32||first.Current!=32||callbacks!=2)return 3;
                    if(!first.MoveNext()||first.Current!=34)return 4;
                    advances=0;if(first.MoveNext()||advances!=12)return 5;
                    advances=0;if(first.MoveNext()||advances!=0)return 6;
                }
                if(active!=0||created!=destroyed||allocations!=releases)return 7;
                starts=0;advances=0;
                if(query.Take(0).Count()!=0||starts!=12||advances!=0||active!=0)return 8;
                foreach(int value in query){if(value!=32)return 9;break;}
                if(active!=0||created!=destroyed||allocations!=releases)return 10;
                for(int mode=1;mode<=3;mode++){
                    failure=mode;bool caught=false;starts=0;advances=0;
                    try{query.Count();}catch(ref Exception error){if(error.GetCode()!=70+mode)return 11;caught=true;}
                    if(!caught||active!=0||created!=destroyed||allocations!=releases)return 12;
                }
                failure=0;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ProjectedOwnersAreIndependentAndSelectorFailuresReleaseAllState(int optimization)
        => Run(OwningSources+"""
            int main(){
                var left=new Enumerable<int,Source,Source::Iterator>(new Source(1,3));
                var right=new Enumerable<int,Source,Source::Iterator>(new Source(2,2));
                var query=left.Zip(right,Text);
                if(query.Count()!=2||callbacks!=0||active!=0)return 1;
                {
                    var first=query.GetEnumerator();
                    try{first.Current;return 2;}catch(ref Exception error){}
                    if(!first.MoveNext())return 3;
                    String a=first.Current;String b=first.Current;
                    if(a.Data==b.Data||a.Length!=6||b.Length!=6||callbacks!=2)return 4;
                    a[0]='X';if(b[0]!='r')return 5;
                    if(!first.MoveNext()||first.MoveNext())return 6;
                    try{first.Current;return 7;}catch(ref Exception error){}
                }
                if(active!=0||allocations!=releases)return 8;
                failure=4;try{query.ToVector();return 9;}catch(ref Exception error){if(error.GetCode()!=74)return 10;}
                failure=0;
                if(active!=0||created!=destroyed||allocations!=releases)return 11;
                {
                    Vector<String> values=query.ToVector();
                    if(values.Count!=2||values[0].Length!=6||values[1].Length!=6||active!=0)return 12;
                }
                if(active!=0||created!=destroyed||allocations!=releases)return 13;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OwningElementsOnBothSidesAreCopiedForValueSelectorParameters(int optimization)
        => Run(OwningSources+"""
            nuint Mutate(String left,String right){
                callbacks++;nuint length=left.Length+right.Length;
                left[0]='X';left.Clear();right[0]='Y';right.Clear();return length;
            }
            nuint SumLengths(nuint total,nuint value)=>total+value;
            int main(){
                {
                    Vector<String> left=new();left.Add(s"one");left.Add(s"two");
                    Vector<String> right=new();right.Add(s"blue");right.Add(s"red");
                    var query=left.AsEnumerable().Zip(right.AsEnumerable(),Mutate);
                    int before=allocations;int baseline=allocations-releases;
                    if(query.Count()!=2||callbacks!=0||allocations!=before)return 1;
                    if(query.Aggregate((nuint)0,SumLengths)!=13||callbacks!=2)return 2;
                    if(left[0].Length!=3||left[0][0]!='o'||left[1].Length!=3||left[1][0]!='t')return 3;
                    if(right[0].Length!=4||right[0][0]!='b'||right[1].Length!=3||right[1][0]!='r')return 4;
                    if(allocations-releases!=baseline)return 5;
                }
                if(allocations!=releases)return 6;return 0;
            }
            """,optimization);

    private static void Run(string program,int optimization)
    {
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("zip.gf",program));
        var result=CompilerTestHelper.Run(sources,optimization);
        Assert.True(result.ExitCode==0,$"Exit {result.ExitCode}: {result.StandardError}");
    }
}
