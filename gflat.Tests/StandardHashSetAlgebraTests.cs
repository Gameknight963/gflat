namespace gflat.Tests;

public sealed class StandardHashSetAlgebraTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void IntersectionAndSymmetricDifferenceMatchDotNetIncludingTombstonesAndSelf(int optimization)
    {
        var random=new Random(712203);int[] left=new int[48],right=new int[48],intersection=new int[48],symmetric=new int[48];
        for(int i=0;i<left.Length;i++){
            left[i]=i<4?new[]{0,0,131071,131071}[i]:random.Next(131072);
            right[i]=i<4?new[]{0,131071,0,131071}[i]:random.Next(131072);
            var a=Enumerable.Range(-8,17).Where(key=>(left[i]&(1<<(key+8)))!=0).ToHashSet();
            var b=Enumerable.Range(-8,17).Where(key=>(right[i]&(1<<(key+8)))!=0).ToHashSet();
            var x=new HashSet<int>(a);x.IntersectWith(b);intersection[i]=x.Sum(key=>1<<(key+8));
            x=new(a);x.SymmetricExceptWith(b);symmetric[i]=x.Sum(key=>1<<(key+8));
        }
        Run($$"""
            using std; using std::Collections;
            int Mask(immutable(HashSet<int>)* set){
                int result=0;for(int key=-8;key<=8;key++)if(set.Contains(&key))result=result|(1<<(key+8));
                return result;
            }
            int main(){
                int[48] left=[{{string.Join(",",left)}}];int[48] right=[{{string.Join(",",right)}}];
                int[48] intersection=[{{string.Join(",",intersection)}}];int[48] symmetric=[{{string.Join(",",symmetric)}}];
                for(nuint sample=0;sample<48;sample++){
                    HashSet<int> target=new();HashSet<int> toggled=new();HashSet<int> source=new();
                    for(int key=-8;key<=8;key++){
                        if((left[sample]&(1<<(key+8)))!=0){target.Add(key);toggled.Add(key);}
                        if((right[sample]&(1<<(key+8)))!=0)source.Add(key);
                    }
                    int extra=99;target.Add(extra);target.Remove(&extra);toggled.Add(extra);toggled.Remove(&extra);source.Add(extra);source.Remove(&extra);
                    nuint capacity=target.Capacity;target.IntersectWith(&source);
                    if(Mask(&target)!=intersection[sample]||target.Capacity!=capacity||Mask(&source)!=right[sample])return 1;
                    toggled.SymmetricExceptWith(&source);
                    if(Mask(&toggled)!=symmetric[sample]||Mask(&source)!=right[sample])return 2;
                    nuint count=target.Count;target.IntersectWith(&target);if(target.Count!=count||Mask(&target)!=intersection[sample])return 3;
                    toggled.SymmetricExceptWith(&toggled);if(!toggled.IsEmpty)return 4;
                }
                return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void IntersectionOwnsNoncopyableCollisionKeysAndKeepsCompletedRemovalsOnFailure(int optimization)
        => Run("""
            using std; using std::Collections;extern void*? malloc(nuint count);extern void free(void* storage);
            int allocations=0;int releases=0;int dead=0;int budget=-1;bool failHash=false;
            namespace Memory{
                public replace void*? Allocate(nuint count){allocations++;return malloc(count);}
                public replace void Free(void* storage) nothrow{releases++;free(storage);}
            }
            struct Key:IHashable,IEquatable<Key>{
                public int Id;public Key(int id){Id=id;}
                public immutable nuint GetHashCode(){if(failHash)throw new Exception("hash");return 0;}
                public immutable bool Equals(immutable(Key)* other){if(budget==0)throw new Exception("equal");if(budget>0)budget--;return Id==other.Id;}
                ~Key(){dead++;}
            }
            int main(){
                {HashSet<Key> target=new();HashSet<Key> source=new();
                 for(int i=1;i<=5;i++)target.Add(new Key(i));for(int i=3;i<=6;i++)source.Add(new Key(i));
                 nuint capacity=target.Capacity;int before=allocations;
                 target.IntersectWith(&target);target.IntersectWith(&source);
                 if(target.Count!=3||source.Count!=4||dead!=2||target.Capacity!=capacity||allocations!=before)return 1;
                 var iterator=target.GetEnumerator();int mask=0;while(iterator.MoveNext())mask=mask|(1<<iterator.Current.Id);
                 if(mask!=56)return 2;}
                if(dead!=9||allocations!=releases)return 3;dead=0;
                {HashSet<Key> target=new();HashSet<Key> source=new();
                 for(int i=1;i<=5;i++)target.Add(new Key(i));for(int i=3;i<=5;i++)source.Add(new Key(i));
                 failHash=true;try{target.IntersectWith(&source);return 4;}catch(ref Exception error){}failHash=false;
                 if(target.Count!=5||dead!=0||source.Count!=3)return 5;
                 budget=4;try{target.IntersectWith(&source);return 6;}catch(ref Exception error){}budget=-1;
                 if(target.Count!=4||dead!=1||source.Count!=3)return 7;
                 var iterator=target.GetEnumerator();int mask=0;while(iterator.MoveNext())mask=mask|(1<<iterator.Current.Id);
                 if(mask!=60)return 8;
                 HashSet<Key> empty=new();int before=allocations;target.IntersectWith(&empty);
                 if(!target.IsEmpty||dead!=5||source.Count!=3||allocations!=before)return 9;}
                if(dead!=8||allocations!=releases)return 10;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SymmetricDifferenceCopiesOnlyMissingKeysAndPreservesEarlierTogglesOnCopyFailure(int optimization)
        => Run("""
            using std; using std::Collections;int made=0;int dead=0;int copies=0;bool denyCopy=false;bool denyAlloc=false;
            extern void*? malloc(nuint count);extern void free(void* storage);
            namespace Memory{
                public replace void*? Allocate(nuint count){if(denyAlloc)return null;return malloc(count);}
                public replace void Free(void* storage) nothrow{free(storage);}
            }
            struct Key:IHashable,IEquatable<Key>{
                public int Id;public Key(int id){Id=id;made++;}
                public copy Key(immutable(Key)* source){if(denyCopy)throw new Exception("copy");Id=source.Id;made++;copies++;}
                public immutable nuint GetHashCode()=>0;
                public immutable bool Equals(immutable(Key)* other)=>Id==other.Id;
                ~Key(){dead++;}
            }
            int main(){
                {HashSet<Key> target=new();HashSet<Key> source=new();
                 target.Add(new Key(1));target.Add(new Key(2));target.Add(new Key(3));source.Add(new Key(2));source.Add(new Key(4));
                 target.SymmetricExceptWith(&source);
                 if(target.Count!=3||source.Count!=2||copies!=1||dead!=1)return 1;
                 var iterator=target.GetEnumerator();int mask=0;while(iterator.MoveNext())mask=mask|(1<<iterator.Current.Id);
                 if(mask!=26)return 2;}
                if(made!=dead||made!=6)return 3;made=0;dead=0;copies=0;
                {HashSet<Key> target=new();HashSet<Key> source=new();
                 target.Add(new Key(1));target.Add(new Key(2));target.Add(new Key(3));source.Add(new Key(2));source.Add(new Key(4));
                 denyCopy=true;try{target.SymmetricExceptWith(&source);return 4;}catch(ref Exception error){}denyCopy=false;
                 if(target.Count!=2||source.Count!=2||copies!=0||dead!=1)return 5;
                 var iterator=target.GetEnumerator();int mask=0;while(iterator.MoveNext())mask=mask|(1<<iterator.Current.Id);
                 if(mask!=10)return 6;}
                if(made!=dead||made!=5)return 7;made=0;dead=0;copies=0;
                {HashSet<Key> target=new();HashSet<Key> source=new();
                 for(int i=1;i<=4;i++)target.Add(new Key(i));
                 source.Add(new Key(2));source.Add(new Key(5));source.Add(new Key(6));
                 denyAlloc=true;try{target.SymmetricExceptWith(&source);return 8;}catch(ref BadAllocException error){}denyAlloc=false;
                 if(target.Count!=4||source.Count!=3||copies!=2||dead!=2)return 9;
                 var iterator=target.GetEnumerator();int mask=0;while(iterator.MoveNext())mask=mask|(1<<iterator.Current.Id);
                 if(mask!=58)return 10;}
                if(made!=dead||made!=9)return 11;return 0;
            }
            """,optimization);

    [Fact]
    public void CopyingExtensionRequiresCopyableElements()
    {
        const string program="""
            using std; using std::Collections;
            struct Key:IHashable,IEquatable<Key>{
                public immutable nuint GetHashCode()=>0;
                public immutable bool Equals(immutable(Key)* other)=>true;
                ~Key(){}
            }
            int main(){HashSet<Key> first=new();HashSet<Key> second=new();first.SymmetricExceptWith(&second);return 0;}
            """;
        Compiler.Emit(Sources(program.Replace("first.SymmetricExceptWith(&second);",
            "first.IntersectWith(&second);first.ExceptWith(&second);",StringComparison.Ordinal)));
        var error=Assert.Throws<gflat.CompileExceptions.TypeCheckException>(()=>Compiler.Emit(Sources(program)));
        // Without a copy constructor the immutable borrowed source key cannot
        // become the independent mutable owning argument required by Add.
        Assert.Contains("cannot pass 'immutable(Key)' as 'Key'",error.Description,StringComparison.Ordinal);
        Assert.Contains(".Add",error.Description,StringComparison.Ordinal);
    }

    private static IEnumerable<SourceFile> Sources(string code)
        => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("main.gf",code));
    private static void Run(string code,int optimization)
        => Assert.Equal(0,CompilerTestHelper.Run(Sources(code),optimization).ExitCode);
}
