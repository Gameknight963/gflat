using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class StandardVectorResizeTests
{
    private static IEnumerable<SourceFile> Sources(string code)
        => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("vector-resize.gf", code));

    private static void Run(string code, int optimization)
    {
        var result = CompilerTestHelper.Run(Sources(code), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ResizeTraceMatchesListAndOrdinaryGeometricGrowth(int optimization)
    {
        var random = new Random(618523);
        var model = new List<int>();
        var operations = new List<(int Count, int Fill)> { (0, 1), (1, 7), (1, 9), (17, 3), (2, 4), (0, 5), (32, 6) };
        for (int i = 0; i < 40; i++) operations.Add((random.Next(33), random.Next(10)));
        using var packet = new MemoryStream();
        foreach (var operation in operations)
        {
            if (operation.Count < model.Count) model.RemoveRange(operation.Count, model.Count - operation.Count);
            while (model.Count < operation.Count) model.Add(operation.Fill);
            packet.WriteByte((byte)operation.Count);
            packet.WriteByte((byte)operation.Fill);
            foreach (int value in model) packet.WriteByte((byte)value);
        }
        string hex = Convert.ToHexString(packet.ToArray());
        Run(Memory + $$$"""
            nuint ReadByte(readonly(char)* data, ref nuint offset) {
                nuint value=0;
                for(int i=0;i<2;i++) {
                    char digit=data[offset];offset++;
                    nuint part=(nuint)((int)digit-(int)'0');
                    if(digit>='A')part=(nuint)((int)digit-(int)'A'+10);
                    value=value*16+part;
                }
                return value;
            }
            int main() {
                {
                    Vector<int> values=new();Vector<int> ordinary=new();
                    readonly(char)* packet="{{{hex}}}";nuint offset=0;
                    for(int row=0;row<{{{operations.Count}}};row++) {
                        nuint count=ReadByte(packet,ref offset);int fill=(int)ReadByte(packet,ref offset);
                        values.Resize(count,fill);
                        if(count<=ordinary.Count)ordinary.Truncate(count);
                        else while(ordinary.Count<count)ordinary.Add(fill);
                        if(values.Count!=count||values.Capacity!=ordinary.Capacity)return 1;
                        for(nuint i=0;i<count;i++)if(values[i]!=(int)ReadByte(packet,ref offset))return 2;
                    }
                    if(offset!={{{hex.Length}}})return 5;
                    Vector<nuint> scratch=new();scratch.Reserve(64);
                    int before=attempts;deny=true;
                    scratch.Resize(64,(nuint)0);scratch.Resize<nuint>(3,0);scratch.Resize(3,(nuint)9);
                    deny=false;
                    if(attempts!=before||scratch.Count!=3||scratch[0]!=0||scratch.Capacity!=64)return 3;
                }
                if(allocations!=releases)return 4;
                return 0;
            }
            """, optimization);

        const string unique = """
            using std;
            struct Unique {
                private int* counter;
                public Unique(int* value){counter=value;}
                ~Unique(){(*counter)++;}
            }
            int main(){int destroyed=0;{
                Vector<Unique> values=new();values.Add(new Unique(&destroyed));
                // RESIZE
                values.Truncate(0);
            }return destroyed-1;}
            """;
        Run(unique, optimization);
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Sources(
            unique.Replace("// RESIZE", "values.Resize(0,new Unique(&destroyed));"))));
        Assert.Contains("Unique", error.Message);
        Assert.Contains("cannot pass 'Unique' as 'Unique'", error.Message);
        Assert.Contains("Argument 1", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CapturedOwnersAndEveryCopyOrAllocationFailureKeepValidPrefixes(int optimization)
        => Run(Memory + Owners + """
            int main() {
                {
                    Vector<Owner> values=new();values.Add(new Owner(7));
                    int before=copies;values.Resize(9,values[0]);
                    if(copies-before!=9||values.Count!=9)return 1;
                    values[0].Text[0]='X';
                    if(values[8].Text[0]!='o')return 2;
                    before=copies;values.Resize(1,values[8]);
                    if(copies-before!=1||values.Count!=1||values[0].Text[0]!='X')return 3;
                    before=copies;values.Resize(1,values[0]);
                    if(copies-before!=1)return 4;
                }
                for(int failure=0;failure<4;failure++) {
                    {
                        budget=-1;Vector<Owner> values=new();values.Reserve(8);
                        values.Add(new Owner(1));values.Add(new Owner(2));
                        int before=copies;budget=failure;
                        try{values.Resize(5,values[0]);return 5;}
                        catch(ref Exception error){if(error.GetCode()!=37)return 6;}
                        budget=-1;
                        nuint expected=2;if(failure>1)expected+=(nuint)(failure-1);
                        if(values.Count!=expected||copies-before!=failure+1)return 7;
                        if(values[0].Id!=1||values[1].Id!=2)return 8;
                        for(nuint i=2;i<values.Count;i++)if(values[i].Id!=1||values[i].Text[0]!='o')return 9;
                    }
                    if(made!=dead||allocations!=releases)return 10;
                }
                int requests=0;
                {
                    Vector<Owner> values=new();values.Reserve(4);
                    for(int i=0;i<4;i++)values.Add(new Owner(i));
                    attempts=0;values.Resize(10,values[0]);requests=attempts;
                }
                if(requests==0)return 11;
                for(int failure=1;failure<=requests;failure++) {
                    {
                        Vector<Owner> values=new();values.Reserve(4);
                        for(int i=0;i<4;i++)values.Add(new Owner(i));
                        attempts=0;failAt=failure;
                        try{values.Resize(10,values[0]);return 12;}
                        catch(ref BadAllocException error){}
                        failAt=0;
                        if(values.Count<4||values.Count>=10)return 13;
                        for(nuint i=0;i<values.Count;i++) {
                            int expected=0;if(i<4)expected=(int)i;
                            if(values[i].Id!=expected||values[i].Text[0]!='o')return 14;
                        }
                        values.Resize(10,values[0]);
                        if(values.Count!=10)return 15;
                    }
                    if(made!=dead||allocations!=releases)return 16;
                }
                return 0;
            }
            """, optimization);

    private const string Memory = """
        using std;
        int attempts=0;int allocations=0;int releases=0;int failAt=0;bool deny=false;
        extern void*? malloc(nuint bytes);extern void free(void* value);
        namespace Memory {
            public replace void*? Allocate(nuint bytes) {
                attempts++;
                if(deny||attempts==failAt)return null;
                void*? result=malloc(bytes);if(result!=null)allocations++;return result;
            }
            public replace void Free(void* value)nothrow{releases++;free(value);}
        }
        """;

    private const string Owners = """
        int made=0;int dead=0;int copies=0;int budget=-1;
        struct Owner {
            public int Id;public String Text;
            public Owner(int id){Id=id;Text=s"owned";made++;}
            public copy Owner(immutable(Owner)* source) {
                Id=source.Id;Text=source.Text;copies++;
                if(budget==0)throw new Exception("copy failed",37);
                if(budget>0)budget--;made++;
            }
            ~Owner(){dead++;}
        }
        """;
}



