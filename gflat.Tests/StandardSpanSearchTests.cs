namespace gflat.Tests;

public sealed class StandardSpanSearchTests
{
    private static IEnumerable<SourceFile> Sources(string code)
        => new[] { "Collections/IEnumerable.gf", "Collections/Enumerable.gf", "Collections/Algorithms.gf",
            "Span.gf", "Vector.gf", "String.gf", "ByteSearch.gf", "IStringConvertable.gf", "IHashable.gf", "IEquatable.gf", "IComparable.gf", "Hashing.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", name))))
            .Append(new SourceFile("main.gf", code));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SearchesMatchIndependentLinearOracleForEverySliceAndKey(int optimization)
        => Run("""
            using std;
            int calls = 0;
            bool Equal(readonly(int)* a, readonly(int)* b) { calls++; return *a == *b; }
            int main() {
                int[12] data = [7,2,7,0,2,9,7,2,2,9,0,7];
                Span<int> mutable = new((int*)data,12);
                for(nuint start=0;start<=12;start++) {
                    for(nuint length=0;length<=12-start;length++) {
                        Span<readonly(int)> values=mutable.AsImmutable().Slice(start,length);
                        for(int key=-1;key<=10;key++) {
                            nuint first=length; nuint last=length;
                            for(nuint i=0;i<length;i++) if(data[start+i]==key) {
                                if(first==length) first=i;
                                last=i;
                            }
                            calls=0;
                            if(IndexOf<int>(values,&key,Equal)!=first) return 1;
                            int firstCalls=(int)length;
                            if(first!=length) firstCalls=(int)first+1;
                            if(calls!=firstCalls) return 2;
                            calls=0;
                            if(LastIndexOf<int>(values,&key,Equal)!=last) return 3;
                            int expected=(int)length;
                            if(last!=length) expected=(int)(length-last);
                            if(calls!=expected) return 4;
                            if(Contains<int>(values,&key,EqualityComparer<int>::Equals)!=(first!=length)) return 5;
                        }
                    }
                }
                mutable[0]=42;
                int changed=42;
                if(IndexOf<int>(mutable.AsImmutable(),&changed,Equal)!=0) return 6;
                return 0;
            }
            """, optimization, "EqualityComparer.gf");

    private const string Owning = """
        using std;
        extern void*? malloc(nuint size);
        extern void free(void* storage);
        int allocations=0; int releases=0; int destroyed=0; int calls=0; int failAt=0;
        namespace Memory {
            public replace void*? Allocate(nuint size) { allocations++; return malloc(size); }
            public replace void Free(void* storage) nothrow { releases++; free(storage); }
        }
        struct Item {
            public int Key;
            public String Name;
            public Item(int key) { Key=key; Name=s"owned"; }
            ~Item() { destroyed++; }
        }
        bool Equal(immutable(Item)* a,immutable(Item)* b) {
            calls++;
            if(failAt!=0 && calls==failAt) throw new Exception("comparison failed");
            return a.Key==b.Key && a.Name.Equals(&b.Name);
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SearchesBorrowNoncopyableOwningElementsAndAllocateNothing(int optimization)
        => Run(Owning + """
            int main() {
                {
                    Vector<Item> items=new();
                    items.Add(new Item(2));items.Add(new Item(3));items.Add(new Item(2));
                    Span<immutable(Item)> values=items.AsImmutable();
                    int before=allocations; int freed=releases;
                    for(int i=0;i<100;i++) {
                        if(IndexOf<Item>(values,&items[2],Equal)!=0) return 1;
                        if(LastIndexOf<Item>(values,&items[0],Equal)!=2) return 2;
                        if(!Contains<Item>(values,&items[1],Equal)) return 3;
                    }
                    if(allocations!=before || releases!=freed || destroyed!=0) return 4;
                    if(items[0].Name.Length!=5 || items[1].Key!=3 || items[2].Key!=2) return 5;
                }
                if(destroyed!=3 || allocations!=releases) return 6;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ThrowingComparisonsPreserveSourceAndShortCircuitEmptySpans(int optimization)
        => Run(Owning + """
            int main() {
                {
                    Vector<Item> items=new();
                    items.Add(new Item(1));items.Add(new Item(2));items.Add(new Item(3));
                    Span<immutable(Item)> values=items.AsImmutable();
                    failAt=1;
                    Span<immutable(Item)> empty=new();
                    if(IndexOf<Item>(empty,&items[0],Equal)!=0 || LastIndexOf<Item>(empty,&items[0],Equal)!=0) return 1;
                    if(Contains<Item>(empty,&items[0],Equal) || calls!=0) return 2;
                    failAt=2; calls=0;
                    try { IndexOf<Item>(values,&items[2],Equal); return 3; } catch(ref Exception error) {}
                    if(calls!=2 || destroyed!=0) return 4;
                    calls=0;
                    try { LastIndexOf<Item>(values,&items[0],Equal); return 5; } catch(ref Exception error) {}
                    if(calls!=2 || destroyed!=0) return 6;
                    calls=0;
                    try { Contains<Item>(values,&items[2],Equal); return 7; } catch(ref Exception error) {}
                    if(calls!=2 || destroyed!=0) return 8;
                    for(nuint i=0;i<3;i++) if(items[i].Key!=(int)i+1 || items[i].Name.Length!=5) return 9;
                }
                if(destroyed!=3 || allocations!=releases) return 10;
                return 0;
            }
            """, optimization);

    private static void Run(string code, int optimization, string? extra = null)
    {
        IEnumerable<SourceFile> sources = Sources(code);
        if(extra != null) sources = sources.Append(new SourceFile(extra,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core",extra))));
        Assert.Equal(0,CompilerTestHelper.Run(sources,optimization).ExitCode);
    }
}
