using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class StandardQuerySearchTests
{
    private const string Allocator="""
        using std; using std::Text; using std::Memory; using std::Queries; using std::Collections;extern void*? malloc(nuint size);extern void free(void* storage);
        int allocations=0;int releases=0;
        namespace Memory{
            public replace void*? Allocate(nuint size){void*? result=malloc(size);if(result!=null)allocations++;return result;}
            public replace void Free(void* storage)nothrow{releases++;free(storage);}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PrimitiveSearchesMatchIndependentLinqOraclesWithoutAllocation(int optimization)
    {
        var random=new Random(730815);
        var data=new List<int>();var manifest=new List<int>();
        for(int sample=0;sample<64;sample++){
            int[] values=Enumerable.Range(0,sample%32).Select(_=>random.Next(-8,9)).ToArray();
            int key=random.Next(-8,9),take=sample%7;
            manifest.AddRange([values.Length,key,take,values.Any(value=>value==3)?1:0,
                values.Contains(key)?1:0,values.Any(value=>Math.Abs(value)==Math.Abs(key))?1:0,
                values.Where(value=>value%2==0).Select(value=>value+1).Take(take).Contains(key)?1:0]);
            data.AddRange(values);
        }
        Run($$"""
            using std; using std::Text; using std::Memory; using std::Queries; using std::Collections;int allocations=0;
            namespace Memory{public replace void*? Allocate(nuint size){allocations++;return null;}public replace void Free(void* storage)nothrow{} }
            bool Three(int value)=>value==3;bool ReadOnlyThree(readonly(int) value)=>value==3;
            bool Even(int value)=>value%2==0;bool Positive(int value)=>value>=-8;
            int Increment(int value)=>value+1;
            bool Same(immutable(int)* left,immutable(int)* right)=>*left==*right;
            bool Absolute(immutable(int)* left,immutable(int)* right){int a=*left;int b=*right;if(a<0)a=-a;if(b<0)b=-b;return a==b;}
            struct Plain{public int Value;public Plain(int value){Value=value;} }
            bool EqualPlain(immutable(Plain)* left,immutable(Plain)* right)=>left.Value==right.Value;
            int main(){
                int[{{data.Count}}] data=[{{string.Join(',',data)}}];
                int[{{manifest.Count}}] manifest=[{{string.Join(',',manifest)}}];nuint offset=0;
                for(nuint sample=0;sample<64;sample++){
                    nuint row=sample*7;int key=manifest[row+1];
                    Span<int> view=new(&data[offset],(nuint)manifest[row]);var query=view.AsEnumerable();
                    if(query.Any(&Three)!=(manifest[row+3]!=0)||query.Any()!=(view.Length!=0)||!query.All(&Positive))return 1;
                    if(query.Contains(&key)!=(manifest[row+4]!=0)||query.Contains(&key,&Same)!=(manifest[row+4]!=0))return 2;
                    if(query.Contains(&key,&Absolute)!=(manifest[row+5]!=0))return 3;
                    if(query.Where(&Even).Select(&Increment).Take((nuint)manifest[row+2]).Contains(&key)!=(manifest[row+6]!=0))return 4;
                    if(std::Collections::Contains<int>(view.AsImmutable(),&key,&Same)!=(manifest[row+4]!=0))return 5;
                    if(query.Contains(&key)!=(manifest[row+4]!=0))return 6;
                    if(view.AsReadOnly().AsEnumerable().Any(&ReadOnlyThree)!=(manifest[row+3]!=0))return 9;
                    if(view.AsImmutable().AsEnumerable().Contains(&key,&Same)!=(manifest[row+4]!=0))return 10;
                    offset+=view.Length;
                }
                Plain[2] values=[new Plain(1),new Plain(2)];Plain key=new(2);Span<Plain> view=new(values,2);
                if(!view.AsEnumerable().Contains(&key,&EqualPlain)||values[0].Value!=1||values[1].Value!=2)return 7;
                if(allocations!=0)return 8;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SearchesReadCurrentOnceAndStopBeforeAThrowingSuffix(int optimization)
        =>Run("""
            using std; using std::Text; using std::Memory; using std::Queries; using std::Collections;int starts=0;int ends=0;int moves=0;int reads=0;int calls=0;int target=0;int count=5;bool poison=false;
            struct Source{
                public struct Iterator{
                    private int position;public Iterator(){position=0;starts++;}
                    public bool MoveNext(){moves++;if(poison&&position==1)throw new Exception("suffix");if(position==count)return false;position++;return true;}
                    public int Current{get{reads++;return position;}}
                    ~Iterator(){ends++;}
                }
                public Iterator GetEnumerator()=>new Iterator();
            }
            bool Match(int value){calls++;return value==target;}
            bool Same(immutable(int)* left,immutable(int)* right){calls++;return *left==*right;}
            void Reset(){moves=0;reads=0;calls=0;}
            int main(){
                var query=new Enumerable<int,Source,Source::Iterator>(new Source());
                int[4] targets=[1,3,5,9];
                for(nuint sample=0;sample<4;sample++){
                    target=targets[sample];int visited=target;if(visited>count)visited=count;bool expected=target<=count;
                    int expectedMoves=visited;if(!expected)expectedMoves++;
                    Reset();if(query.Any(&Match)!=expected||reads!=visited||calls!=visited||moves!=expectedMoves||starts!=ends)return 1;
                    Reset();if(query.Contains(&target,&Same)!=expected||reads!=visited||calls!=visited||moves!=expectedMoves||starts!=ends)return 2;
                    Reset();if(query.Contains(&target)!=expected||reads!=visited||moves!=expectedMoves||starts!=ends)return 3;
                }
                Reset();if(!query.Any()||reads!=0||moves!=1||starts!=ends)return 4;
                count=0;Reset();if(query.Any(&Match)||query.Contains(&target,&Same)||reads!=0||calls!=0||moves!=2||starts!=ends)return 5;
                count=5;target=1;poison=true;Reset();
                if(!query.Any(&Match)||!query.Contains(&target)||!query.Contains(&target,&Same)||reads!=3||moves!=3||starts!=ends)return 6;
                return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OwningCurrentsAndIteratorsCleanUpOnEveryExitAndTheKeyIsNeverCopied(int optimization)
        =>Run(Allocator+"""
            int mode=0;int live=0;int starts=0;int ends=0;int calls=0;int copies=0;
            struct Key:IEquatable<Key>{
                public int Id;private String token;
                public Key(int id){Id=id;token=s"element";live++;}
                public copy Key(immutable(Key)* source){
                    Id=source.Id;token=source.token;copies++;
                    if(Id==99)throw new Exception("key copied",99);
                    if(mode==5&&copies==2)throw new Exception("copy",75);
                    live++;
                }
                public immutable bool Equals(immutable(Key)* other){calls++;if(mode==4&&calls==2)throw new Exception("comparison",74);return Id==3&&other.Id==99;}
                ~Key(){live--;}
            }
            struct Source{
                public struct Iterator{
                    private int position;private String token;
                    public Iterator(){position=0;token=s"iterator";starts++;}
                    public bool MoveNext(){if(mode==2&&position==1)throw new Exception("advance",72);if(position==3)return false;position++;return true;}
                    public Key Current{get{if(mode==3&&position==2)throw new Exception("current",73);return new Key(position);}}
                    ~Iterator(){ends++;}
                }
                public Iterator GetEnumerator()=>new Iterator();
            }
            bool Match(Key value){calls++;if(mode==1&&calls==2)throw new Exception("predicate",71);return value.Id==3;}
            bool Compare(immutable(Key)* left,immutable(Key)* right)=>left.Equals(right);
            int main(){
                {
                    Key key=new(99);var query=new Enumerable<Key,Source,Source::Iterator>(new Source());int baseline=allocations-releases;
                    if(!query.Any(&Match)||!query.Contains(&key)||!query.Contains(&key,&Compare))return 1;
                    if(live!=1||starts!=ends||allocations-releases!=baseline||key.Id!=99)return 2;
                    for(int failure=1;failure<=4;failure++){
                        mode=failure;calls=0;bool caught=false;
                        try{if(failure==1)query.Any(&Match);else if(failure==4)query.Contains(&key,&Compare);else query.Contains(&key);}
                        catch(ref Exception error){if(error.GetCode()!=70+failure)return 3;caught=true;}
                        mode=0;if(!caught||live!=1||starts!=ends||key.Id!=99||allocations-releases!=baseline)return 4;
                    }
                    Vector<Key> values=new();values.Add(new Key(1));values.Add(new Key(2));values.Add(new Key(3));
                    int ownedBaseline=allocations-releases;
                    for(int operation=0;operation<2;operation++){
                        copies=0;mode=5;bool caught=false;
                        try{if(operation==0)values.AsEnumerable().Contains(&key);else values.AsEnumerable().Any(&Match);}
                        catch(ref Exception error){if(error.GetCode()!=75)return 5;caught=true;}
                        mode=0;if(!caught||live!=4||allocations-releases!=ownedBaseline||key.Id!=99||values[0].Id!=1||values[1].Id!=2||values[2].Id!=3)return 6;
                    }
                    copies=0;if(!values.AsEnumerable().Contains(&key)||copies!=3||live!=4)return 7;
                }
                if(live!=0||starts!=ends||allocations!=releases)return 8;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ManagedCurrentAndBorrowedKeyRemainRootedDuringCallbacks(int optimization)
        =>Run("""
            using std; using std::Text; using std::Memory; using std::Queries; using std::Collections;class Item{public int Value;public Item(int value){Value=value;}}
            bool Two(Item^ value){GC::Collect();return value.Value==2;}
            bool Same(immutable(Item^)* left,immutable(Item^)* right){GC::Collect();return (*left).Value==(*right).Value;}
            int main(){
                Vector<Item^> values=new();values.Add(new^ Item(1));values.Add(new^ Item(2));values.Add(new^ Item(3));
                Item^ key=new^ Item(2);var query=values.AsEnumerable();
                if(!query.Any(&Two)||!query.Contains(&key,&Same)||key.Value!=2)return 1;
                GC::Collect();if(values[0].Value!=1||values[1].Value!=2||values[2].Value!=3)return 2;
                return 0;
            }
            """,optimization,managed:true);

    [Fact]
    public void DefaultEqualityRequiresItsDeclaredContract()
    {
        const string source="using std; using std::Text; using std::Memory; using std::Queries; using std::Collections;struct Plain{public int Value;}int main(){Plain[1] data=[default];Span<Plain> view=new(data,1);Plain key=default;std::Queries::Contains(view.AsEnumerable(),&key);return 0;}";
        var error=Assert.Throws<TypeCheckException>(()=>Compiler.Check(Sources(source)));
        Assert.Contains("No matching overload for 'Contains'",error.Message);
    }

    [Fact]
    public void QualifiedDefaultEqualityRequiresItsExactElementContract()
    {
        const string source="using std; using std::Text; using std::Memory; using std::Queries; using std::Collections;int main(){int[1] data=[1];Span<int> view=new(data,1);int key=1;std::Queries::Contains(view.AsImmutable().AsEnumerable(),&key);return 0;}";
        var error=Assert.Throws<TypeCheckException>(()=>Compiler.Check(Sources(source)));
        Assert.Contains("No matching overload for 'Contains'",error.Message);
    }

    private static IEnumerable<SourceFile> Sources(string source,bool managed=false)
    {
        string root=Path.Combine(AppContext.BaseDirectory,"Std");
        var files=Directory.GetFiles(Path.Combine(root,"core"),"*.gf",SearchOption.AllDirectories).AsEnumerable();
        if(managed)files=files.Concat(new[]{Path.Combine(root,"libc/GC.gf"),Path.Combine(root,"libc/TracingCollector.gf")});
        return files.Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("query-search.gf",source));
    }
    private static void Run(string source,int optimization,bool managed=false)
    {
        var result=CompilerTestHelper.Run(Sources(source,managed),optimization);
        Assert.True(result.ExitCode==0,$"Query search exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
