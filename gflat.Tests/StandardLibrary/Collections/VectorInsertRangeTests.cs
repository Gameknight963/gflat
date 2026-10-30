using gflat.CompileExceptions;

namespace gflat.Tests.Std.Collections;

public sealed class VectorInsertRangeTests
{
    private static IEnumerable<SourceFile> Sources(string code)
        => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("main.gf", code));

    private static void Run(string code, int optimization)
        => Assert.Equal(0, CompilerTestHelper.Run(Sources(code), optimization).ExitCode);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SmallSelfAndSeparateRangesMatchListWithAndWithoutGrowth(int optimization)
    {
        string path = Path.Combine(Path.GetTempPath(), $"gflat-insert-range-{Guid.NewGuid():N}.bin");
        try
        {
            int rows = 0;
            using (var output = new BinaryWriter(File.Create(path)))
            {
                for (int count = 0; count <= 8; count++)
                for (int start = 0; start <= count; start++)
                for (int length = 0; length <= count - start; length++)
                for (int index = 0; index <= count; index++)
                for (int separate = 0; separate <= 1; separate++)
                {
                    var expected = Enumerable.Range(0, count).ToList();
                    expected.InsertRange(index, Enumerable.Range(start, length)
                        .Select(value => value + separate * 100).ToArray());
                    output.Write(count); output.Write(start); output.Write(length);
                    output.Write(index); output.Write(separate);
                    for (int i = 0; i < 16; i++) output.Write(i < expected.Count ? expected[i] : -1);
                    rows++;
                }
            }
            Run("""
                using std; using std::Text; using std::Memory; using std::Collections;
                extern void* fopen(readonly(char)* path, readonly(char)* mode);
                extern nuint fread(void* buffer, nuint size, nuint count, void* stream);
                extern int fclose(void* stream);
                int main() {
                    void* stream = fopen("PATH", "rb");
                    int[21] row; int processed = 0;
                    while (fread((void*)(int*)row, 84, 1, stream) == 1) {
                        processed++;
                        for (int roomy = 0; roomy < 2; roomy++) {
                            Vector<int> values = new();
                            nuint reserved = (nuint)row[0]; if (roomy == 1) reserved = 32;
                            values.Reserve(reserved);
                            for (int i = 0; i < row[0]; i++) values.Add(i);
                            int[8] other;
                            for (nuint i = 0; i < 8; i++) other[i] = (int)i + 100;
                            Span<readonly(int)> source = values.AsImmutable().Slice((nuint)row[1], (nuint)row[2]);
                            if (row[4] == 1) source = new((readonly(int)*)other + row[1], (nuint)row[2]);
                            values.InsertRange((nuint)row[3], source);
                            if (values.Count != (nuint)(row[0] + row[2])) return 1;
                            for (nuint i = 0; i < values.Count; i++) if (values[i] != row[i + 5]) return 2;
                            if (roomy == 1 && values.Capacity != 32) return 3;
                        }
                    }
                    if (processed != ROWS) return 4;
                    return fclose(stream);
                }
                """.Replace("PATH", path.Replace("\\", "/")).Replace("ROWS", rows.ToString()), optimization);
        }
        finally { File.Delete(path); }
    }

    private const string Owners = """
        using std; using std::Text; using std::Memory; using std::Collections;
        int made=0; int dead=0; int budget=-1; int copies=0;
        int allocations=0; int releases=0; bool deny=false;
        extern void*? malloc(nuint bytes); extern void free(void* storage);
        namespace Memory {
            public replace void*? Allocate(nuint bytes) { if(deny)return null; allocations++;return malloc(bytes); }
            public replace void Free(void* storage) nothrow { releases++;free(storage); }
        }
        struct Owner {
            public int Id; public String Text;
            public Owner(int id) { Id=id;Text=s"owned";made++; }
            public copy Owner(immutable(Owner)* source) {
                Id=source.Id;Text=source.Text;copies++;
                if(budget==0)throw new Exception("copy failed",37);
                if(budget>0)budget--;made++;
            }
            ~Owner(){dead++;}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryCopyFailureRollsBackAndSuccessMovesWithoutExtraCopies(int optimization)
        => Run(Owners + """
            int main() {
                for(int roomy=0;roomy<2;roomy++)for(int failure=0;failure<3;failure++) {
                    budget=-1;
                    Vector<Owner> values=new();nuint reserved=3;if(roomy==1)reserved=16;values.Reserve(reserved);
                    for(int i=0;i<3;i++)values.Add(new Owner(i));
                    int live=allocations-releases;int before=copies;budget=failure;
                    try{values.InsertRange(1,values.AsImmutable());return 1;}
                    catch(ref Exception error){if(error.code!=37)return 2;}
                    if(values.Count!=3||copies-before!=failure+1||made-dead!=3)return 3;
                    for(nuint i=0;i<3;i++)if(values[i].Id!=(int)i||values[i].Text[0]!='o')return 4;
                    if(allocations-releases!=live)return 5;
                    budget=-1;before=copies;int destroyed=dead;
                    values.InsertRange(1,values.AsImmutable());
                    int[6] expected=[0,0,1,2,1,2];
                    for(nuint i=0;i<6;i++)if(values[i].Id!=expected[i])return 6;
                    if(copies-before!=3||dead!=destroyed)return 7;
                    values[0].Text[0]='X';if(values[1].Text[0]!='o')return 8;
                }
                if(made!=dead||allocations!=releases)return 9;return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AllocationBoundsAndEmptyPreflightPreserveOwners(int optimization)
        => Run(Owners + """
            int main(){
                {
                    Vector<Owner> values=new();values.Reserve(3);
                    for(int i=0;i<3;i++)values.Add(new Owner(i));
                    immutable(Owner)* address=values.AsImmutable().Data!;
                    nuint capacity=values.Capacity;int before=copies;deny=true;
                    try{values.InsertRange(1,values.AsImmutable());return 1;}catch(ref BadAllocException error){}
                    deny=false;
                    if(values.Count!=3||values.Capacity!=capacity||values.AsImmutable().Data!=address||copies!=before)return 2;
                    Span<immutable(Owner)> invalid=new(((nuint)1).reinterpret<immutable(Owner)*>(),1);
                    try{values.InsertRange(4,invalid);return 3;}catch(ref Exception error){}
                    Span<immutable(Owner)> empty=new();
                    try{values.InsertRange(4,empty);return 4;}catch(ref Exception error){}
                    deny=true;values.InsertRange(3,empty);deny=false;
                    if(values.Count!=3||values.Capacity!=capacity||copies!=before)return 5;
                    for(nuint i=0;i<3;i++)if(values[i].Id!=(int)i||values[i].Text[0]!='o')return 6;
                }
                if(made!=dead||allocations!=releases)return 7;return 0;
            }
            """, optimization);

    [Fact]
    public void CopyRequirementAppliesOnlyWhenInsertRangeIsCalled()
    {
        const string program = """
            using std; using std::Text; using std::Memory; using std::Collections;
            struct Unique {
                private int* destroyed;
                public Unique(int* counter){destroyed=counter;}
                ~Unique(){(*destroyed)++;}
            }
            int main(){int destroyed=0;{
                Vector<Unique> values=new();values.Add(new Unique(&destroyed));
                values.Insert(0,new Unique(&destroyed));
                // INSERT
            }return destroyed-2;}
            """;
        Run(program, 0);
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Sources(
            program.Replace("// INSERT", "values.InsertRange(0,values.AsImmutable());"))));
        Assert.Contains("cannot pass 'immutable(Unique)' as 'Unique'", error.Message);
    }
}
