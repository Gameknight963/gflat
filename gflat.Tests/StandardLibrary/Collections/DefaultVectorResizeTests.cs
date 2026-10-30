using gflat.CompileExceptions;
using System.Text;
using System.Text.RegularExpressions;

namespace gflat.Tests.Std.Collections;

public sealed class DefaultVectorResizeTests
{
    private const string Allocator = """
        using std; using std::Text; using std::Memory; using std::Collections;
        int attempts=0; int allocated=0; int freed=0; bool deny=false; nuint firstDenied=0;
        extern void*? malloc(nuint bytes); extern void free(void* storage);
        namespace Memory {
            public replace void*? Allocate(nuint bytes) {
                attempts++; if(deny){if(firstDenied==0)firstDenied=bytes;return null;}
                void*? result=malloc(bytes);if(result!=null)allocated++;return result;
            }
            public replace void Free(void* storage)nothrow{freed++;free(storage);}
        }
        """;

    private static IEnumerable<SourceFile> Sources(string code)
        => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("default-vector-resize.gf", code));

    private static void Run(string code, int optimization)
    {
        var result = CompilerTestHelper.Run(Sources(code), optimization);
        Assert.True(result.ExitCode == 0, $"Resize exit {result.ExitCode}: {result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ScalarLengthsAndContentsMatchHostModel(int optimization)
    {
        var model = new List<int>();
        var random = new Random(38720);
        var sizes = new List<int> { 0, 1, 1, 17, 2, 0, 32 };
        sizes.AddRange(Enumerable.Range(0, 24).Select(_ => random.Next(33)));
        var code = new StringBuilder(Allocator + "int main(){ { Vector<int> values=new();");
        int capacity = 0;
        foreach (int size in sizes)
        {
            if (size < model.Count) model.RemoveRange(size, model.Count - size);
            while (model.Count < size) model.Add(0);
            capacity = Math.Max(capacity, size);
            code.Append($"values.Resize({size});if(values.Count!={size}||values.Capacity!={capacity})return 1;");
            for (int i = 0; i < model.Count; i++) code.Append($"if(values[{i}]!={model[i]})return 2;");
            if (size != 0) { model[^1] = size + 7; code.Append($"values[{size - 1}]={size + 7};"); }
        }
        code.Append("}if(allocated!=freed)return 3;return 0;}");
        Run(code.ToString(), optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SpareCapacityEqualShrinkAndRegrowthPreserveAddressesWithoutAllocation(int optimization)
        => Run(Allocator + """
            int main(){
                {
                    Vector<int> values=new();values.Reserve(12);values.Resize(4);
                    int* origin=&values[0];values[0]=41;values[3]=99;
                    int before=attempts;deny=true;
                    values.Resize(4);values.Resize(2);values.Resize(10);
                    if(attempts!=before||values.Capacity!=12||&values[0]!=origin||values[0]!=41)return 1;
                    for(nuint i=1;i<10;i++)if(values[i]!=0||&values[i]!=origin+i)return 2;
                    values.Resize(0);values.Resize(0);values.Resize(12);
                    if(attempts!=before||&values[0]!=origin)return 3;
                    for(nuint i=0;i<12;i++)if(values[i]!=0)return 4;
                    deny=false;
                }
                return allocated-freed;
            }
            """, optimization);

    private const string Owners = """
        int made=0;int dead=0;int calls=0;int failAt=-1;int[64] destroyed;
        nuint failureAddress=0;
        class Failure:Exception {
            public Failure():base("original default constructor",73){}
        }
        struct Owner {
            public int Id;public String Token;
            public Owner(){
                int index=calls;calls++;
                if(index==failAt){
                    try{throw new Failure();}
                    catch(ref Failure error){failureAddress=(&error).reinterpret<nuint>();throw;}
                }
                Id=made;Token=s"owned";made++;
            }
            ~Owner(){destroyed[dead]=Id;dead++;}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NoncopyableOwnersAndConstructorRollbackRetainOriginalExceptionAndResources(int optimization)
        => Run(Allocator + Owners + """
            int main(){
                for(int roomy=0;roomy<2;roomy++)for(int failure=0;failure<3;failure++){
                    made=0;dead=0;calls=0;failAt=-1;
                    {
                        Vector<Owner> values=new();values.Reserve(2);values.Resize(2);
                        if(roomy==1)values.Reserve(8);
                        nuint origin=(&values[0]).reinterpret<nuint>();nuint token=(values[0].Token.Data).reinterpret<nuint>();
                        int live=allocated-freed;failAt=calls+failure;bool caught=false;
                        try{values.Resize(5);return 1;}
                        catch(ref Failure error){
                            caught=(&error).reinterpret<nuint>()==failureAddress&&error.GetCode()==73
                                &&CString::Equals(error.GetMessage(),"original default constructor");
                        }
                        if(!caught)return 21;
                        if(values.Count!=2)return 22;
                        if(made!=2+failure)return 23;
                        if(dead!=failure)return 24;
                        if(values.Capacity!=5&&roomy==0)return 3;
                        if(roomy==1&&(values.Capacity!=8||(&values[0]).reinterpret<nuint>()!=origin))return 4;
                        if(roomy==0&&(&values[0]).reinterpret<nuint>()==origin)return 5;
                        if(values[0].Id!=0||values[1].Id!=1||(values[0].Token.Data).reinterpret<nuint>()!=token)return 6;
                        if(allocated-freed!=live)return 7;
                        for(int i=0;i<failure;i++)if(destroyed[i]!=1+failure-i)return 8;
                        failAt=-1;values.Resize(4);values.Resize(1);
                        if(dead!=failure+3||values[0].Id!=0)return 9;
                        if(destroyed[failure]!=3+failure||destroyed[failure+1]!=2+failure||destroyed[failure+2]!=1)return 10;
                    }
                    if(made!=dead||allocated!=freed||destroyed[dead-1]!=0)return 11;
                }
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AllocationAndOverflowFailuresOccurBeforeAnyDefaultConstructor(int optimization)
        => Run(Allocator + Owners + """
            int main(){
                {
                    Vector<Owner> values=new();values.Resize(2);
                    Owner* origin=&values[0];int before=calls;int allocationsBefore=attempts;
                    deny=true;bool caught=false;
                    try{values.Resize(5);return 1;}catch(ref BadAllocException error){caught=true;}
                    deny=false;
                    if(!caught||calls!=before||attempts<=allocationsBefore||firstDenied!=5*(nuint)sizeof(Owner)||values.Count!=2||values.Capacity!=2||&values[0]!=origin)return 2;
                    caught=false;
                    try{values.Resize((nuint)0-(nuint)1);return 3;}
                    catch(ref Exception error){caught=CString::Equals(error.GetMessage(),"Vector capacity overflow");}
                    if(!caught||calls!=before||values.Count!=2||values.Capacity!=2||&values[0]!=origin)return 4;
                }
                if(made!=dead||allocated!=freed)return 5;return 0;
            }
            """, optimization);

    [Theory]
    [InlineData("struct Item{public Item(int value){}}", "Item")]
    [InlineData("struct Item{private Item(){}}", "Item")]
    [InlineData("", "int*")]
    [InlineData("class Node{}", "Node^")]
    public void ConstructionRequirementsAreLazyAndRejectedOnlyWhenOverloadIsCalled(string declaration, string type)
    {
        string code = Allocator + declaration + $" int main(){{Vector<{type}> values=new(); /*resize*/ return 0;}}";
        _ = Compiler.Emit(Sources(code));
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Sources(code.Replace("/*resize*/", "values.Resize(0);"))));
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    [Fact]
    public void DocumentedInitializedIoBufferExampleCompiles()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Guides/std/core/Collections/OWNING_COLLECTIONS.md");
        string example = Regex.Matches(File.ReadAllText(path), @"(?ms)^```cpp\r?\n(?<code>.*?)^```")
            .Select(match => match.Groups["code"].Value)
            .Single(code => code.Contains("buffer.Resize(4096)", StringComparison.Ordinal));
        _ = Compiler.Emit(Sources(example));
    }
}
