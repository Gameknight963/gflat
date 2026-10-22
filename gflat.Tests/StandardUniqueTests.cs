namespace gflat.Tests;

public sealed class StandardUniqueTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MovesAndFailuresDestroyNoncopyablePointeesExactlyOnce(int optimization)
        => Run("""
            using std;
            extern void*? malloc(nuint size);extern void free(void* ptr);
            void*? watched=null;int watchedFrees=0;int dead=0;
            namespace Memory {
                public replace void*? Allocate(nuint size)=>malloc(size);
                public replace void Free(void* ptr){if(ptr==watched){watchedFrees++;watched=null;}free(ptr);}
            }
            struct Payload{public int Number;public Payload(int n){Number=n;}~Payload(){dead++;}}
            void Fail(Unique<Payload> item){if(item.Value.Number!=42)throw new Exception("wrong payload");throw new Exception("callee failure");}
            class Base{~Base(){dead++;}}
            class Derived:Base{~Derived(){dead+=10;}}
            int main(){
                if(sizeof(Unique<int>)!=sizeof(void*))return 1;
                {Unique<Payload> empty=new();if(!empty.IsEmpty)return 2;
                 try{empty.Value;return 3;}catch(ref Exception error){}
                 Unique<Payload> owner=new Unique<Payload>(new* Payload(42));
                 watched=(void*)&owner.Value;Payload* before=&owner.Value;
                 Unique<Payload> moved=move owner;
                 if(moved.IsEmpty||&moved.Value!=before||moved.Value.Number!=42)return 4;
                 moved.Value.Number=43;if(before.Number!=43)return 5;}
                if(dead!=1||watchedFrees!=1)return 6;
                {Unique<Payload> owner=new(new* Payload(42));watched=(void*)&owner.Value;
                 try{Fail(move owner);return 7;}catch(ref Exception error){}
                 if(dead!=2||watchedFrees!=2)return 8;}
                {Base* raw=new* Derived();Unique<Base> owner=new(raw);watched=(void*)&owner.Value;}
                if(dead!=13||watchedFrees!=3)return 9;
                {Unique<Payload> owner=new(new* Payload(1));owner=new Unique<Payload>(new* Payload(2));
                 if(dead!=14||owner.Value.Number!=2)return 10;}
                if(dead!=15)return 11;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void VectorRelocationAndRemovalTransferOwnersWithoutMovingPointees(int optimization)
        => Run("""
            using std;int dead=0;
            struct Payload{public int Number;public Payload(int n){Number=n;}~Payload(){dead++;}}
            int main(){
                {Vector<Unique<Payload>> owners=new();
                 owners.Add(new Unique<Payload>(new* Payload(1)));
                 Payload* first=&owners[0].Value;
                 for(int i=2;i<=17;i++)owners.Add(new Unique<Payload>(new* Payload(i)));
                 owners.Reserve(100);
                 if(dead!=0||&owners[0].Value!=first||first.Number!=1)return 1;
                 owners.RemoveRange(1,10);
                 if(dead!=10||owners.Count!=7||&owners[0].Value!=first)return 2;
                 {Unique<Payload> last=owners.Pop();
                  if(dead!=10||last.Value.Number!=17||owners.Count!=6)return 3;}
                 if(dead!=11)return 4;
                 owners.Clear();if(dead!=17||!owners.IsEmpty)return 5;}
                if(dead!=17)return 6;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ReadonlyOwnerBorrowsMutablePointeeAndRefParametersUpdateInPlace(int optimization)
        => Run("""
            using std;
            void Increment(ref int value){value++;}
            int main(){
                Unique<int> owner=new(new* int(7));
                readonly(Unique<int>)* view=&owner;
                view.Value=11;Increment(ref view.Value);
                if(owner.Value!=12||view.IsEmpty)return 1;
                immutable(Unique<int>)* immutableView=&owner;
                if(immutableView.IsEmpty)return 2;
                Unique<int> empty=new();immutable(Unique<int>)* emptyView=&empty;
                if(!emptyView.IsEmpty)return 3;
                return 0;
            }
            """,optimization);

    [Theory]
    [InlineData("Unique<int> first=new(new* int(1));Unique<int> copy=first;", "copy constructor")]
    [InlineData("Unique<int> first=new(new* int(1));Unique<int> second=move first;return first.Value;", "unassigned")]
    [InlineData("Unique<int> owner=new(new* int(1));immutable(Unique<int>)* view=&owner;view.Value=3;", "no setter")]
    [InlineData("I* pointer=new* Item();Unique<I> owner=new(pointer);return owner.Value.Read();", "interface")]
    public void CopiesMovedSourcesImmutableMutationAndInterfaceValuesAreRejected(string statements,string diagnostic)
    {
        var error=Assert.Throws<gflat.CompileExceptions.TypeCheckException>(()=>Compiler.Emit(Sources("""
            using std;interface I{int Read();}class Item:I{public int Read()=>42;}
            int main(){
            """+statements+"return 0;}")));
        Assert.Contains(diagnostic,error.Message,StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<SourceFile> Sources(string code)
        => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("main.gf",code));
    private static void Run(string code,int optimization)
        => Assert.Equal(0,CompilerTestHelper.Run(Sources(code),optimization).ExitCode);
}
