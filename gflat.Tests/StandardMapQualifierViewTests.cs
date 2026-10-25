using System.Text;
using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class StandardMapQualifierViewTests
{
    private const string Types="""
        using std;
        int destroyed=0;
        int allocations=0;
        extern void*? malloc(nuint bytes);extern void free(void* pointer);
        namespace Memory{
            public replace void*? Allocate(nuint bytes){allocations++;return malloc(bytes);}
            public replace void Free(void* pointer) nothrow{free(pointer);}
        }
        struct Cell{public int Value;public Cell(int value){Value=value;}}
        struct Payload{
            public int Inline;public int* Pointer;public Cell^ Managed;
            public Payload(int* pointer,Cell^ reference){Inline=3;Pointer=pointer;Managed=reference;}
            ~Payload(){destroyed++;}
        }
        int Compare(immutable(int)* left,immutable(int)* right){if(*left<*right)return -1;if(*left>*right)return 1;return 0;}
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ShallowAndImmutableViewsBorrowNoncopyablePayloadsWithDistinctProtection(int optimization)
    {
        var code=new StringBuilder(Types+"int main(){int item=10;Cell^ cell=new(20);int key=1;int missing=99;\n");
        foreach(string map in new[]{"HashMap","SortedMap"})
        {
            string argument=map=="SortedMap"?"&Compare":"";
            code.AppendLine($$"""
                { {{map}}<int,Payload> map=new({{argument}});map.Add(1,new Payload(&item,cell));int before=allocations;int beforeDestroyed=destroyed;
                  readonly({{map}}<int,Payload>)* shallow=&map;
                  readonly(Payload)* value=shallow.FindReadOnly(&key)!;
                  *value.Pointer=30;value.Managed.Value=40;
                  immutable({{map}}<int,Payload>)* deep=&map;
                  immutable(Payload)* frozen=deep.FindImmutable(&key)!;
                  if(frozen.Inline!=3||*frozen.Pointer!=30||frozen.Managed.Value!=40)return 1;
                  if(shallow.FindReadOnly(&missing)!=null||deep.FindImmutable(&missing)!=null)return 2;
                  {{map}}<int,Payload>::Entry entry=map.Extract(&key);
                  readonly({{map}}<int,Payload>::Entry)* shallowEntry=&entry;
                  readonly(Payload)* mapped=shallowEntry.ValueReadOnly;
                  *mapped.Pointer=50;mapped.Managed.Value=60;
                  immutable({{map}}<int,Payload>::Entry)* deepEntry=&entry;
                  if(*deepEntry.Key!=1||*deepEntry.ValueImmutable.Pointer!=50||deepEntry.ValueImmutable.Managed.Value!=60)return 3;
                  if(!map.IsEmpty||allocations!=before||destroyed!=beforeDestroyed)return 4;
                }
                """);
        }
        code.AppendLine("if(destroyed!=2)return 5;return 0;}");
        var result=CompilerTestHelper.Run(Sources(code.ToString()),optimization);
        Assert.True(result.ExitCode==0,$"Exit {result.ExitCode}: {result.StandardError}");
    }

    [Theory]
    [InlineData("HashMap")]
    [InlineData("SortedMap")]
    public void ShallowInlineWritesAndImmutableTargetWritesAreRejected(string map)
    {
        string[] managed = ["libc/GC.gf", "libc/TracingCollector.gf"];
        string[] dependencies = map == "HashMap"
            ? ["core/Collections/HashMap.gf", "core/IHashable.gf", "core/IEquatable.gf", "core/Hashing.gf"]
            : ["core/Collections/SortedMap.gf", "core/Collections/Vector.gf", "core/Span.gf",
                "core/Collections/IEnumerable.gf", "core/Collections/Enumerable.gf"];
        SourceFile[] library = managed.Concat(dependencies).Select(relative =>
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Std", relative);
            return new SourceFile(path, File.ReadAllText(path));
        }).ToArray();
        string prefix = Types + $$"""
            void Test(readonly({{map}}<int,Payload>)* shallow,immutable({{map}}<int,Payload>)* deep,
                readonly({{map}}<int,Payload>::Entry)* shallowEntry,immutable({{map}}<int,Payload>::Entry)* deepEntry,int* key){
                readonly(Payload)* value=shallow.FindReadOnly(key)!;
                immutable(Payload)* frozen=deep.FindImmutable(key)!;
                readonly(Payload)* entryValue=shallowEntry.ValueReadOnly;
                immutable(Payload)* entryFrozen=deepEntry.ValueImmutable;
            """;
        IEnumerable<SourceFile> Inputs(string code)
            => library.Append(new SourceFile("views.gf", code));

        Compiler.Check(Inputs(prefix + "}"));
        string[] writes =
        [
            "value.Inline=9;", "value.Pointer=key;", "entryValue.Inline=9;",
            "*frozen.Pointer=9;", "frozen.Managed.Value=9;", "*entryFrozen.Pointer=9;",
            "entryFrozen.Managed.Value=9;", "*shallowEntry.Key=9;"
        ];
        foreach (string write in writes)
        {
            var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Inputs(prefix + write + "}")));
            Assert.Contains("readonly", error.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static IEnumerable<SourceFile> Sources(string code)
        => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("views.gf",code));
}
