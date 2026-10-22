namespace gflat.Tests;

public sealed class StandardSortedMapExtractTests
{
    private const string Prelude="""
        using std;extern void*? malloc(nuint size);extern void free(void* storage);
        int allocations=0;int releases=0;int dead=0;int budget=-1;
        namespace Memory{
            public replace void*? Allocate(nuint size){allocations++;return malloc(size);}
            public replace void Free(void* storage){releases++;free(storage);}
        }
        struct Owner{
            public int Id;
            public Owner(int id){Id=id;}
            ~Owner(){dead++;}
        }
        int Compare(immutable(Owner)* a,immutable(Owner)* b){
            if(budget==0)throw new Exception("comparer failed");if(budget>0)budget--;
            if(a.Id<b.Id)return -1;if(a.Id>b.Id)return 1;return 0;
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ExtractTransfersNoncopyableEntriesAndAcceptsLookupBorrowFromSameEntry(int optimization)
        => Run("""
            int main(){
                {SortedMap<Owner,Owner> map=new(Compare);
                 for(int i=1;i<=5;i++)map.Add(new Owner(i),new Owner(i*10));
                 nuint capacity=map.Capacity;int before=allocations;
                 {Owner probe=new(3);SortedMap<Owner,Owner>::Entry middle=map.Extract(&probe);
                  if(middle.Key.Id!=3||middle.Value.Id!=30||map.Count!=4||dead!=0)return 1;}
                 if(dead!=3)return 2;
                 {var iterator=map.GetEnumerator();iterator.MoveNext();
                  immutable(Owner)* key=iterator.Current.Key;
                  SortedMap<Owner,Owner>::Entry first=map.Extract(key);
                  if(first.Key.Id!=1||first.Value.Id!=10||dead!=3||map.Count!=3)return 3;}
                 if(dead!=5)return 4;
                 {Owner probe=new(5);SortedMap<Owner,Owner>::Entry last=map.Extract(&probe);
                  if(last.Key.Id!=5||last.Value.Id!=50||map.Count!=2)return 5;}
                 if(dead!=8||allocations!=before||map.Capacity!=capacity)return 6;
                 var iterator=map.GetEnumerator();
                 if(!iterator.MoveNext()||iterator.Current.Key.Id!=2)return 7;
                 if(!iterator.MoveNext()||iterator.Current.Key.Id!=4||iterator.MoveNext())return 8;}
                if(dead!=12||allocations!=releases)return 9;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MissingAndThrowingComparersLeaveEntriesIntact(int optimization)
        => Run("""
            int main(){
                {SortedMap<Owner,Owner> map=new(Compare);
                 for(int i=1;i<=3;i++)map.Add(new Owner(i),new Owner(i*10));
                 nuint capacity=map.Capacity;Owner probe=new(9);
                 try{map.Extract(&probe);return 1;}catch(ref Exception error){}
                 if(map.Count!=3||map.Capacity!=capacity||dead!=0)return 2;
                 probe.Id=2;
                 for(int remaining=0;remaining<3;remaining++){
                  budget=remaining;
                  try{map.Extract(&probe);return 3;}catch(ref Exception error){}
                  budget=-1;
                  if(map.Count!=3||dead!=0)return 10;
                 }
                 if(map.Count!=3||map.Capacity!=capacity||dead!=0)return 4;
                 var iterator=map.GetEnumerator();
                 for(int i=1;i<=3;i++){
                  if(!iterator.MoveNext()||iterator.Current.Key.Id!=i||iterator.Current.ValueReadOnly.Id!=i*10)return 5;}
                 if(iterator.MoveNext())return 6;
                 {SortedMap<Owner,Owner>::Entry extracted=map.Extract(&probe);
                  if(extracted.Key.Id!=2||extracted.Value.Id!=20||map.Count!=2)return 7;}
                 if(dead!=2)return 8;}
                if(dead!=7||allocations!=releases)return 9;return 0;
            }
            """,optimization);

    private static void Run(string code,int optimization)
    {
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("main.gf",Prelude+code));
        Assert.Equal(0,CompilerTestHelper.Run(sources,optimization).ExitCode);
    }
}
