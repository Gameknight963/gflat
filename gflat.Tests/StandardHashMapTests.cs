namespace gflat.Tests;

public class StandardHashMapTests
{
    private static IEnumerable<SourceFile> Sources(string code) =>
        new[] { "IHashable.gf", "IEquatable.gf", "IComparable.gf", "Hashing.gf", "Collections/HashMap.gf", "Collections/HashSet.gf", "IStringConvertable.gf", "String.gf", "ByteSearch.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", name))))
            .Append(new SourceFile("main.gf", code));
    private static void Run(string code) => Assert.Equal(0, CompilerTestHelper.Run(Sources(code)).ExitCode);

    [Fact]
    public void AutomaticGrowthAllocatesLogarithmicallyRatherThanRehashingEachInsertion()
        => Run("""
            using std;
            int allocations=0; int releases=0;
            private extern void*? malloc(nuint bytes);
            private extern void free(void* address);
            namespace Memory {
                public replace void*? Allocate(nuint bytes) { allocations++; return malloc(bytes); }
                public replace void Free(void* address) nothrow { releases++; free(address); }
            }
            int main() {
                {
                    HashMap<int,int> map=new();
                    for(int i=0;i<1000;i++) map.Add(i,i);
                    if(allocations>10 || map.Count!=1000 || map.Capacity<1000) return 1;
                    for(int i=0;i<1000;i++) if(map[&i]!=i) return 2;
                }
                if(allocations!=releases) return 3;
                int before=allocations;
                {
                    HashSet<int> set=new();
                    for(int i=0;i<1000;i++) if(!set.Add(i)) return 4;
                    if(allocations-before>10 || set.Count!=1000) return 5;
                }
                if(allocations!=releases) return 6;
                return 0;
            }
            """);

    [Fact]
    public void GrowthDuplicatesAndRemovalDestroyEachOwnedResourceOnce()
        => Run("""
            using std;
            struct Key : IHashable, IEquatable<Key> {
                public int id; private int* destroyed;
                public Key(int n,int* counter) { id=n; destroyed=counter; }
                public immutable nuint GetHashCode()=>(nuint)(id%3);
                public immutable bool Equals(immutable(Key)* other)=>id==other.id;
                ~Key() { (*destroyed)++; }
            }
            struct Value {
                private int* destroyed;
                public Value(int* counter) { destroyed=counter; }
                ~Value() { (*destroyed)++; }
            }
            int main() {
                int keys=0; int values=0;
                {
                    HashMap<Key,Value> map=new();
                    for(int i=0;i<100;i++) map.Add(new Key(i,&keys),new Value(&values));
                    if(keys!=0 || values!=0) return 1;
                    if(map.TryAdd(new Key(7,&keys),new Value(&values))) return 2;
                    if(keys!=1 || values!=1) return 3;
                    Key lookup=new Key(7,&keys);
                    if(!map.Remove(&lookup) || keys!=2 || values!=2) return 4;
                    map.Reserve(1000);
                    map.Clear(); if(keys!=101 || values!=101) return 5;
                    map.Add(new Key(101,&keys),new Value(&values));
                }
                if(keys!=103 || values!=102) return 6;
                return 0;
            }
            """);

    [Fact]
    public void FailedGrowthAndThrowingKeyComparisonPreserveExistingEntries()
        => Run("""
            using std;
            bool fail=false; bool compareFail=false;
            private extern void*? malloc(nuint bytes);
            private extern void free(void* address);
            namespace Memory {
                public replace void*? Allocate(nuint bytes) { if(global::fail) return null; return malloc(bytes); }
                public replace void Free(void* address) nothrow { free(address); }
            }
            struct Key : IHashable, IEquatable<Key> {
                public int id; public Key(int n) { id=n; }
                public immutable nuint GetHashCode()=>0;
                public immutable bool Equals(immutable(Key)* other) {
                    if(compareFail) throw new Exception("comparison failed");
                    return id==other.id;
                }
            }
            int main() {
                HashMap<Key,int> map=new(); for(int i=0;i<4;i++) map.Add(new Key(i),i);
                fail=true; int caught=0;
                try { map.Add(new Key(4),4); } catch { caught++; }
                fail=false;
                compareFail=true;
                try { map.Add(new Key(5),5); } catch { caught++; }
                compareFail=false;
                if(caught!=2 || map.Count!=4) return 1;
                for(int i=0;i<4;i++) { Key key=new Key(i); if(map[&key]!=i) return 2; }
                map.Add(new Key(4),4); if(map.Count!=5) return 3;
                return 0;
            }
            """);

    [Fact]
    public void BorrowedEntriesPreventKeyMutation()
    {
        Assert.Throws<gflat.CompileExceptions.TypeCheckException>(() => Compiler.Emit(Sources("""
            using std;
            struct Key : IHashable, IEquatable<Key> {
                public int id;
                public immutable nuint GetHashCode()=>(nuint)id;
                public immutable bool Equals(immutable(Key)* other)=>id==other.id;
            }
            int main() {
                HashMap<Key,int> map=new(); map.Add(new Key(),1);
                HashMap<Key,int>::Enumerator e=map.GetEnumerator(); e.MoveNext();
                e.Current.Key.id=5; return 0;
            }
            """)));
    }

    [Fact]
    public void GrowthReplacementRemovalAndReinsertionPreserveProbeChains()
        => Run("""
            using std;
            int main() {
                HashMap<int,int> map = new();
                for(int i=0;i<300;i++) map.Add(i,i*3);
                if(map.Count!=300 || map.Capacity<300) return 1;
                for(int i=0;i<300;i++) if(map[&i]!=i*3) return 2;
                for(int i=0;i<300;i+=2) if(!map.Remove(&i)) return 3;
                for(int i=0;i<300;i++) {
                    if(i%2==0) { if(map.ContainsKey(&i)) return 4; map.Set(i, i+1000); }
                    else map.Set(i, i+2000);
                }
                if(map.Count!=300) return 5;
                for(int i=0;i<300;i++) {
                    int expected=i+2000; if(i%2==0) expected=i+1000;
                    if(map[&i]!=expected) return 6;
                }
                map.Clear(); if(!map.IsEmpty) return 7;
                map.Add(42,7); int key=42; map[&key]=8;
                if(map[&key]!=8) return 8;
                return 0;
            }
            """);

    [Fact]
    public void CollisionsTombstonesAndDuplicateInsertionsDoNotLoseEntries()
        => Run("""
            using std;
            struct Key : IHashable, IEquatable<Key> {
                public int value;
                public Key(int x) { value=x; }
                public immutable nuint GetHashCode()=>0;
                public immutable bool Equals(immutable(Key)* other)=>value==other.value;
            }
            int main() {
                HashMap<Key,int> map=new();
                for(int i=0;i<40;i++) map.Add(new Key(i),i);
                Key k=new Key(0); if(!map.Remove(&k)) return 1;
                k.value=20; if(!map.Remove(&k)) return 2;
                if(map.TryAdd(new Key(21),99)) return 3;
                k.value=21; if(map[&k]!=21 || map.Count!=38) return 4;
                map.Add(new Key(100),100);
                for(int i=1;i<40;i++) { if(i!=20) { k.value=i; if(map[&k]!=i) return 5; } }
                bool caught=false; try { map.Add(new Key(1),2); } catch { caught=true; }
                if(!caught || map.Count!=39) return 6;
                return 0;
            }
            """);

    [Fact]
    public void OwningStringKeysAndValuesSurviveGrowthAndBorrowedIteration()
        => Run("""
            using std;
            int main() {
                HashMap<String,String> map=new();
                map.Add(s"first",s"one"); map.Add(s"second",s"two");
                String key=s"first";
                String*? value=map.Find(&key); if(value==null || value!.Length!=3) return 1;
                value!.AppendLiteral("!",1);
                map.Reserve(500);
                if(map[&key].Length!=4) return 2;
                map.Set(s"first",s"replacement");
                if(map[&key].Length!=11 || map.Count!=2) return 3;
                HashMap<String,String>::Enumerator e=map.GetEnumerator(); nuint seen=0;
                while(e.MoveNext()) { if(e.Current.Key.Length==0 || e.Current.ValueImmutable.Length==0) return 4; seen++; }
                if(seen!=2) return 5;
                immutable(HashMap<String,String>)* ro=&map;
                if(ro.FindImmutable(&key)==null || !ro.ContainsKey(&key)) return 6;
                if(!map.Remove(&key) || map.Find(&key)!=null) return 7;
                return 0;
            }
            """);

    [Fact]
    public void SetOwnsUniqueValuesAndBorrowsDuringIteration()
        => Run("""
            using std;
            int main() {
                HashSet<String> set=new();
                if(!set.Add(s"a") || !set.Add(s"b") || set.Add(s"a")) return 1;
                String key=s"a"; if(!set.Contains(&key) || set.Count!=2) return 2;
                HashSet<String>::Enumerator e=set.GetEnumerator(); nuint n=0;
                while(e.MoveNext()) { if(e.Current.Length!=1) return 3; n++; }
                if(n!=2 || !set.Remove(&key) || set.Contains(&key)) return 4;
                set.Clear(); if(!set.IsEmpty) return 5;
                return 0;
            }
            """);

    [Fact]
    public void CapacityOverflowAndMissingKeysLeaveMapUsable()
        => Run("""
            using std;
            int main() {
                HashMap<int,int> map=new(); int k=8;
                if(map.Find(&k)!=null || map.ContainsKey(&k) || map.Remove(&k)) return 1;
                int caught=0;
                try { map[&k]; } catch { caught++; }
                try { map.Reserve((nuint)0-(nuint)1); } catch { caught++; }
                map.Add(k,12); if(caught!=2 || map[&k]!=12) return 2;
                map.Clear(); map.Clear(); map.Add(4,5); k=4;
                if(map[&k]!=5) return 3; return 0;
            }
            """);
}
