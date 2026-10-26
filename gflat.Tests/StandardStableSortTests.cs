namespace gflat.Tests;

public sealed class StandardStableSortTests
{
    private static IEnumerable<SourceFile> Sources(string code)
        => new[] { "Collections/IEnumerable.gf", "Queries/Enumerable.gf", "Collections/stable_sort.gf",
            "Collections/Vector.gf", "Memory/Span.gf", "Text/String.gf", "Text/ByteSearch.gf", "IStringConvertable.gf",
            "IHashable.gf", "IEquatable.gf", "IComparable.gf", "Hashing/primitive_hashing.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", name))))
            .Append(new SourceFile("main.gf", code));

    private static void Run(string code, int optimization)
        => Assert.Equal(0, CompilerTestHelper.Run(Sources(code), optimization).ExitCode);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void StreamedDuplicateKeysAndDirectionsMatchStableOrderBy(int optimization)
    {
        string path = Path.Combine(Path.GetTempPath(), $"gflat-stable-sort-{Guid.NewGuid():N}.bin");
        try
        {
            var random = new Random(80497);
            int rows = 0;
            using (var output = new BinaryWriter(File.Create(path)))
            {
                foreach (int length in Enumerable.Range(0, 66).Concat(new[] { 127, 128, 129, 255, 256, 257 }))
                for (int pattern = 0; pattern < 4; pattern++)
                for (int descending = 0; descending < 2; descending++)
                {
                    int[] keys = Enumerable.Range(0, length).Select(i => pattern switch
                    {
                        0 => 7,
                        1 => i / 3,
                        2 => (length - i) / 3,
                        _ => random.Next(9) - 4
                    }).ToArray();
                    Write(keys, descending);
                }
                for (int i = 0; i < 256; i++)
                {
                    int[] keys = Enumerable.Range(0, random.Next(513))
                        .Select(_ => random.Next(4) == 0 ? random.Next(int.MinValue, int.MaxValue) : random.Next(17) - 8)
                        .ToArray();
                    Write(keys, i % 2);
                }
                void Write(int[] keys, int descending)
                {
                    int[] ids = Enumerable.Range(0, keys.Length).ToArray();
                    int[] expected = (descending == 0 ? ids.OrderBy(i => keys[i]) : ids.OrderByDescending(i => keys[i])).ToArray();
                    output.Write(keys.Length); output.Write(descending);
                    foreach (int key in keys) output.Write(key);
                    foreach (int id in expected) output.Write(id);
                    rows++;
                }
            }
            Run("""
                using std; using std::Text; using std::Memory; using std::Queries; using std::Collections;
                extern void* fopen(readonly(char)* path, readonly(char)* mode);
                extern nuint fread(void* data, nuint size, nuint count, void* stream);
                extern int fclose(void* stream);
                int descending=0;
                struct Item {
                    public int Key; public int Id;
                    public Item(int key,int id){Key=key;Id=id;}
                }
                int Compare(immutable(Item)* left,immutable(Item)* right){
                    int order=0;if(left.Key<right.Key)order=-1;if(left.Key>right.Key)order=1;
                    if(descending!=0)return -order;return order;
                }
                int main(){
                    void* stream=fopen("PATH","rb");int[2] header;int rows=0;
                    while(fread((void*)(int*)header,8,1,stream)==1){
                        rows++;descending=header[1];Vector<Item> values=new();
                        for(int i=0;i<header[0];i++){
                            int key=0;if(fread(&key,4,1,stream)!=1)return 1;
                            values.Add(new Item(key,i));
                        }
                        StableSort<Item>(values.AsSpan(),Compare);
                        for(nuint i=0;i<values.Count;i++){
                            int expected=0;if(fread(&expected,4,1,stream)!=1)return 2;
                            if(values[i].Id!=expected)return 3;
                        }
                    }
                    if(rows!=ROWS)return 4;return fclose(stream);
                }
                """.Replace("PATH", path.Replace("\\", "/")).Replace("ROWS", rows.ToString()), optimization);
        }
        finally { File.Delete(path); }
    }

    private const string Owners = """
        using std; using std::Text; using std::Memory; using std::Queries; using std::Collections;
        int made=0;int dead=0;int budget=-1;int comparisons=0;
        int allocations=0;int releases=0;int denyAfter=-1;bool inconsistent=false;
        extern void*? malloc(nuint bytes);extern void free(void* storage);
        namespace Memory{
            public replace void*? Allocate(nuint bytes){
                if(denyAfter==0)return null;if(denyAfter>0)denyAfter--;
                allocations++;return malloc(bytes);
            }
            public replace void Free(void* storage) nothrow{releases++;free(storage);}
        }
        struct Owner{
            public int Key;public int Id;public String Text;
            public Owner(int key,int id){Key=key;Id=id;Text=s"owned";made++;}
            ~Owner(){dead++;}
        }
        int Compare(immutable(Owner)* left,immutable(Owner)* right){
            comparisons++;
            if(budget==0)throw new Exception("comparison failed",37);
            if(budget>0)budget--;
            if(inconsistent){if(comparisons%2==0)return -1;return 1;}
            if(left.Key<right.Key)return -1;if(left.Key>right.Key)return 1;return 0;
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryComparisonFailurePreservesNoncopyableOwnersAndReleasesScratch(int optimization)
        => Run(Owners + """
            int main(){
                int total=0;
                {
                    Vector<Owner> values=new();for(int i=0;i<16;i++)values.Add(new Owner((i*7)%4,i));
                    StableSort<Owner>(values.AsSpan(),Compare);total=comparisons;
                }
                for(int failure=0;failure<total;failure++){
                    budget=-1;Vector<Owner> values=new();
                    for(int i=0;i<16;i++)values.Add(new Owner((i*7)%4,i));
                    int live=allocations-releases;int destroyed=dead;comparisons=0;budget=failure;
                    try{StableSort<Owner>(values.AsSpan(),Compare);return 1;}
                    catch(ref Exception error){if(error.code!=37)return 2;}
                    if(comparisons!=failure+1||allocations-releases!=live||dead!=destroyed)return 3;
                    for(nuint i=0;i<16;i++){
                        if(values[i].Id!=(int)i||values[i].Key!=((int)i*7)%4||values[i].Text[0]!='o')return 4;
                    }
                }
                if(made!=dead||allocations!=releases)return 5;return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AllocationsArePreflightedAndOwnerCyclesDoNotCopyOrDestroy(int optimization)
        => Run(Owners + """
            int main(){
                for(int failure=0;failure<2;failure++){
                    Vector<Owner> values=new();for(int i=0;i<9;i++)values.Add(new Owner((i*7)%4,i));
                    int live=allocations-releases;int destroyed=dead;comparisons=0;denyAfter=failure;
                    try{StableSort<Owner>(values.AsSpan(),Compare);return 1;}catch(ref BadAllocException error){}
                    denyAfter=-1;
                    for(nuint i=0;i<9;i++)if(values[i].Id!=(int)i)return 2;
                    if(allocations-releases!=live||dead!=destroyed||comparisons!=0)return 3;
                }
                {
                    Span<Owner> empty=new();Owner single=new Owner(1,0);Span<Owner> one=new(&single,1);
                    comparisons=0;denyAfter=0;
                    StableSort<Owner>(empty,Compare);StableSort<Owner>(one,Compare);denyAfter=-1;
                    if(comparisons!=0||single.Id!=0)return 4;
                    Span<Owner> huge=new((Owner*)1,(nuint)0-(nuint)1);
                    int before=allocations-releases;
                    try{StableSort<Owner>(huge,Compare);return 5;}catch(ref Exception error){}
                    if(comparisons!=0||allocations-releases!=before)return 6;
                }
                for(int length=2;length<100;length++){
                    Vector<Owner> values=new();for(int i=0;i<length;i++)values.Add(new Owner((i*7)%4,i));
                    int destroyed=dead;int created=made;int before=allocations;budget=-1;
                    StableSort<Owner>(values.AsSpan(),Compare);
                    if(dead!=destroyed||made!=created||allocations!=before+2)return 7;
                    for(nuint i=1;i<values.Count;i++){
                        if(values[i-1].Key>values[i].Key)return 8;
                        if(values[i-1].Key==values[i].Key&&values[i-1].Id>values[i].Id)return 9;
                    }
                }
                {
                    Vector<Owner> values=new();for(int i=0;i<31;i++)values.Add(new Owner(i%4,i));
                    inconsistent=true;StableSort<Owner>(values.AsSpan(),Compare);
                    bool[31] seen;
                    for(nuint i=0;i<31;i++)seen[i]=false;
                    for(nuint i=0;i<31;i++){
                        int id=values[i].Id;if(id<0||id>=31||seen[id])return 10;seen[id]=true;
                    }
                }
                if(made!=dead||allocations!=releases)return 11;return 0;
            }
            """, optimization);
}
