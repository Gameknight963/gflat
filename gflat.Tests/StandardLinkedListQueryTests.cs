using System.Text;
using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class StandardLinkedListQueryTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void QueriesMatchDotNetAfterMixedNodeOperations(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std; extern int printf(readonly(char)* format,...);
            int main() {
                for (int count=0;count<25;count++) {
                    LinkedList<int> values=new();
                    for (int i=0;i<count;i++) {
                        if(i%2==0) values.AddFirst(i); else values.AddLast(i);
                    }
                    if(count>2) { values.RemoveFirst(); values.RemoveLast(); }
                    var query=values.AsEnumerable().Where((int n)=>n%3!=0).Select((int n)=>n*2).Take(5);
                    foreach(int value in query) printf("%d,",value);
                    printf("/");
                    foreach(int value in query) printf("%d,",value);
                    printf("\n");
                }
                return 0;
            }
            """), optimization);
        Assert.Equal(0, result.ExitCode);
        var expected = new StringBuilder();
        for (int count = 0; count < 25; count++)
        {
            var values = new LinkedList<int>();
            for (int i = 0; i < count; i++)
                if (i % 2 == 0) values.AddFirst(i); else values.AddLast(i);
            if (count > 2) { values.RemoveFirst(); values.RemoveLast(); }
            string sequence = string.Concat(values.Where(n => n % 3 != 0).Select(n => n * 2).Take(5).Select(n => n + ","));
            expected.Append(sequence).Append('/').Append(sequence).Append('\n');
        }
        Assert.Equal(expected.ToString(), result.StandardOutput.Replace("\r\n", "\n"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NoncopyableBorrowedQueriesSurviveOwnerMoveAndObserveElementEdits(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std; extern void*? malloc(nuint size); extern void free(void* pointer);
            int allocations=0; int releases=0; int dead=0; bool deny=false;
            namespace Memory {
                public replace void*? Allocate(nuint size) { if(deny)return null; allocations++; return malloc(size); }
                public replace void Free(void* pointer) nothrow { releases++; free(pointer); }
            }
            struct Owner { public int Id; public Owner(int id){Id=id;} ~Owner(){dead++;} }
            int main() {
                {
                    LinkedList<Owner> original=new();
                    original.AddLast(new Owner(1)); original.AddLast(new Owner(2)); original.AddLast(new Owner(3));
                    int before=allocations; deny=true;
                    var query=original.AsEnumerable();
                    if(query.Count()!=3||!query.Any()||dead!=0)return 1;
                    var first=query.GetEnumerator(); var second=query.GetEnumerator();
                    if(!first.MoveNext()||!second.MoveNext()||first.Current.Id!=1||second.Current.Id!=1)return 2;
                    LinkedList<Owner> moved=move original;
                    original=new();
                    if(original.AsEnumerable().Any()||query.Count()!=3)return 3;
                    moved.First!.Value.Id=42;
                    if(first.Current.Id!=42||second.Current.Id!=42)return 4;
                    first.Current.Id=43;
                    if(moved.First!.Value.Id!=43||!first.MoveNext()||first.Current.Id!=2||second.Current.Id!=43)return 5;
                    var repeated=query.GetEnumerator();
                    if(!repeated.MoveNext()||repeated.Current.Id!=43)return 6;
                    if(allocations!=before||dead!=0)return 7;
                    deny=false;
                }
                if(dead!=3||allocations!=releases)return 8;
                return 0;
            }
            """), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }

    [Fact]
    public void BorrowedViewCannotBeConstructedFromAnArbitraryNode()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Sources("""
            using std;
            int main(){LinkedList<int>::View view=new LinkedList<int>::View(null);return 0;}
            """)));
        Assert.Equal("linked-list-query.gf", error.Span.Source?.Path);
        Assert.Contains("inaccessible", error.Message);
    }

    private static IEnumerable<SourceFile> Sources(string source)
    {
        string[] names = ["LinkedList.gf", "Vector.gf", "Span.gf", "Collections/IEnumerable.gf", "Collections/IEnumerator.gf", "Collections/Enumerable.gf",
            "Collections/EnumerableExtensions.gf", "Collections/WhereSource.gf", "Collections/SelectSource.gf", "Collections/SliceSource.gf",
            "Collections/TakeWhileSource.gf", "Collections/SkipWhileSource.gf"];
        return names.Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", name))))
            .Append(new SourceFile("linked-list-query.gf", source));
    }
}
