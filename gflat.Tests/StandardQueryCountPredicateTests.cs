namespace gflat.Tests;

public sealed class StandardQueryCountPredicateTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ConditionalCountsMatchLinqAndExistingWhereCountWithoutAllocation(int optimization)
    {
        var random=new Random(491803);var data=new List<int>();var manifest=new List<int>();
        for(int sample=0;sample<64;sample++){
            int[] values=Enumerable.Range(0,sample%32).Select(_=>random.Next(-50,51)).ToArray();
            int skip=sample%5,take=sample%9;
            manifest.AddRange([values.Length,skip,take,values.Count(value=>value%2==0),
                values.Where(value=>value>=0).Select(value=>value+1).Skip(skip).Take(take).Count(value=>value%2==0)]);
            data.AddRange(values);
        }
        Run($$"""
            using std; using std::Collections;int allocations=0;
            namespace Memory{public replace void*? Allocate(nuint count){allocations++;return null;}public replace void Free(void* storage)nothrow{} }
            bool Even(int value)=>value%2==0;bool Keep(int value)=>value>=0;bool Always(int value)=>true;bool Never(int value)=>false;
            int Increment(int value)=>value+1;
            int main(){
                int[{{data.Count}}] data=[{{string.Join(',',data)}}];
                int[{{manifest.Count}}] manifest=[{{string.Join(',',manifest)}}];nuint offset=0;
                for(nuint sample=0;sample<64;sample++){
                    nuint row=sample*5;Span<int> view=new(&data[offset],(nuint)manifest[row]);var query=view.AsEnumerable();
                    nuint expected=(nuint)manifest[row+3];
                    if(query.Count(&Even)!=expected||query.Where(&Even).Count()!=expected||query.Count(&Even)!=expected)return 1;
                    if(query.Count(&Always)!=view.Length||query.Count(&Never)!=0||query.Count()!=view.Length)return 2;
                    var chain=query.Where(&Keep).Select(&Increment).Skip((nuint)manifest[row+1]).Take((nuint)manifest[row+2]);
                    if(chain.Count(&Even)!=(nuint)manifest[row+4]||chain.Where(&Even).Count()!=(nuint)manifest[row+4])return 3;
                    offset+=view.Length;
                }
                if(allocations!=0)return 4;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PredicateCountsReadEveryCurrentOnceAndParameterlessCountStillSkipsIt(int optimization)
        =>Run("""
            using std; using std::Collections;int starts=0;int ends=0;int moves=0;int reads=0;int calls=0;int projections=0;int count=6;int mode=0;
            struct Source{
                public struct Iterator{
                    private int position;public Iterator(){position=0;starts++;}
                    public bool MoveNext(){moves++;if(position==count)return false;position++;return true;}
                    public int Current{get{reads++;return position;}}
                    ~Iterator(){ends++;}
                }
                public Iterator GetEnumerator()=>new Iterator();
            }
            bool Predicate(int value){calls++;if(mode==3&&value==3)throw new Exception("suffix",73);if(mode==1)return true;if(mode==2)return false;return value%2==0;}
            int Project(int value){projections++;return value;}
            void Reset(){moves=0;reads=0;calls=0;projections=0;}
            int main(){
                var query=new Enumerable<int,Source,Source::Iterator>(new Source());
                for(mode=0;mode<3;mode++){
                    Reset();nuint expected=3;if(mode==1)expected=6;if(mode==2)expected=0;
                    if(query.Count(&Predicate)!=expected||reads!=6||calls!=6||moves!=7||starts!=ends)return 1;
                }
                Reset();if(query.Count()!=6||reads!=0||calls!=0||moves!=7||starts!=ends)return 2;
                mode=0;Reset();if(query.Select(&Project).Count(&Predicate)!=3||projections!=6||reads!=6||calls!=6||moves!=7||starts!=ends)return 3;
                count=0;Reset();if(query.Count(&Predicate)!=0||query.Count()!=0||reads!=0||calls!=0||moves!=2||starts!=ends)return 4;
                count=6;mode=3;Reset();bool caught=false;
                try{query.Count(&Predicate);}catch(ref Exception error){if(error.GetCode()!=73)return 5;caught=true;}
                if(!caught||reads!=3||calls!=3||moves!=3||starts!=ends)return 6;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OwningCurrentIteratorAndFailedCopiesReleaseExactlyOnce(int optimization)
        =>Run("""
            using std; using std::Collections;extern void*? malloc(nuint count);extern void free(void* storage);
            int allocations=0;int releases=0;int live=0;int starts=0;int ends=0;int calls=0;int copies=0;int mode=0;
            namespace Memory{
                public replace void*? Allocate(nuint count){void*? result=malloc(count);if(result!=null)allocations++;return result;}
                public replace void Free(void* storage)nothrow{releases++;free(storage);}
            }
            struct Owner{
                public int Id;private String text;
                public Owner(int id){Id=id;text=s"current";live++;}
                public copy Owner(immutable(Owner)* source){Id=source.Id;text=source.text;copies++;if(mode==4&&copies==2)throw new Exception("copy",74);live++;}
                ~Owner(){live--;}
            }
            struct Source{
                public struct Iterator{
                    private int position;private String token;
                    public Iterator(){position=0;token=s"iterator";starts++;}
                    public bool MoveNext(){if(mode==2&&position==2)throw new Exception("advance",72);if(position==4)return false;position++;return true;}
                    public Owner Current{get{if(mode==3&&position==3)throw new Exception("current",73);return new Owner(position);}}
                    ~Iterator(){ends++;}
                }
                public Iterator GetEnumerator()=>new Iterator();
            }
            bool Even(Owner value){calls++;if(mode==1&&calls==3)throw new Exception("predicate",71);return value.Id%2==0;}
            int main(){
                var query=new Enumerable<Owner,Source,Source::Iterator>(new Source());
                if(query.Count(&Even)!=2||live!=0||starts!=ends||allocations!=releases)return 1;
                for(int failure=1;failure<=3;failure++){
                    mode=failure;calls=0;bool caught=false;
                    try{query.Count(&Even);}catch(ref Exception error){if(error.GetCode()!=70+failure)return 2;caught=true;}
                    mode=0;if(!caught||live!=0||starts!=ends||allocations!=releases)return 3;
                }
                {
                    Vector<Owner> values=new();for(int id=1;id<=4;id++)values.Add(new Owner(id));
                    int baseline=allocations-releases;copies=0;mode=4;bool caught=false;
                    try{values.AsEnumerable().Count(&Even);}catch(ref Exception error){if(error.GetCode()!=74)return 4;caught=true;}
                    mode=0;if(!caught||live!=4||allocations-releases!=baseline||values.Count!=4)return 5;
                    for(nuint i=0;i<4;i++)if(values[i].Id!=(int)i+1)return 6;
                    copies=0;if(values.AsEnumerable().Count(&Even)!=2||copies!=4||live!=4)return 7;
                }
                if(live!=0||starts!=ends||allocations!=releases)return 8;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ManagedCallbackArgumentsRemainRootedThroughoutTheFullScan(int optimization)
        =>Run("""
            using std; using std::Collections;int calls=0;class Item{public int Value;public Item(int value){Value=value;}}
            bool Even(Item^ value){calls++;GC::Collect();return value.Value%2==0;}
            int main(){
                Vector<Item^> values=new();for(int value=1;value<=5;value++)values.Add(new^ Item(value));
                var query=values.AsEnumerable();
                if(query.Count(&Even)!=2||calls!=5)return 1;
                if(query.Count()!=5||calls!=5)return 2;
                if(query.Count(&Even)!=2||calls!=10)return 3;
                GC::Collect();for(nuint i=0;i<5;i++)if(values[i].Value!=(int)i+1)return 4;
                return 0;
            }
            """,optimization,managed:true);

    private static void Run(string source,int optimization,bool managed=false)
    {
        string root=Path.Combine(AppContext.BaseDirectory,"Std");
        IEnumerable<string> paths=Directory.GetFiles(Path.Combine(root,"core"),"*.gf",SearchOption.AllDirectories);
        if(managed)paths=paths.Concat(new[]{Path.Combine(root,"libc/GC.gf"),Path.Combine(root,"libc/TracingCollector.gf")});
        var sources=paths.Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("count-predicate.gf",source));
        var result=CompilerTestHelper.Run(sources,optimization);
        Assert.True(result.ExitCode==0,$"Count predicate exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
