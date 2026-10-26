using System.Text;

namespace gflat.Tests;

public sealed class StandardHashSetMutationTests
{
    private const string Owners="""
        using std; using std::Memory; using std::Numerics; using std::Collections;
        int made=0;int dead=0;int copies=0;int hashCalls=0;int equalityCalls=0;
        int failCopy=0;int failHash=0;int failEquality=0;int allocations=0;int releases=0;bool failLarge=false;
        private extern void*? malloc(nuint bytes);private extern void free(void* storage);
        namespace Memory{
            public replace void*? Allocate(nuint bytes){if(failLarge && bytes>=100)return null;void*? result=malloc(bytes);if(result!=null)allocations++;return result;}
            public replace void Free(void* storage) nothrow{releases++;free(storage);}
        }
        struct Key:IHashable,IEquatable<Key>{
            public int Value;public String Token;
            public Key(int value){Value=value;Token=s"owner";made++;}
            public copy Key(immutable(Key)* source){copies++;Value=source.Value;Token=source.Token;if(copies==failCopy)throw new Exception("copy");made++;}
            ~Key(){dead++;}
            public immutable nuint GetHashCode(){hashCalls++;if(hashCalls==failHash)throw new Exception("hash");return 1;}
            public immutable bool Equals(immutable(Key)* other){equalityCalls++;if(equalityCalls==failEquality)throw new Exception("equality");return Value==other.Value;}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MixedMutationsMatchDotNetIncludingSelfAndReadonlySources(int optimization)
    {
        var random=new Random(81119);HashSet<int>[] sets=[[],[]];
        var source=new StringBuilder("""
            using std; using std::Memory; using std::Numerics; using std::Collections;extern int puts(immutable(char*) text);
            bool Matches(immutable(HashSet<int>)* values,readonly(int)* expected,nuint count){
                if(values.Count!=count)return false;for(nuint i=0;i<count;i++)if(!values.Contains(expected+i))return false;return true;
            }
            int main(){HashSet<int> a=new();HashSet<int> b=new();
            """);
        for(int step=0;step<220;step++){
            int index=random.Next(2),other=1-index,operation=random.Next(100),value=random.Next(50);string target=index==0?"a":"b",input=other==0?"a":"b";
            if(operation<26){sets[index].Add(value);source.AppendLine($"{target}.Add({value});");}
            else if(operation<45){sets[index].Remove(value);source.AppendLine($"{{int key={value};{target}.Remove(&key);}}");}
            else if(operation<65){sets[index].UnionWith(sets[other]);source.AppendLine($"{target}.UnionWith(&{input});");}
            else if(operation<85){sets[index].ExceptWith(sets[other]);source.AppendLine($"{target}.ExceptWith(&{input});");}
            else if(operation<95)source.AppendLine($"{target}.UnionWith(&{target});");
            else{sets[index].Clear();source.AppendLine($"{target}.ExceptWith(&{target});");}
            for(int set=0;set<2;set++){
                int[] expected=sets[set].Order().ToArray();source.AppendLine($"{{int[{Math.Max(1,expected.Length)}] expected=["+string.Join(",",expected.Length==0?[0]:expected)+"];");
                source.AppendLine($"if(!Matches(&{(set==0?"a":"b")},(readonly(int)*)expected,{expected.Length})){{puts(\"set{set} step{step}\");return 1;}} }}");
            }
        }
        source.AppendLine("return 0;}");Run(source.ToString(),optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ExceptSupportsNoncopyableOwnersAndSelfClearsWithoutAllocating(int optimization)
        => Run(Owners+"""
            struct Unique:IHashable,IEquatable<Unique>{
                public int Value;public String Token;public Unique(int value){Value=value;Token=s"unique";made++;}~Unique(){dead++;}
                public immutable nuint GetHashCode()=>1;
                public immutable bool Equals(immutable(Unique)* other)=>Value==other.Value;
            }
            int main(){ {
                HashSet<Unique> target=new();HashSet<Unique> source=new();
                for(int i=0;i<8;i++){target.Add(new Unique(i));source.Add(new Unique(i+4));}
                int before=allocations;immutable(HashSet<Unique>)* read=&source;target.ExceptWith(read);
                if(target.Count!=4 || source.Count!=8 || made!=16 || dead!=4 || allocations!=before || copies!=0)return 1;
                var iterator=source.GetEnumerator();while(iterator.MoveNext())if(iterator.Current.Token.Length!=6)return 2;
                target.ExceptWith(&target);if(target.Count!=0 || dead!=8 || allocations!=before)return 3;
                HashSet<Unique> moved=move source;moved.ExceptWith(&moved);if(dead!=16 || !moved.IsEmpty)return 4;
                target.ExceptWith(&moved);
            }if(made!=dead || allocations!=releases)return 5;return 0;}
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void UnionSkipsDuplicateAndSelfCopiesAndOwnsIndependentNewKeys(int optimization)
        => Run(Owners+"""
            int main(){ {
                HashSet<Key> target=new();HashSet<Key> source=new();
                for(int i=0;i<8;i++){target.Add(new Key(i));source.Add(new Key(i));}
                immutable(HashSet<Key>)* read=&source;
                int before=allocations;failCopy=1;target.UnionWith(read);
                if(copies!=0 || target.Count!=8 || source.Count!=8 || allocations!=before)return 1;
                failHash=hashCalls+1;target.UnionWith(&target);failHash=0;
                if(copies!=0 || allocations!=before)return 2;
                failCopy=0;for(int i=8;i<12;i++)source.Add(new Key(i));
                target.UnionWith(read);if(copies!=4 || target.Count!=12 || source.Count!=12 || made-dead!=24)return 3;
                readonly(char)*? a=null;readonly(char)*? b=null;
                var left=target.GetEnumerator();while(left.MoveNext())if(left.Current.Value==8)a=left.Current.Token.Data;
                var right=source.GetEnumerator();while(right.MoveNext())if(right.Current.Value==8)b=right.Current.Token.Data;
                if(a==null || b==null || a==b)return 4;
            }if(made!=dead || allocations!=releases)return 5;return 0;}
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CopyHashEqualityAndAllocationFailuresKeepSourcesAndCompletedProgress(int optimization)
        => Run(Owners+"""
            int main(){
                {HashSet<Key> target=new();HashSet<Key> source=new();target.Reserve(8);target.Add(new Key(0));for(int i=1;i<=4;i++)source.Add(new Key(i));
                    copies=0;failCopy=2;bool caught=false;try{target.UnionWith(&source);}catch{caught=true;}failCopy=0;
                    if(!caught || copies!=2 || target.Count!=2 || source.Count!=4 || made-dead!=6)return 1;
                    var iterator=source.GetEnumerator();while(iterator.MoveNext())if(iterator.Current.Token.Length!=5)return 2;
                    target.UnionWith(&source);if(target.Count!=5 || source.Count!=4 || made-dead!=9)return 3;
                }if(made!=dead || allocations!=releases)return 4;
                {HashSet<Key> target=new();HashSet<Key> source=new();target.Add(new Key(0));source.Add(new Key(1));
                    int live=made-dead;copies=0;failHash=hashCalls+1;bool caught=false;try{target.UnionWith(&source);}catch{caught=true;}failHash=0;
                    if(!caught || copies!=0 || target.Count!=1 || source.Count!=1 || made-dead!=live)return 5;
                    copies=0;failHash=hashCalls+2;caught=false;try{target.UnionWith(&source);}catch{caught=true;}failHash=0;
                    if(!caught || copies!=1 || target.Count!=1 || source.Count!=1 || made-dead!=live)return 6;
                    copies=0;failEquality=equalityCalls+1;caught=false;try{target.UnionWith(&source);}catch{caught=true;}failEquality=0;
                    if(!caught || copies!=0 || target.Count!=1 || source.Count!=1 || made-dead!=live)return 7;
                }if(made!=dead || allocations!=releases)return 8;
                {HashSet<Key> target=new();HashSet<Key> source=new();for(int i=0;i<4;i++){target.Add(new Key(i));source.Add(new Key(i+4));}
                    int before=allocations-releases;failLarge=true;bool caught=false;try{target.UnionWith(&source);}catch{caught=true;}failLarge=false;
                    if(!caught || target.Count!=4 || source.Count!=4 || made-dead!=8 || allocations-releases!=before)return 9;
                    target.UnionWith(&source);if(target.Count!=8 || source.Count!=4 || made-dead!=12)return 10;
                    failHash=hashCalls+3;caught=false;try{target.ExceptWith(&source);}catch{caught=true;}failHash=0;
                    if(!caught || target.Count!=6 || source.Count!=4 || made-dead!=10)return 11;
                    target.ExceptWith(&source);if(target.Count!=4 || source.Count!=4 || made-dead!=8)return 12;
                }if(made!=dead || allocations!=releases)return 13;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ManagedKeyFieldsRemainRootedByTheirActualOwningSets(int optimization)
        => Run("""
            using std; using std::Memory; using std::Numerics; using std::Collections;
            class Node{public int Value;public Node^? Next;public Node(int value){Value=value;}}
            struct Key:IHashable,IEquatable<Key>{
                public int Id;public immutable(Node^) Value;
                public Key(int id,Node^ value){Id=id;Value=value;}
                public copy Key(immutable(Key)* source){Id=source.Id;Value=source.Value;}
                public immutable nuint GetHashCode()=>(nuint)Id;
                public immutable bool Equals(immutable(Key)* other)=>Id==other.Id;
            }
            void Fill(HashSet<Key>* target,HashSet<Key>* source){
                Node^ a=new^ Node(0);a.Next=a;target.Add(new Key(0,a));
                Node^ b=new^ Node(1);b.Next=b;target.Add(new Key(1,b));
                Node^ c=new^ Node(11);c.Next=c;source.Add(new Key(1,c));
                Node^ d=new^ Node(2);d.Next=d;source.Add(new Key(2,d));
            }
            int main(){HashSet<Key> target=new();HashSet<Key> source=new();Fill(&target,&source);GC::Collect();
                if(TracingCollector::ManagedCount!=4)return 1;
                target.UnionWith(&source);GC::Collect();if(target.Count!=3 || source.Count!=2 || TracingCollector::ManagedCount!=4)return 2;
                target.ExceptWith(&source);GC::Collect();if(target.Count!=1 || source.Count!=2 || TracingCollector::ManagedCount!=3)return 3;
                source.Clear();GC::Collect();if(TracingCollector::ManagedCount!=1)return 4;
                target.Clear();GC::Collect();return (int)TracingCollector::ManagedCount;
            }
            """,optimization,managed:true);

    private static void Run(string program,int optimization,bool managed=false)
    {
        string[] files=["IHashable.gf","IEquatable.gf","IComparable.gf","Hashing/primitive_hashing.gf","Collections/HashMap.gf","Collections/HashSet.gf","Collections/hash_set_algebra.gf","IStringConvertable.gf","Text/String.gf", "Text/ByteSearch.gf"];
        IEnumerable<SourceFile> sources=files.Select(file=>new SourceFile(file,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core",file))));
        if(managed)sources=sources.Concat(new[]{"GC.gf","TracingCollector.gf"}.Select(file=>new SourceFile(file,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/libc",file)))));
        sources=sources.Append(new SourceFile("set-mutations.gf",program));
        string directory=Path.Combine(Path.GetTempPath(),"gflat_set_mutation_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try{
            string ir=Path.Combine(directory,"set.ll"),executable=Path.Combine(directory,"set.exe");File.WriteAllText(ir,Compiler.Emit(sources,options:new(){InternalizeFunctions=true}));
            var compiled=NativeToolchain.Compile(ir,executable,optimizationLevel:optimization);Assert.True(compiled.ExitCode==0,compiled.StandardError);
            var result=NativeToolchain.Run(executable,[]);Assert.True(result.ExitCode==0,$"Set mutation exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }finally{Directory.Delete(directory,true);}
    }
}
