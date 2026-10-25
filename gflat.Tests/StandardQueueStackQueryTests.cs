using System.Text;
using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class StandardQueueStackQueryTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CapturedFifoAndLifoOrderMatchDotNetAcrossWrappedExtents(int optimization)
    {
        var result=CompilerTestHelper.Run(Sources("""
            using std; using std::Queries; using std::Collections;extern int printf(readonly(char)* format,...);
            int main(){
                for(int count=0;count<=16;count++)for(int removed=0;removed<=count;removed++)for(int roomy=0;roomy<2;roomy++){
                    Queue<int> queue=new();Stack<int> stack=new();
                    nuint capacity=(nuint)count;if(roomy!=0)capacity+=2;queue.Reserve(capacity);stack.Reserve(capacity);
                    for(int i=0;i<count;i++){queue.Enqueue(i);stack.Push(i);}
                    for(int i=0;i<removed;i++){queue.Dequeue();stack.Pop();}
                    for(int i=0;i<removed;i++){queue.Enqueue(100+i);stack.Push(100+i);}
                    var fifo=queue.AsEnumerable();var lifo=stack.AsEnumerable();
                    if(fifo.Count()!=(nuint)count||lifo.Count()!=(nuint)count)return 1;
                    foreach(int value in fifo)printf("%d,",value);printf("/");
                    foreach(int value in lifo)printf("%d,",value);printf("\n");
                }
                return 0;
            }
            """),optimization);
        Assert.Equal(0,result.ExitCode);var expected=new StringBuilder();
        for(int count=0;count<=16;count++)for(int removed=0;removed<=count;removed++)for(int roomy=0;roomy<2;roomy++){
            var queue=new Queue<int>(Enumerable.Range(0,count));var stack=new Stack<int>(Enumerable.Range(0,count));
            for(int i=0;i<removed;i++){queue.Dequeue();stack.Pop();}
            for(int i=0;i<removed;i++){queue.Enqueue(100+i);stack.Push(100+i);}
            foreach(int value in queue)expected.Append(value).Append(',');expected.Append('/');
            foreach(int value in stack)expected.Append(value).Append(',');expected.Append('\n');
        }
        Assert.Equal(expected.ToString(),result.StandardOutput.Replace("\r\n","\n"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void LazyQueriesRepeatAndObserveElementEditsWithoutAllocation(int optimization)
        =>Run("""
            using std; using std::Queries; using std::Collections;extern void*? malloc(nuint size);extern void free(void* pointer);
            int allocations=0;int predicates=0;int selections=0;
            namespace Memory{public replace void*? Allocate(nuint size){allocations++;return malloc(size);}public replace void Free(void* pointer) nothrow{free(pointer);}}
            bool Even(int value){predicates++;return value%2==0;}
            int Twice(int value){selections++;return value*2;}
            int Sum(int total,int value)=>total+value;
            int main(){
                Queue<int> queue=new();queue.Reserve(5);for(int i=0;i<5;i++)queue.Enqueue(i);
                queue.Dequeue();queue.Dequeue();queue.Enqueue(5);queue.Enqueue(6);
                Stack<int> stack=new();for(int i=0;i<5;i++)stack.Push(i);int before=allocations;
                var fifo=queue.AsEnumerable();var lifo=stack.AsEnumerable();
                var forward=fifo.Where(Even).Select(Twice).Take(2);var backward=lifo.Where(Even).Select(Twice).Take(2);
                if(predicates!=0||selections!=0)return 1;
                if(forward.Aggregate(0,Sum)!=12||predicates!=3||selections!=2)return 2;
                predicates=0;selections=0;if(backward.Aggregate(0,Sum)!=12||predicates!=3||selections!=2)return 3;
                queue.Front=20;stack.Top=40;
                if(forward.Aggregate(0,Sum)!=48||backward.Aggregate(0,Sum)!=84)return 4;
                var first=fifo.GetEnumerator();var second=fifo.GetEnumerator();
                if(!first.MoveNext()||!second.MoveNext()||first.Current!=20||second.Current!=20)return 5;
                if(!first.MoveNext()||first.Current!=3||second.Current!=20)return 6;
                if(!fifo.Any()||!lifo.Any()||fifo.Count()!=5||lifo.Count()!=5||allocations!=before)return 7;
                Queue<int> emptyQueue=new();Stack<int> emptyStack=new();
                if(emptyQueue.AsEnumerable().Any()||emptyStack.AsEnumerable().Any())return 8;
                return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NoncopyableElementsSupportMetadataQueriesAndDirectBorrowedIteration(int optimization)
        =>Run("""
            using std; using std::Queries; using std::Collections;extern void*? malloc(nuint size);extern void free(void* pointer);
            int allocations=0;int releases=0;bool deny=false;int dead=0;
            namespace Memory{public replace void*? Allocate(nuint size){if(deny)return null;allocations++;return malloc(size);}public replace void Free(void* pointer) nothrow{releases++;free(pointer);}}
            struct Owner{public int Id;public Owner(int id){Id=id;}~Owner(){dead++;}}
            int main(){
                {
                    Queue<Owner> queue=new();queue.Reserve(2);queue.Enqueue(new Owner(1));queue.Enqueue(new Owner(2));
                    Stack<Owner> stack=new();stack.Reserve(2);stack.Push(new Owner(3));stack.Push(new Owner(4));
                    int before=allocations;deny=true;
                    var fifo=queue.AsEnumerable();var lifo=stack.AsEnumerable();
                    if(fifo.Count()!=2||lifo.Count()!=2||!fifo.Any()||!lifo.Any())return 1;
                    var first=fifo.GetEnumerator();var second=fifo.GetEnumerator();
                    if(!first.MoveNext()||!second.MoveNext()||first.Current.Id!=1||second.Current.Id!=1)return 2;
                    if(!first.MoveNext()||first.Current.Id!=2||second.Current.Id!=1)return 3;
                    second.Current.Id=9;if(queue.Front.Id!=9)return 4;
                    var top=lifo.GetEnumerator();if(!top.MoveNext()||top.Current.Id!=4)return 5;
                    top.Current.Id=8;if(stack.Top.Id!=8)return 6;
                    if(!top.MoveNext()||top.Current.Id!=3||top.MoveNext()||top.MoveNext())return 7;
                    if(queue.Count!=2||stack.Count!=2||allocations!=before||dead!=0)return 8;deny=false;
                }
                if(dead!=4||allocations!=releases)return 9;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData("Queue", "null,0,0,0")]
    [InlineData("Stack", "new Span<int>()")]
    public void ViewConstructorsRemainPrivateToTheOwner(string type,string arguments)
    {
        var error=Assert.Throws<TypeCheckException>(()=>Compiler.Check(Sources($"using std; using std::Queries; using std::Collections;int main(){{{type}<int>::View view=new {type}<int>::View({arguments});return 0;}}")));
        Assert.Equal("queue-stack-queries.gf",error.Span.Source?.Path);Assert.Contains("inaccessible",error.Message);
    }

    private static IEnumerable<SourceFile> Sources(string source)
    {
        string[] names=["Collections/Queue.gf","Collections/Stack.gf","Span.gf","Collections/Vector.gf","Collections/IEnumerable.gf","Collections/IEnumerator.gf","Queries/Enumerable.gf",
            "Queries/EnumerableExtensions.gf","Queries/WhereSource.gf","Queries/SelectSource.gf","Queries/SliceSource.gf","Queries/TakeWhileSource.gf","Queries/SkipWhileSource.gf"];
        return names.Select(name=>new SourceFile(name,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core",name))))
            .Append(new SourceFile("queue-stack-queries.gf",source));
    }
    private static void Run(string source,int optimization)
    {
        var result=CompilerTestHelper.Run(Sources(source),optimization);Assert.True(result.ExitCode==0,$"Exit {result.ExitCode}: {result.StandardError}");
    }
}
