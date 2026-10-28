using System.Text;
using gflat.CompileExceptions;

namespace gflat.Tests.Std.Collections;

public sealed class SortedMapRangeTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void HalfOpenRangesMatchHostSortedDictionaryInComparerOrder(int optimization)
    {
        var code = new StringBuilder("""
            using std::Collections;using std::Memory;
            int Ascending(immutable(int)* a,immutable(int)* b){if(*a<*b)return -1;if(*a>*b)return 1;return 0;}
            int Descending(immutable(int)* a,immutable(int)* b)=>Ascending(b,a);
            int Magnitude(immutable(int)* a,immutable(int)* b){int x=*a;int y=*b;if(x<0)x=-x;if(y<0)y=-y;return Ascending(&x,&y);}
            int main(){
            """);
        int[] keys = [20, -5, 10, -20, 5, 0, 30, -10, 15, -30, 25];
        var random = new Random(91247);
        foreach (string kind in new[] { "Ascending", "Descending", "Magnitude" })
        {
            Comparison<int> compare = kind switch
            {
                "Descending" => (a, b) => b.CompareTo(a),
                "Magnitude" => (a, b) => Math.Abs(a).CompareTo(Math.Abs(b)),
                _ => (a, b) => a.CompareTo(b)
            };
            var reference = new SortedDictionary<int, int>(Comparer<int>.Create(compare));
            code.AppendLine($"{{SortedMap<int,int> map=new({kind});int lower=0;int upper=1;");
            code.AppendLine("var empty=map.GetRange(&lower,&upper);if(empty.Length!=0||empty.Data!=null)return 1;");
            foreach (int key in keys)
            {
                reference[key] = key * 10;
                code.AppendLine($"map.Set({key},{key * 10});");
            }
            code.AppendLine("immutable(SortedMap<int,int>)* view=&map;readonly(SortedMap<int,int>)* shallow=&map;");
            List<(int Lower, int Upper)> bounds = [(-40, 40), (40, -40), (5, 20), (20, 5), (10, 10),
                (6, 9), (31, 39), (-40, -30), (0, 1), (-20, 20), (-21, 21), (0, 0)];
            for (int i = 0; i < 20; i++) bounds.Add((random.Next(-40, 41), random.Next(-40, 41)));
            for (int i = 0; i < bounds.Count; i++)
            {
                var (lower, upper) = bounds[i];
                var expected = reference.Where(pair => compare(pair.Key, lower) >= 0 && compare(pair.Key, upper) < 0).ToArray();
                code.AppendLine($"{{lower={lower};upper={upper};var range={(i % 2 == 0 ? "view" : "shallow")}.GetRange(&lower,&upper);");
                code.AppendLine($"if(range.Length!={expected.Length}||map.Count!={reference.Count})return 2;");
                if (expected.Length == 0) code.AppendLine("if(range.Data!=null)return 3;");
                for (int n = 0; n < expected.Length; n++)
                    code.AppendLine($"if(*range[{n}].Key!={expected[n].Key}||*range[{n}].ValueImmutable!={expected[n].Value})return 4;");
                code.AppendLine("}");
            }
            code.AppendLine("}");
        }
        code.AppendLine("return 0;}");
        Run(code.ToString(), optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RangesBorrowNoncopyableOwnersAndComparerFailuresPreserveTheMap(int optimization)
        => Run("""
            using std::Collections;using std::Memory;
            extern void*? malloc(nuint count);extern void free(void* storage);
            int allocations=0;int releases=0;int dead=0;int calls=0;int budget=-1;bool deny=false;
            namespace Memory{
                public replace void*? Allocate(nuint count){allocations++;if(deny)return null;return malloc(count);}
                public replace void Free(void* storage) nothrow{releases++;free(storage);}
            }
            struct Owner{public int Id;public Owner(int id){Id=id;}~Owner(){dead++;}}
            int Compare(immutable(Owner)* a,immutable(Owner)* b){
                calls++;if(budget==0)throw new Exception("range comparison",73);if(budget>0)budget--;
                if(a.Id<b.Id)return -1;if(a.Id>b.Id)return 1;return 0;
            }
            int main(){
                {SortedMap<Owner,Owner> map=new(Compare);
                 for(int i=0;i<8;i++)map.Add(new Owner(i),new Owner(i*10));
                 Owner lower=new(2);Owner upper=new(6);nuint capacity=map.Capacity;
                 int allocationsBefore=allocations;deny=true;calls=0;
                 immutable(SortedMap<Owner,Owner>)* view=&map;
                 var range=view.GetRange(&lower,&upper);int comparisons=calls;
                 if(range.Length!=4||range[0].Key.Id!=2||range[3].Key.Id!=5||dead!=0||allocations!=allocationsBefore)return 1;
                 if(range[0].ValueImmutable!=map.FindImmutable(&lower))return 2;
                 map.Find(&lower)!.Id=123;if(range[0].ValueImmutable.Id!=123)return 3;
                 var iterator=map.GetEnumerator();iterator.MoveNext();
                 immutable(Owner)* storedLower=iterator.Current.Key;
                 var aliased=view.GetRange(storedLower,range[2].Key);
                 if(aliased.Length!=4||aliased[0].Key.Id!=0||aliased[3].Key.Id!=3)return 4;
                 if(allocations!=allocationsBefore||dead!=0)return 5;deny=false;
                 for(int remaining=0;remaining<comparisons;remaining++){
                    budget=remaining;bool caught=false;
                    try{view.GetRange(&lower,&upper);}catch(ref Exception error){if(error.GetCode()!=73)return 6;caught=true;}
                    budget=-1;if(!caught||map.Count!=8||map.Capacity!=capacity||dead!=0)return 7;
                    var check=map.GetEnumerator();
                    for(int i=0;i<8;i++){
                        if(!check.MoveNext()||check.Current.Key.Id!=i)return 8;
                        int expected=i*10;if(i==2)expected=123;
                        if(check.Current.ValueImmutable.Id!=expected)return 9;
                    }
                    if(check.MoveNext())return 10;
                 }
                 if(range[0].ValueImmutable.Id!=123)return 11;
                }
                if(dead!=18||allocations!=releases)return 12;return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ViewsDoNotInvokeThrowingCopyConstructors(int optimization)
        => Run("""
            using std::Collections;
            int copies=0;int made=0;int dead=0;
            struct Item{
                public int Id;
                public Item(int id){Id=id;made++;}
                public copy Item(immutable(Item)* other){copies++;throw new Exception("unexpected copy");}
                ~Item(){dead++;}
            }
            int Compare(immutable(Item)* a,immutable(Item)* b){if(a.Id<b.Id)return -1;if(a.Id>b.Id)return 1;return 0;}
            int main(){
                {SortedMap<Item,Item> map=new(Compare);map.Add(new Item(1),new Item(10));map.Add(new Item(2),new Item(20));
                 Item lower=new(1);Item upper=new(3);
                 var range=map.GetRange(&lower,&upper);
                 if(range.Length!=2||range[0].Key.Id!=1||range[1].ValueImmutable.Id!=20||copies!=0||dead!=0)return 1;}
                if(copies!=0||made!=dead)return 2;return 0;
            }
            """, optimization);

    [Theory]
    [InlineData("*range[0].Key=99;")]
    [InlineData("range[0].Value=99;")]
    [InlineData("*range[0].ValueImmutable=99;")]
    public void BorrowedRangeCannotMutateKeysOrValues(string invalid)
    {
        const string setup = """
            using std::Collections;
            int Compare(immutable(int)* a,immutable(int)* b){if(*a<*b)return -1;if(*a>*b)return 1;return 0;}
            int main(){SortedMap<int,int> map=new(Compare);map.Add(1,10);int lower=0;int upper=2;
                readonly(SortedMap<int,int>)* view=&map;var range=view.GetRange(&lower,&upper);
            """;
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Sources(setup + invalid + "return 0;}")));
        Assert.Contains("readonly", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<SourceFile> Sources(string program)
    {
        string[] files = ["Collections/IEnumerable.gf", "Queries/Enumerable.gf", "Collections/Vector.gf",
            "Memory/Span.gf", "Collections/SortedMap.gf"];
        return files.Select(file => new SourceFile(file,
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", file))))
            .Append(new SourceFile("sorted-map-range.gf", program));
    }

    private static void Run(string program, int optimization)
    {
        var result = CompilerTestHelper.Run(Sources(program), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }
}
