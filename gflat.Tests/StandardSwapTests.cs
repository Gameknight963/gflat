using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class StandardSwapTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PrimitiveSlotsFieldsAndRefGettersSwapWithoutChangingTheirAddresses(int optimization)
        => Run("""
            using std;
            int getterCalls=0;
            struct Cells {
                public int Left;public int Right;public bool Fail;
                public Cells(){Left=1;Right=2;Fail=false;}
                public ref int First {get{getterCalls++;return Left;}}
                public ref int Second {get{getterCalls++;if(Fail)throw new Exception("argument failed",47);return Right;}}
            }
            int main(){
                int first=1;int second=2;int* a=&first;int* b=&second;
                Swap(ref first,ref second);
                if(first!=2||second!=1||a!=&first||b!=&second||*a!=2||*b!=1)return 1;
                Swap(ref first,ref first);if(first!=2)return 2;
                Cells cells=new();Swap(ref cells.Left,ref cells.Right);
                if(cells.Left!=2||cells.Right!=1)return 3;
                Swap(ref cells.First,ref cells.Second);
                if(getterCalls!=2||cells.Left!=1||cells.Right!=2)return 4;
                cells.Fail=true;
                try{Swap(ref cells.First,ref cells.Second);return 5;}
                catch(ref Exception error){if(error.GetCode()!=47)return 6;}
                if(getterCalls!=4||cells.Left!=1||cells.Right!=2)return 7;
                int[3] data=[3,4,5];Span<int> view=new(data,3);
                Swap(ref view[0],ref view[2]);Swap(ref view[1],ref view[1]);
                if(data[0]!=5||data[1]!=4||data[2]!=3)return 8;
                return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OwningStringsAndNoncopyableUniqueValuesRelocateWithoutAllocatingOrDestroying(int optimization)
        => Run("""
            using std;
            extern void*? malloc(nuint size);extern void free(void* pointer);
            int attempts=0;int allocated=0;int freed=0;int dead=0;bool deny=false;
            namespace Memory {
                public replace void*? Allocate(nuint size){attempts++;if(deny)return null;void*? p=malloc(size);if(p!=null)allocated++;return p;}
                public replace void Free(void* pointer) nothrow {freed++;free(pointer);}
            }
            struct Payload {public int Number;public Payload(int value){Number=value;}~Payload(){dead++;}}
            int main(){
                {
                    String first=s"first";String second=s"s\0econd";
                    readonly(char)* firstData=first.Data;readonly(char)* secondData=second.Data;
                    Unique<Payload> left=new(new* Payload(11));Unique<Payload> right=new(new* Payload(22));
                    Payload* leftPointer=&left.Value;Payload* rightPointer=&right.Value;
                    int before=attempts;deny=true;
                    Swap(ref first,ref second);Swap(ref first,ref first);
                    Swap(ref left,ref right);Swap(ref right,ref right);
                    if(attempts!=before||dead!=0)return 1;
                    if(first.Data!=secondData||second.Data!=firstData||first.Length!=7||first.Data[1]!='\0')return 2;
                    if(&left.Value!=rightPointer||&right.Value!=leftPointer||left.Value.Number!=22||right.Value.Number!=11)return 3;
                    deny=false;
                }
                if(dead!=2||allocated!=freed)return 4;
                bool caught=false;
                try{
                    Unique<Payload> left=new(new* Payload(33));Unique<Payload> right=new(new* Payload(44));
                    Swap(ref left,ref right);if(left.Value.Number!=44||right.Value.Number!=33)return 5;
                    throw new Exception("after swap",48);
                }catch(ref Exception error){caught=true;if(error.GetCode()!=48)return 6;}
                if(!caught||dead!=4||allocated!=freed)return 7;
                return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ManagedLocalAndNativeStructSlotsRemainRootsAfterSwapping(int optimization)
        => Run("""
            using std;
            class Node {public int Number;public Node^? Next;public Node(int value){Number=value;Next=null;}}
            struct Pair {
                public Node^ First;public Node^ Second;
                public Pair(Node^ first,Node^ second){First=first;Second=second;}
            }
            void Work(){
                Node^ first=new^ Node(1);Node^ second=new^ Node(2);
                first.Next=first;second.Next=second;
                Swap(ref first,ref second);Swap(ref first,ref first);
                if(GC::Collect()!=0||first.Number!=2||second.Number!=1)throw new Exception("local roots");
                Pair left=new(first,second);Pair right=new(new^ Node(3),new^ Node(4));
                Swap(ref left.First,ref left.Second);
                if(GC::Collect()!=0||left.First.Number!=1||left.Second.Number!=2)throw new Exception("field roots");
                Swap(ref left,ref right);Swap(ref left,ref left);
                if(GC::Collect()!=0||TracingCollector::ManagedCount!=4)throw new Exception("aggregate roots");
                if(left.First.Number!=3||left.Second.Number!=4||right.First.Number!=1||right.Second.Number!=2)throw new Exception("aggregate values");
            }
            int main(){Work();if(GC::Collect()!=4||TracingCollector::ManagedCount!=0)return 1;return 0;}
            """,optimization);

    [Theory]
    [InlineData("readonly(int) left=1;int right=2;Swap<int>(ref left,ref right);", "readonly")]
    [InlineData("immutable(int) left=1;int right=2;Swap<int>(ref left,ref right);", "Argument 1")]
    [InlineData("readonly(int) left=1;readonly(int) right=2;Swap(ref left,ref right);", "readonly")]
    [InlineData("immutable(int) left=1;immutable(int) right=2;Swap(ref left,ref right);", "readonly")]
    [InlineData("int right=2;Swap<int>(ref 1,ref right);", "existing storage")]
    [InlineData("int left;int right=2;Swap<int>(ref left,ref right);", "unassigned")]
    [InlineData("Holder item=new();int right=2;Swap<int>(ref item.Value,ref right);", "existing storage")]
    [InlineData("Holder^ item=new^ Holder();int right=2;Swap<int>(ref item.Field,ref right);", "managed storage")]
    public void SwapRequiresInitializedMutableNativeStorage(string statements,string diagnostic)
    {
        var error=Assert.Throws<TypeCheckException>(()=>Compiler.Check(Sources("""
            using std;class Holder{public int Field;public int Value{get=>Field;}}
            int main(){
            """+statements+"return 0;}")));
        Assert.Contains(diagnostic,error.Message,StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<SourceFile> Sources(string program)
        => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path)))
            .Append(new SourceFile("swap-test.gf",program));
    private static void Run(string program,int optimization)
    {
        var result=CompilerTestHelper.Run(Sources(program),optimization);
        Assert.True(result.ExitCode==0,$"Swap exit {result.ExitCode}: {result.StandardError}");
    }
}
