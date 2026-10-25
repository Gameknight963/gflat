using System.Text;

namespace gflat.Tests;

public sealed class StandardQueryHashSetTests
{
    private const string Allocator="""
        using std; using std::Queries; using std::Collections;extern void*? malloc(nuint size);extern void free(void* storage);
        int allocations=0;int releases=0;int attempts=0;int failureIndex=0;
        namespace Memory{
            public replace void*? Allocate(nuint size){
                attempts++;if(failureIndex!=0&&attempts==failureIndex)return null;
                void*? result=malloc(size);if(result!=null)allocations++;return result;
            }
            public replace void Free(void* storage) nothrow{releases++;free(storage);}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void LazyPipelinesMatchDotNetHashSetAndCanBeMaterializedRepeatedly(int optimization)
    {
        string path=Path.Combine(Path.GetTempPath(),"gflat_query_hash_set_"+Guid.NewGuid().ToString("N")+".bin");
        try{
            var random=new Random(82521);
            using(var output=new BinaryWriter(File.Create(path)))for(int sample=0;sample<100;sample++){
                int count=sample%33,skip=sample%7,take=sample%13;
                int[] values=Enumerable.Range(0,count).Select(_=>random.Next(-20,21)).ToArray();
                int[] expected=values.Where(value=>value%2==0).Select(value=>value%7).Skip(skip).Take(take).ToHashSet().Order().ToArray();
                output.Write(count);output.Write(skip);output.Write(take);output.Write(expected.Length);
                foreach(int value in values)output.Write(value);foreach(int value in expected)output.Write(value);
            }
            Run(Allocator+$$"""
                extern void*? fopen(readonly(char)* path,readonly(char)* mode);
                extern nuint fread(void* data,nuint size,nuint count,void* stream);
                extern int fclose(void* stream);extern int ferror(void* stream);
                bool Keep(int value)=>value%2==0;
                int Project(int value)=>value%7;
                int main(){
                    void*? file=fopen({{Literal(path)}},"rb");if(file==null)return 1;defer{fclose(file!);}
                    int[4] header;int[32] values;int[32] expected;int seen=0;
                    while(true){
                        nuint bytes=fread((void*)&header[0],1,16,file!);if(bytes==0)break;if(bytes!=16)return 2;
                        nuint count=(nuint)header[0];nuint expectedCount=(nuint)header[3];
                        if(count>32||expectedCount>32)return 3;
                        for(nuint i=0;i<32;i++)values[i]=0;
                        if(fread((void*)&values[0],4,count,file!)!=count||fread((void*)&expected[0],4,expectedCount,file!)!=expectedCount)return 4;
                        int[32] original=values;
                        {
                            Span<int> view=new(values,count);
                            var query=view.AsEnumerable().Where(Keep).Select(Project).Skip((nuint)header[1]).Take((nuint)header[2]);
                            HashSet<int> first=query.ToHashSet();HashSet<int> second=query.ToHashSet();
                            if(first.Count!=expectedCount||second.Count!=expectedCount)return 5;
                            for(nuint i=0;i<expectedCount;i++)if(!first.Contains(&expected[i])||!second.Contains(&expected[i]))return 6;
                            first.Clear();if(second.Count!=expectedCount)return 7;
                        }
                        for(nuint i=0;i<count;i++)if(values[i]!=original[i])return 8;
                        if(allocations!=releases)return 9;seen++;
                    }
                    if(ferror(file!)!=0||seen!=100)return 10;return 0;
                }
                """,optimization);
        }finally{File.Delete(path);}
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void StringResultsOwnIndependentCopiesAndDuplicateArgumentsAreDestroyed(int optimization)
        =>Run(Allocator+"""
            int main(){
                {
                    Vector<String> words=new();words.Add(s"same");words.Add(s"same");words.Add(s"other");
                    int baseline=allocations-releases;
                    words.AsEnumerable().ToHashSet();
                    if(allocations-releases!=baseline)return 1;
                    HashSet<String> values=words.AsEnumerable().ToHashSet();
                    if(values.Count!=2||words.Count!=3||words[0].Length!=4||words[1].Length!=4||words[2].Length!=5)return 2;
                    var iterator=values.GetEnumerator();nuint visited=0;
                    while(iterator.MoveNext()){
                        readonly(char)* buffer=iterator.Current.Data;
                        for(nuint i=0;i<words.Count;i++)if(buffer==words[i].Data)return 3;
                        visited++;
                    }
                    if(visited!=2)return 4;
                    words[0][0]='X';words[1][0]='Y';words[2][0]='Z';words.Clear();
                    String first=s"same";String second=s"other";
                    if(!values.Contains(&first)||!values.Contains(&second))return 5;
                }
                if(allocations!=releases)return 6;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CallbackCopyIteratorAndEveryAllocationFailureCleanPartialResultsAndKeepSourceOwners(int optimization)
        =>Run(Allocator+"""
            int mode=0;int live=0;int created=0;int destroyed=0;int active=0;
            int copies=0;int hashes=0;int comparisons=0;int projections=0;
            struct Key : IHashable,IEquatable<Key>{
                public int Id;private String text;
                public Key(int id){Id=id;text=s"owned";live++;}
                public copy Key(immutable(Key)* source){
                    Id=source.Id;text=source.text;copies++;
                    if(mode==7&&copies==3)throw new Exception("copy",77);
                    live++;
                }
                public immutable nuint GetHashCode(){
                    hashes++;if(mode==5&&hashes==3)throw new Exception("hash",75);return 0;
                }
                public immutable bool Equals(immutable(Key)* other){
                    comparisons++;if(mode==6&&comparisons==3)throw new Exception("equality",76);return Id==other.Id;
                }
                ~Key(){live--;}
            }
            struct Source{
                private Span<Key> values;
                public Source(Span<Key> source){values=source;}
                public struct Iterator{
                    private Span<Key> values;private nuint position;private String token;
                    public Iterator(Span<Key> source){
                        values=source;position=0;token=s"iterator";
                        if(mode==1)throw new Exception("iterator construction",71);
                        created++;active++;
                    }
                    public bool MoveNext(){
                        if(mode==2&&position==2)throw new Exception("advance",72);
                        if(position==values.Length)return false;position++;return true;
                    }
                    public ref Key Current{
                        get{
                            if(mode==3&&position==3)throw new Exception("current",73);
                            return values[position-1];
                        }
                    }
                    ~Iterator(){destroyed++;active--;}
                }
                public Iterator GetEnumerator()=>new Iterator(values);
            }
            Key Project(Key value){
                projections++;if(mode==4&&projections==3)throw new Exception("selector",74);return move value;
            }
            int main(){
                {
                    Vector<Key> values=new();values.Add(new Key(0));values.Add(new Key(1));values.Add(new Key(0));values.Add(new Key(2));
                    var query=new Enumerable<Key,Source,Source::Iterator>(new Source(values.AsSpan())).Select(Project);
                    int balance=allocations-releases;
                    {
                        HashSet<Key> result=query.ToHashSet();
                        if(result.Count!=3||live!=7||active!=0||created!=destroyed)return 1;
                    }
                    if(live!=4||allocations-releases!=balance)return 2;
                    for(int failure=1;failure<=7;failure++){
                        copies=0;hashes=0;comparisons=0;projections=0;mode=failure;bool caught=false;
                        try{query.ToHashSet();}catch(ref Exception error){if(error.GetCode()!=70+failure)return 3;caught=true;}
                        mode=0;
                        if(!caught||live!=4||active!=0||created!=destroyed||allocations-releases!=balance)return 4;
                        if(values.Count!=4||values[0].Id!=0||values[1].Id!=1||values[2].Id!=0||values[3].Id!=2)return 5;
                    }
                    int allocationPoints=0;
                    {attempts=0;HashSet<Key> measured=query.ToHashSet();allocationPoints=attempts;}
                    if(allocationPoints==0||live!=4||allocations-releases!=balance)return 6;
                    for(int failure=1;failure<=allocationPoints;failure++){
                        attempts=0;failureIndex=failure;bool caught=false;
                        try{query.ToHashSet();}catch(ref BadAllocException error){caught=true;}
                        failureIndex=0;
                        if(!caught||live!=4||active!=0||created!=destroyed||allocations-releases!=balance)return 7;
                    }
                }
                if(live!=0||allocations!=releases||created!=destroyed||active!=0)return 8;return 0;
            }
            """,optimization);

    private static string Literal(string value)=>"\""+value.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";
    private static IEnumerable<SourceFile> Sources(string code)
        =>Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std/core"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("query-hash-set.gf",code));
    private static void Run(string code,int optimization)
    {
        var result=CompilerTestHelper.Run(Sources(code),optimization);
        Assert.True(result.ExitCode==0,$"Exit {result.ExitCode}: {result.StandardError}");
    }
}
