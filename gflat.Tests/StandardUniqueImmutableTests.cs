using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class StandardUniqueImmutableTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DeepBorrowReadsNoncopyablePayloadWithoutAllocationCopyOrEarlyDestruction(int optimization)
        => Run("""
            using std; using std::Text; using std::Memory; using std::Collections;extern void*? malloc(nuint count);extern void free(void* data);
            int allocations=0;int dead=0;
            namespace Memory{
                public replace void*? Allocate(nuint count){allocations++;return malloc(count);}
                public replace void Free(void* data) nothrow{free(data);}
            }
            struct Payload{
                public int* Data;public Payload(int* data){Data=data;}
                public immutable int Read()nothrow=>*Data;
                ~Payload(){dead++;}
            }
            int main(){
                {int value=42;Unique<Payload> owner=new(new* Payload(&value));
                 immutable(Unique<Payload>)* view=&owner;readonly(Unique<Payload>)* shallow=&owner;
                 immutable(Payload)* address=&owner.ValueImmutable;int before=allocations;
                 view.ValueImmutable;shallow.ValueImmutable;owner.ValueImmutable;
                 if(view.ValueImmutable.Read()!=42||*view.ValueImmutable.Data!=42)return 1;
                 if(&view.ValueImmutable!=address||dead!=0||allocations!=before)return 2;
                 owner.Value.Data[0]=43;
                 if(view.ValueImmutable.Read()!=43||allocations!=before)return 3;}
                if(dead!=1)return 4;
                Unique<Payload> empty=new();immutable(Unique<Payload>)* view=&empty;
                try{view.ValueImmutable;return 5;}catch(ref Exception error){
                    if(!CString::Equals(error.GetMessage(),"Unique is empty"))return 6;
                }
                return dead-1;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ManagedFieldsAndBorrowedAddressSurviveOwnerMovesAndVectorRelocation(int optimization)
        => Run("""
            using std; using std::Text; using std::Memory; using std::Collections;int dead=0;
            class Node{public int Value;public Node(int value){Value=value;}public immutable int Read()nothrow=>Value;}
            struct Payload{
                public int* Data;public Node^ Child;
                public Payload(int* data){Data=data;Child=new^ Node(30);}
                ~Payload(){dead++;}
            }
            Unique<Payload> Make(int* data)=>new Unique<Payload>(new* Payload(data));
            void Check(){
                int value=12;Unique<Payload> owner=Make(&value);
                immutable(Unique<Payload>)* view=&owner;
                immutable(Payload)* original=&view.ValueImmutable;
                GC::Collect();
                if(view.ValueImmutable.Child.Read()+*view.ValueImmutable.Data!=42)throw new Exception("deep read");
                Unique<Payload> moved=move owner;
                Vector<Unique<Payload>> owners=new();owners.Add(move moved);owners.Reserve(64);
                immutable(Vector<Unique<Payload>>)* values=&owners;
                (*values)[0].ValueImmutable;
                if(&(*values)[0].ValueImmutable!=original||dead!=0)throw new Exception("borrow changed");
                GC::Collect();
                if((*values)[0].ValueImmutable.Child.Value!=30||TracingCollector::ManagedCount!=1)throw new Exception("managed child lost");
                owners.Clear();if(dead!=1)throw new Exception("wrong destruction");
            }
            int main(){Check();GC::Collect();if(dead!=1||TracingCollector::ManagedCount!=0)return 1;return 0;}
            """,optimization);

    [Theory]
    [InlineData("view.ValueImmutable.Number=1;")]
    [InlineData("view.ValueImmutable.Data[0]=1;")]
    [InlineData("view.ValueImmutable.Child.Value=1;")]
    [InlineData("view.ValueImmutable.Change();")]
    [InlineData("view.ValueImmutable.Child.Change();")]
    [InlineData("int* escaped=view.ValueImmutable.Data;")]
    [InlineData("Node^ escaped=view.ValueImmutable.Child;")]
    [InlineData("Payload copy=view.ValueImmutable;")]
    public void DeepBorrowCannotMutateOrExposeWritableAliases(string operation)
    {
        string source="""
            using std; using std::Text; using std::Memory; using std::Collections;
            class Node{public int Value;public Node(int value){Value=value;}public void Change(){Value=1;}}
            struct Payload{
                public int Number;public int* Data;public Node^ Child;
                public Payload(int* data){Number=0;Data=data;Child=new^ Node(30);}
                public void Change(){Number=1;}
                ~Payload(){}
            }
            void Invalid(immutable(Unique<Payload>)* view){
            """+operation+"}";
        Assert.Throws<TypeCheckException>(()=>Compiler.Emit(Sources(source)));
    }

    private static IEnumerable<SourceFile> Sources(string source)
        => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("unique-immutable.gf",source));
    private static void Run(string source,int optimization)
        => Assert.Equal(0,CompilerTestHelper.Run(Sources(source),optimization).ExitCode);
}
