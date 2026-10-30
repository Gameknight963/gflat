using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class InterfaceViewCastTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ValueViewsBorrowOwnersAndMaterializeRvaluesUntilNormalOrExceptionalFullExpressionExit(int optimization)
        => Run("""
            int created=0;int destroyed=0;int copied=0;int evaluated=0;
            interface ICounter{int Add(int amount);immutable int Read();int Fail();}
            struct Counter:ICounter{
                public int Value;
                public Counter(int value){Value=value;created++;}
                public copy Counter(immutable(Counter)* source){Value=source.Value;copied++;created++;}
                public int Add(int amount){Value+=amount;return Value;}
                public immutable int Read()=>Value;
                public int Fail(){throw new Exception("view failure",61);}
                ~Counter(){destroyed++;}
            }
            struct NonCopy:ICounter{
                public int Value;
                public NonCopy(int value){Value=value;created++;}
                public int Add(int amount){Value+=amount;return Value;}
                public immutable int Read()=>Value;
                public int Fail(){return Value;}
                ~NonCopy(){destroyed++;}
            }
            Counter Make(){evaluated++;return new Counter(10);}
            int Generic<T:ICounter>(ref T value)=>((ICounter)value).Read();
            int main(){
                {
                    Counter value=new(1);
                    if(((ICounter)value).Add(2)!=3||value.Value!=3||copied!=0)return 1;
                    Counter* pointer=&value;if(((ICounter)pointer).Add(4)!=7||value.Value!=7)return 2;
                    readonly(Counter)* qualified=&value;if(((ICounter)qualified).Read()!=7)return 3;
                    ICounter* prior=&value;if(((ICounter)prior).Add(1)!=8||value.Value!=8)return 4;
                    if(Generic<Counter>(ref value)!=8||copied!=0)return 5;
                    NonCopy owner=new(20);if(((ICounter)owner).Add(1)!=21||owner.Value!=21)return 6;
                    int before=destroyed;int result=((ICounter)Make()).Read();
                    if(result!=10||evaluated!=1||destroyed!=before+1||copied!=0)return 7;
                    before=destroyed;result=((ICounter)new Counter(11)).Read();if(result!=11||destroyed!=before+1)return 8;
                    if(attempts!=0)return 9;
                    before=destroyed;bool caught=false;
                    try{((ICounter)new Counter(12)).Fail();}catch(ref Exception error){caught=error.GetCode()==61;}
                    if(!caught||destroyed!=before+1||copied!=0)return 10;
                }
                if(created!=destroyed||copied!=0)return 11;return 0;
            }
            """, optimization, denyAllocation: true);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PrimitiveGenericAndBaseClassViewsUseExistingInterfaceDispatchWithoutAllocation(int optimization)
        => Run("""
            interface IRead{
                immutable int Read();
                implementation int{public immutable int Read()=>*this;}
            }
            interface IValue<T>{immutable T Get();}
            struct Box<T>:IValue<T>{public T Value;public Box(T value){Value=value;}public immutable T Get()=>Value;}
            class Base:IRead{public immutable virtual int Read()=>1;}
            class Derived:Base{public immutable override int Read()=>42;}
            int main(){
                int value=17;if(((IRead)value).Read()!=17||((IRead)23).Read()!=23)return 21;
                Box<int> box=new(37);if(((IValue<int>)box).Get()!=37)return 22;
                Derived derived=new();Base* basePointer=&derived;
                if(((IRead)basePointer).Read()!=42||((IRead)(*basePointer)).Read()!=42)return 23;
                IRead* existing=basePointer;if(existing.Read()!=42||((IRead)existing).Read()!=42)return 24;
                if(((readonly(IRead))derived).Read()!=42)return 25;
                if(attempts!=0)return 26;return 0;
            }
            """, optimization, denyAllocation: true);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FreshAndReturnedManagedReceiversRemainRootedThroughCollectAndRetireAfterTheCall(int optimization)
        => Run("""
            alias GC=Collector;
            interface IRead{immutable int Read();}
            static class Collector:Standard::IGarbageCollector{
                private static void*? allocation;
                private static void*?[128] slots;
                private static nuint count;
                public static int Allocations;public static int Releases;public static bool Bad;
                public static void* Allocate(nuint bytes,nuint alignment){
                    if(allocation!=null)throw new Exception("Unexpected live allocation");
                    allocation=Memory::Allocate(bytes);if(allocation==null)throw new Exception("Collector allocation failed");
                    Allocations++;return allocation!;
                }
                public static void ReferenceAdded(void* slot)nothrow{
                    for(nuint i=0;i<count;i++)if(slots[i]==slot)return;
                    for(nuint i=0;i<count;i++)if(slots[i]==null){slots[i]=slot;return;}
                    if(count==128){Bad=true;return;}slots[count]=slot;count++;
                }
                public static void ReferenceRemoved(void* slot)nothrow{
                    for(nuint i=0;i<count;i++)if(slots[i]==slot){slots[i]=null;return;}
                }
                public static nuint Collect(){
                    if(allocation==null)return 0;
                    for(nuint i=0;i<count;i++){
                        if(slots[i]!=null){void*? target=*((void*?*)slots[i]!);if(target==allocation)return 0;}
                    }
                    Memory::Free(allocation!);allocation=null;Releases++;return 1;
                }
            }
            class Node:IRead{
                private int value;
                public Node(int number){value=number;}
                public immutable int Read(){if(Collector::Collect()!=0)return -999;return value;}
            }
            Node^ Make()=>new^ Node(42);
            int main(){
                int first=((IRead)new^ Node(41)).Read();if(first!=41||Collector::Bad||Collector::Collect()!=1)return 31;
                int second=((IRead)Make()).Read();if(second!=42||Collector::Bad||Collector::Collect()!=1)return 32;
                {
                    Node^ owner=new^ Node(43);if(((IRead)owner).Read()!=43||Collector::Collect()!=0||Collector::Bad)return 33;
                }
                if(Collector::Collect()!=1||Collector::Bad||Collector::Allocations!=3||Collector::Releases!=3)return 34;
                if(rawAllocations!=3||rawReleases!=3)return 35;return 0;
            }
            """, optimization, managed: true, countAllocation: true);

    [Theory]
    [InlineData("struct Missing{} int main(){Missing value=new();return ((IRead)value).Read();}", "implement")]
    [InlineData("int main(){Value value=new();IRead stored=(IRead)value;return 0;}", "value type")]
    [InlineData("int main(){Value value=new();var stored=(IRead)value;return 0;}", "value type")]
    [InlineData("int main(){Value value=new();IRead* stored=&((IRead)value);return 0;}", "value type")]
    [InlineData("int main(){Value value=new();IRead* stored=(IRead)value;return 0;}", "IRead*")]
    [InlineData("int main(){Value*? value=null;return ((IRead)value).Read();}", "non-null")]
    [InlineData("int main(){readonly(Value) value=new();return ((IMutable)value).Mutate();}", "readonly")]
    [InlineData("int main(){immutable(Value) value=new();return ((IMutable)value).Mutate();}", "immutable")]
    [InlineData("static interface IStatic{int Read();} static class Provider:IStatic{public static int Read()=>1;} int main(){return ((IStatic)new Value()).Read();}", "Static interface")]
    public void InvalidViewsHaveSourceDiagnosticsAndCannotBeStored(string code, string diagnostic)
    {
        const string contract = """
            interface IRead{immutable int Read();}
            interface IMutable{int Mutate();}
            struct Value:IRead,IMutable{public immutable int Read()=>1;public int Mutate()=>2;}
            """;
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Sources(contract + code),
            options: new() { NoStdLib = true }));
        Assert.Contains(diagnostic, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<SourceFile> Sources(string code, bool managed = false, bool denyAllocation = false, bool countAllocation = false)
    {
        var files = new List<string> { "core/Exception.gf", "libc/Memory/allocation.gf" };
        if (managed) files.Add("core/IGarbageCollector.gf");
        var sources = files.Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard", name))))
            .Append(new SourceFile("interface-view-tests.gf", code)).ToList();
        if (denyAllocation) sources.Add(new SourceFile("no-allocation.gf", """
            int attempts=0;
            namespace Memory{
                public replace void*? Allocate(nuint size){attempts++;return null;}
                public replace void Free(void* address)nothrow{}
            }
            """));
        if (countAllocation) sources.Add(new SourceFile("count-allocation.gf", """
            int rawAllocations=0;int rawReleases=0;
            namespace Memory{
                namespace Imports{private extern void*? malloc(nuint size);private extern void free(void* address);}
                public replace void*? Allocate(nuint size){rawAllocations++;return Imports::malloc(size);}
                public replace void Free(void* address)nothrow{rawReleases++;Imports::free(address);}
            }
            """));
        return sources;
    }

    private static void Run(string code, int optimization, bool managed = false, bool denyAllocation = false, bool countAllocation = false)
    {
        string ir = Compiler.Emit(Sources(code, managed, denyAllocation, countAllocation),
            options: new() { NoStdLib = true, InternalizeFunctions = true });
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }
}
