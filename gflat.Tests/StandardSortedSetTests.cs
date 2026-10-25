using System.Text;
using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class StandardSortedSetTests
{
    private const string Owners = """
        using std; using std::Numerics; using std::Queries; using std::Collections;
        int made=0;int dead=0;int budget=-1;int allocations=0;int releases=0;bool fail=false;
        private extern void*? malloc(nuint bytes);private extern void free(void* storage);
        namespace Memory {
            public replace void*? Allocate(nuint bytes){if(fail)return null;void*? result=malloc(bytes);if(result!=null)allocations++;return result;}
            public replace void Free(void* storage) nothrow{releases++;free(storage);}
        }
        struct Owned {
            public int Id;public int Identity;public String Text;
            public Owned(int id,int identity){Id=id;Identity=identity;Text=s"owned";made++;}
            ~Owned(){dead++;}
        }
        int Compare(immutable(Owned)* left,immutable(Owned)* right){
            if(budget==0)throw new Exception("comparison");if(budget>0)budget--;
            return left.Id-right.Id;
        }
        bool Matches(immutable(SortedSet<Owned>)* values){
            if(values.Count!=8)return false;
            var iterator=values.GetEnumerator();
            for(int i=0;i<8;i++){
                if(!iterator.MoveNext() || iterator.Current.Id!=i || iterator.Current.Identity!=i || iterator.Current.Text.Length!=5)return false;
                if(!values.Contains(iterator.Current))return false;
            }
            return !iterator.MoveNext();
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MixedMutationsAndReadonlyIterationMatchDotNetInBothOrders(int optimization)
    {
        var source=new StringBuilder("""
            using std; using std::Numerics; using std::Queries; using std::Collections;
            int Ascending(readonly(int)* a,readonly(int)* b)=>*a-*b;
            int Descending(readonly(int)* a,readonly(int)* b)=>*b-*a;
            bool Matches(immutable(SortedSet<int>)* values,readonly(int)* expected,nuint length){
                if(values.Count!=length || values.IsEmpty!=(length==0) || values.Capacity<length)return false;
                var iterator=values.GetEnumerator();
                for(nuint i=0;i<length;i++)if(!iterator.MoveNext() || *iterator.Current!=expected[i] || !values.Contains(iterator.Current))return false;
                return !iterator.MoveNext();
            }
            int main(){
            """);
        foreach(bool descending in new[]{false,true})
        {
            var random=new Random(17191);
            var values=new SortedSet<int>(descending?Comparer<int>.Create((a,b)=>b.CompareTo(a)):Comparer<int>.Default);
            source.AppendLine($"{{SortedSet<int> values=new({(descending?"Descending":"Ascending")});");
            for(int step=0;step<180;step++)
            {
                int key=random.Next(-30,31), operation=random.Next(100);
                if(operation<48)source.AppendLine($"if(values.Add({key})!={(values.Add(key)?"true":"false")})return 1;");
                else if(operation<82)source.AppendLine($"{{int key={key};if(values.Remove(&key)!={(values.Remove(key)?"true":"false")})return 2;}}");
                else if(operation<94)source.AppendLine($"values.Reserve({random.Next(1,70)});");
                else {values.Clear();source.AppendLine("values.Clear();");}
                int[] expected=values.ToArray();
                source.AppendLine($"{{int[{Math.Max(1,expected.Length)}] expected=["+string.Join(",",expected.Length==0?[0]:expected)+"];");
                source.AppendLine($"if(!Matches(&values,(readonly(int)*)expected,{expected.Length}))return 3;}}");
            }
            source.AppendLine("}");
        }
        source.AppendLine("return 0;}");Run(source.ToString(),optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NoncopyableDuplicatePreservesOriginalIdentityAndBorrowedRemovalQueryWorks(int optimization)
        => Run(Owners+"""
            int main(){ {
                SortedSet<Owned> values=new(Compare);values.Add(new Owned(3,11));
                immutable(SortedSet<Owned>)* read=&values;
                var first=read.GetEnumerator();first.MoveNext();immutable(Owned)* saved=first.Current;
                int before=allocations;
                if(values.Add(new Owned(3,99)) || dead!=1 || values.Count!=1 || saved.Identity!=11)return 1;
                if(allocations!=before+1 || saved.Text.Length!=5)return 2;
                if(!read.Contains(saved))return 3;
                values.Add(new Owned(1,12));values.Add(new Owned(5,13));
                var iterator=read.GetEnumerator();iterator.MoveNext();iterator.MoveNext();
                if(!values.Remove(iterator.Current) || dead!=2 || values.Count!=2)return 4;
                SortedSet<Owned> moved=move values;var left=moved.GetEnumerator();var right=moved.GetEnumerator();
                if(!left.MoveNext() || !right.MoveNext() || left.Current.Id!=1 || right.Current.Id!=1)return 5;
                if(!left.MoveNext() || left.Current.Id!=5 || right.Current.Id!=1)return 6;
                moved.Clear();moved.Clear();if(dead!=4 || !moved.IsEmpty)return 7;
            }
            if(made!=dead || allocations!=releases)return 8;return 0;}
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ComparisonFailuresPreserveExistingOwnersAtEveryLookupDepth(int optimization)
        => Run(Owners+"""
            int main(){ {
                SortedSet<Owned> values=new(Compare);for(int i=0;i<8;i++)values.Add(new Owned(i,i));
                Owned query=new Owned(3,30);immutable(SortedSet<Owned>)* read=&values;
                var iterator=read.GetEnumerator();iterator.MoveNext();immutable(Owned)* saved=iterator.Current;
                for(int operation=0;operation<3;operation++)for(int failure=0;failure<4;failure++){
                    budget=failure;bool caught=false;
                    try{if(operation==0)read.Contains(&query);else if(operation==1)values.Remove(&query);else values.Add(new Owned(3,99));}
                    catch{caught=true;}budget=-1;
                    if(!caught || made-dead!=9 || !Matches(read))return 1;
                    var current=read.GetEnumerator();current.MoveNext();if(current.Current!=saved)return 2;
                }
            }
            if(made!=dead || allocations!=releases)return 3;return 0;}
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AllocationFailurePreservesAddressesAndDuplicateInsertionNeedsNoStorageGrowth(int optimization)
        => Run(Owners+"""
            int main(){ {
                SortedSet<Owned> values=new(Compare);values.Reserve(1);values.Add(new Owned(5,50));
                var iterator=values.GetEnumerator();iterator.MoveNext();immutable(Owned)* saved=iterator.Current;
                Owned incoming=new Owned(1,10);fail=true;bool caught=false;
                try{values.Add(move incoming);}catch{caught=true;}fail=false;
                if(!caught || values.Count!=1 || values.Capacity!=1 || saved.Id!=5 || made-dead!=1)return 1;
                Owned duplicate=new Owned(5,99);fail=true;bool added=values.Add(move duplicate);fail=false;
                if(added || values.Count!=1 || saved.Identity!=50 || made-dead!=1)return 2;
                fail=true;caught=false;try{values.Reserve(100);}catch{caught=true;}fail=false;
                if(!caught || values.Capacity!=1)return 3;
                var after=values.GetEnumerator();after.MoveNext();if(after.Current!=saved)return 4;
            }
            if(made!=dead || allocations!=releases)return 5;return 0;}
            """,optimization);

    [Fact]
    public void BorrowedKeysCannotBeMutated()
    {
        var error=Assert.Throws<TypeCheckException>(()=>Compiler.Check(Sources(Owners+"""
            int main(){SortedSet<Owned> values=new(Compare);values.Add(new Owned(1,1));
                var iterator=values.GetEnumerator();iterator.MoveNext();iterator.Current.Id=2;return 0;}
            """)));
        Assert.Contains("readonly",error.Message,StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<SourceFile> Sources(string program)
        => new[]{"Collections/IEnumerable.gf","Queries/Enumerable.gf","Span.gf","Collections/Vector.gf","Collections/SortedMap.gf","Collections/SortedSet.gf","IStringConvertable.gf","IHashable.gf","IEquatable.gf","IComparable.gf","Hashing.gf","String.gf", "ByteSearch.gf"}
            .Select(name=>new SourceFile(name,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core",name))))
            .Append(new SourceFile("sorted-set.gf",program));

    private static void Run(string program,int optimization)
    {
        string directory=Path.Combine(Path.GetTempPath(),"gflat_sorted_set_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try{
            string ir=Path.Combine(directory,"set.ll"),executable=Path.Combine(directory,"set.exe");File.WriteAllText(ir,Compiler.Emit(Sources(program),options:new(){InternalizeFunctions=true}));
            var compilation=NativeToolchain.Compile(ir,executable,optimizationLevel:optimization);Assert.True(compilation.ExitCode==0,compilation.StandardError);
            var result=NativeToolchain.Run(executable,[]);Assert.True(result.ExitCode==0,$"Sorted set exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }finally{Directory.Delete(directory,true);}
    }
}
