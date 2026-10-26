using System.Text;
using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class StandardSpanWindowsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FullWindowsMatchIndependentSmallSequenceOracle(int optimization)
    {
        var result=CompilerTestHelper.Run(Sources("""
            using std; using std::Queries; using std::Collections;extern int printf(readonly(char)* format,...);
            int main(){
                int[9] data;for(nuint i=0;i<9;i++)data[i]=(int)(2*i+1);
                for(nuint length=0;length<=9;length++)for(nuint width=1;width<=11;width++)for(nuint step=1;step<=12;step++){
                    Span<int> source=new(data,length);int count=0;int checksum=0;
                    foreach(Span<int> window in source.Windows(width,step)){
                        if(window.Length!=width)return 1;
                        for(nuint i=0;i<window.Length;i++)checksum+=window[i];count++;
                    }
                    printf("%d/%d\n",count,checksum);
                }
                return 0;
            }
            """),optimization);
        Assert.Equal(0,result.ExitCode);
        var expected=new StringBuilder();
        for(int length=0;length<=9;length++)for(int width=1;width<=11;width++)for(int step=1;step<=12;step++){
            var windows=Enumerable.Range(0,length).Where(start=>start%step==0&&width<=length-start)
                .Select(start=>Enumerable.Range(start,width).Sum(index=>2*index+1)).ToArray();
            expected.Append(windows.Length).Append('/').Append(windows.Sum()).Append('\n');
        }
        Assert.Equal(expected.ToString(),result.StandardOutput.Replace("\r\n","\n"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AdjacentDifferencesAndLazySmoothingBorrowCapturedStorageWithoutAllocation(int optimization)
        => Run("""
            using std; using std::Queries; using std::Collections;extern void*? malloc(nuint bytes);extern void free(void* pointer);
            int allocations=0;int selected=0;
            namespace Memory{public replace void*? Allocate(nuint bytes){allocations++;return malloc(bytes);}public replace void Free(void* pointer) nothrow{free(pointer);}}
            int Average(Span<readonly(int)> window){selected++;return (window[0]+window[1]+window[2])/3;}
            int Sum(int total,int value)=>total+value;
            SpanWindows<int> Capture(Span<int> source)=>source.Windows(2);
            int main(){
                int[5] data=[1,3,5,7,9];Span<int> source=new(data,5);int before=allocations;
                var windows=Capture(source);int total=0;
                foreach(Span<int> pair in windows)total+=pair[1]-pair[0];if(total!=8)return 1;
                var query=source.AsImmutable().Windows(3).AsEnumerable().Select(Average);
                if(selected!=0||query.Aggregate(0,Sum)!=15||selected!=3)return 2;
                data[1]=6;if(query.Aggregate(0,Sum)!=17||selected!=6)return 3;
                source=new();total=0;foreach(Span<int> pair in windows)total+=pair[0];if(total!=19)return 4;
                foreach(Span<int> pair in windows){pair[1]=pair[0];}
                for(nuint i=0;i<5;i++)if(data[i]!=1)return 5;
                if(allocations!=before)return 6;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NoncopyableElementsAndShallowOrDeepQualifiedDataStayBorrowed(int optimization)
        => Run("""
            using std; using std::Queries; using std::Collections;int copies=0;int dead=0;
            struct Owner{
                public int Id;public int* Pointer;
                public Owner(int id,int* pointer){Id=id;Pointer=pointer;}
                private copy Owner(immutable(Owner)* source){copies++;Id=source.Id;Pointer=(int*)source.Pointer;}
                ~Owner(){dead++;}
            }
            int main(){
                int pointed=0;
                {
                    Owner[3] data=[new Owner(1,&pointed),new Owner(2,&pointed),new Owner(3,&pointed)];
                    Span<Owner> source=new(data,3);int total=0;
                    foreach(Span<Owner> window in source.Windows(2)){total+=window[0].Id;window[1].Id+=10;}
                    if(total!=13||data[1].Id!=12||data[2].Id!=13)return 1;
                    foreach(Span<readonly(Owner)> window in source.AsReadOnly().Windows(1))*window[0].Pointer+=1;
                    if(pointed!=3)return 2;
                    int seen=0;foreach(Span<immutable(Owner)> window in source.AsImmutable().Windows(1)){seen+=*window[0].Pointer;}
                    if(seen!=9||copies!=0||dead!=0)return 3;
                }
                if(dead!=3||copies!=0)return 4;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EmptyInvalidAndMaximumStepIteratorBoundariesAreChecked(int optimization)
        => Run("""
            using std; using std::Queries; using std::Collections;
            int main(){
                Span<int> empty=new();if(empty.Windows(1).GetEnumerator().MoveNext())return 1;
                int value=42;Span<int> one=new(&value,1);int errors=0;
                try{empty.Windows(0);}catch{errors++;}try{one.Windows(1,0);}catch{errors++;}
                try{SpanWindows<int> invalid=new(null,1,1);}catch{errors++;}
                try{SpanWindows<int>::Enumerator invalid=new(null,1,1,1);}catch{errors++;}
                var iterator=one.Windows(1).GetEnumerator();
                try{iterator.Current;}catch{errors++;}
                if(!iterator.MoveNext()||iterator.Current[0]!=42||iterator.MoveNext()||iterator.MoveNext())return 2;
                try{iterator.Current;}catch{errors++;}
                var tooWide=one.Windows(2).GetEnumerator();if(tooWide.MoveNext())return 3;
                try{tooWide.Current;}catch{errors++;}
                nuint maximum=(nuint)0-(nuint)1;
                // Exercise only cursor arithmetic: this metadata is never dereferenced.
                SpanWindows<int>::Enumerator large=new((int*)1,maximum,1,maximum-1);
                if(!large.MoveNext()||!large.MoveNext()||large.MoveNext())return 4;
                SpanWindows<int>::Enumerator hugeWidth=new((int*)1,maximum,maximum,maximum);
                if(!hugeWidth.MoveNext()||hugeWidth.MoveNext())return 5;
                if(errors!=7)return 6;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WindowElementQualifiersRejectForbiddenWrites(bool deep)
    {
        string setup="""
            using std; using std::Queries; using std::Collections;struct Item{public int Value;public int* Pointer;public Item(int* pointer){Value=0;Pointer=pointer;}}
            int main(){int pointed=0;Item item=new(&pointed);Span<Item> source=new(&item,1);
            """;
        string loop=deep?"foreach(Span<immutable(Item)> window in source.AsImmutable().Windows(1)){BODY}":
            "foreach(Span<readonly(Item)> window in source.AsReadOnly().Windows(1)){BODY}";
        Compiler.Check(Sources(setup+loop.Replace("BODY","int read=window[0].Value;")+"return 0;}"));
        var error=Assert.Throws<TypeCheckException>(()=>Compiler.Check(Sources(setup+loop.Replace("BODY",deep?"*window[0].Pointer=1;":"window[0].Value=1;")+"return 0;}")));
        Assert.Equal("span-windows.gf",error.Span.Source?.Path);
        Assert.Contains("readonly",error.Message,StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<SourceFile> Sources(string program)
    {
        string[] names=["Memory/Span.gf","Collections/IEnumerable.gf","Collections/IEnumerator.gf","Queries/Enumerable.gf","Collections/SpanWindows.gf",
            "Queries/enumerable_predicates.gf", "Queries/enumerable_count.gf", "Queries/enumerable_elements.gf", "Queries/enumerable_for_each.gf", "Queries/enumerable_to_vector.gf", "Queries/enumerable_select.gf", "Queries/enumerable_aggregate.gf", "Queries/enumerable_where.gf", "Queries/enumerable_slice.gf", "Queries/enumerable_while.gf","Queries/WhereSource.gf","Queries/SelectSource.gf","Queries/SliceSource.gf","Queries/TakeWhileSource.gf","Queries/SkipWhileSource.gf"];
        return names.Select(name=>new SourceFile(name,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core",name))))
            .Append(new SourceFile("span-windows.gf",program));
    }
    private static void Run(string program,int optimization)
    {
        var result=CompilerTestHelper.Run(Sources(program),optimization);
        Assert.True(result.ExitCode==0,$"Exit {result.ExitCode}: {result.StandardError}");
    }
}
