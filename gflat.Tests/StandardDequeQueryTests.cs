using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class StandardDequeQueryTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void WrappedRingQueriesAreLazyReusableAndObserveElementReplacement(int optimization)
        => Run("""
            using std; using std::Queries; using std::Collections;
            int predicates=0;int selections=0;
            bool Even(int value){predicates++;return value%2==0;}
            int Twice(int value){selections++;return value*2;}
            int Sum(int total,int value)=>total+value;
            int main(){
                Deque<int> values=new();values.Reserve(8);
                for(int i=0;i<7;i++)values.AddLast(i);
                values.RemoveFirst();values.RemoveFirst();values.AddLast(7);values.AddLast(8);
                var source=values.AsEnumerable();var query=source.Where(Even).Select(Twice).Take(3);
                if(predicates!=0||selections!=0||source.Count()!=7)return 1;
                if(query.Aggregate(0,Sum)!=24||predicates!=5||selections!=3)return 2;
                predicates=0;selections=0;
                if(query.Aggregate(0,Sum)!=24||predicates!=5||selections!=3)return 3;
                values[0]=10;values[2]=12;
                predicates=0;selections=0;
                if(query.Aggregate(0,Sum)!=56||predicates!=5||selections!=3)return 4;
                var first=source.GetEnumerator();var second=source.GetEnumerator();
                if(!first.MoveNext()||first.Current!=10||!first.MoveNext()||first.Current!=3)return 5;
                if(!second.MoveNext()||second.Current!=10)return 6;
                first.Current=30;if(values[1]!=30)return 7;
                predicates=0;selections=0;int visited=0;
                foreach(int value in query){if(value!=20)return 8;visited++;break;}
                if(visited!=1||predicates!=1||selections!=1||values.Count!=7)return 9;
                if(source.Count()!=7||!source.Any())return 10;
                Deque<int> empty=new();if(empty.AsEnumerable().Any()||empty.AsEnumerable().Count()!=0)return 11;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NoncopyableOwnerQueriesBorrowWithoutAllocatingOrConsumingElements(int optimization)
        => Run("""
            using std; using std::Queries; using std::Collections;
            int made=0;int dead=0;int allocations=0;int releases=0;bool deny=false;
            extern void*? malloc(nuint bytes);extern void free(void* storage);
            namespace Memory{
                public replace void*? Allocate(nuint bytes){if(deny)return null;allocations++;return malloc(bytes);}
                public replace void Free(void* storage) nothrow{releases++;free(storage);}
            }
            struct Owner{
                public int Id;
                public Owner(int id){Id=id;made++;}
                ~Owner(){dead++;}
            }
            int main(){
                {
                    Deque<Owner> values=new();values.Reserve(8);
                    for(int i=0;i<7;i++)values.AddLast(new Owner(i));
                    values.RemoveFirst();values.RemoveFirst();values.AddLast(new Owner(7));values.AddLast(new Owner(8));
                    int before=allocations;int freed=releases;deny=true;
                    var query=values.AsEnumerable();
                    if(query.Count()!=7||!query.Any()||made!=9||dead!=2)return 1;
                    var first=query.GetEnumerator();var second=query.GetEnumerator();
                    if(!first.MoveNext()||first.Current.Id!=2)return 2;
                    if(!second.MoveNext()||second.Current.Id!=2)return 3;
                    values[0].Id=42;if(first.Current.Id!=42||second.Current.Id!=42)return 4;
                    int seen=0;
                    while(first.MoveNext()){if(first.Current.Id!=3)return 5;seen++;break;}
                    if(seen!=1||query.Count()!=7||values.Count!=7||dead!=2)return 6;
                    Deque<Owner> empty=new();if(empty.AsEnumerable().Any()||empty.AsEnumerable().Count()!=0)return 7;
                    if(allocations!=before||releases!=freed)return 8;deny=false;
                }
                if(made!=9||dead!=9||allocations!=releases)return 9;return 0;
            }
            """, optimization);

    [Theory]
    [InlineData("readonly")]
    [InlineData("immutable")]
    public void ProtectedOwnersCannotProduceWritableQueries(string qualifier)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Sources($$"""
            using std; using std::Queries; using std::Collections;
            int main(){Deque<int> values=new();{{qualifier}}(Deque<int>)* view=&values;view.AsEnumerable();return 0;}
            """)));
        Assert.Contains("non-" + qualifier, error.Message);
    }

    [Fact]
    public void CapturedViewConstructorDoesNotExposeRawRingMetadata()
        => Assert.Throws<TypeCheckException>(() => Compiler.Check(Sources("""
            using std; using std::Queries; using std::Collections;
            int main(){Deque<int>::View view=new Deque<int>::View(null,0,0,0);return 0;}
            """)));

    private static IEnumerable<SourceFile> Sources(string program)
    {
        string[] names = ["Collections/Deque.gf", "Span.gf", "Collections/Vector.gf", "Collections/IEnumerable.gf", "Collections/IEnumerator.gf", "Queries/Enumerable.gf",
            "Queries/EnumerableExtensions.gf", "Queries/WhereSource.gf", "Queries/SelectSource.gf", "Queries/SliceSource.gf", "Queries/TakeWhileSource.gf", "Queries/SkipWhileSource.gf"];
        return names.Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", name))))
            .Append(new SourceFile("deque-queries.gf", program));
    }
    private static void Run(string program, int optimization)
    {
        var result = CompilerTestHelper.Run(Sources(program), optimization);
        Assert.True(result.ExitCode == 0, $"Deque query exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
