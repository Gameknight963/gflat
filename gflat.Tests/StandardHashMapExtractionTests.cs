using System.Text;

namespace gflat.Tests;

public sealed class StandardHashMapExtractionTests
{
    private const string Owners = """
        using std;
        int made=0; int dead=0; int allocations=0; int releases=0;
        bool failHash=false; bool failEquality=false;
        private extern void*? malloc(nuint bytes);
        private extern void free(void* storage);
        namespace Memory {
            public replace void*? Allocate(nuint bytes) { void*? result=malloc(bytes); if(result!=null)allocations++; return result; }
            public replace void Free(void* storage) { releases++; free(storage); }
        }
        struct Key:IHashable,IEquatable<Key> {
            public int Id; public String Text;
            public Key(int id) { Id=id; Text=s"key"; made++; }
            ~Key() { dead++; }
            public readonly nuint GetHashCode() { if(failHash)throw new Exception("hash"); return 1; }
            public readonly bool Equals(readonly(Key)* other) { if(failEquality)throw new Exception("equality"); return Id==other.Id; }
        }
        struct Payload {
            public int Id; public String Text;
            public Payload(int id) { Id=id; Text=s"value"; made++; }
            ~Payload() { dead++; }
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NoncopyableEntryTransfersWithoutAllocationOrDestructionAndOutlivesMap(int optimization)
        => Run(Owners + """
            HashMap<Key,Payload>::Entry Take() {
                HashMap<Key,Payload> map=new();
                map.Add(new Key(7),new Payload(70)); map.Add(new Key(8),new Payload(80));
                var iterator=map.GetEnumerator();
                readonly(Key)*? query=null;
                while(iterator.MoveNext())if(iterator.Current.Key.Id==7)query=iterator.Current.Key;
                int before=allocations; int destroyed=dead;
                HashMap<Key,Payload>::Entry result=map.Extract(query!);
                if(allocations!=before || dead!=destroyed || map.Count!=1)throw new Exception("transfer allocated or destroyed");
                map.Reserve(100); map.Clear();
                return result;
            }
            int main() { {
                HashMap<Key,Payload>::Entry entry=Take();
                if(made!=4 || dead!=2 || entry.Key.Id!=7 || entry.Value.Id!=70)return 1;
                if(entry.Key.Text.Length!=3 || entry.Value.Text.Length!=5)return 2;
                entry.Value.Text.AppendLiteral("!",1);
                if(entry.ValueReadOnly.Text.Length!=6)return 3;
            }
            if(made!=dead || allocations!=releases)return 4; return 0; }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MissingAndThrowingLookupsLeaveOwningEntriesAndCountsUnchanged(int optimization)
        => Run(Owners + """
            int main() { {
                HashMap<Key,Payload> map=new();
                for(int i=0;i<6;i++)map.Add(new Key(i),new Payload(i*10));
                Key query=new Key(3); Key missing=new Key(100); int live=made-dead; int memory=allocations-releases;
                bool caught=false; failHash=true;
                try { var unused=map.Extract(&query); } catch { caught=true; }
                failHash=false; if(!caught || map.Count!=6 || made-dead!=live || allocations-releases!=memory)return 1;
                caught=false; failEquality=true;
                try { var unused=map.Extract(&query); } catch { caught=true; }
                failEquality=false; if(!caught || map.Count!=6 || made-dead!=live || allocations-releases!=memory)return 2;
                caught=false; try { var unused=map.Extract(&missing); } catch { caught=true; }
                if(!caught || map.Count!=6 || made-dead!=live || allocations-releases!=memory)return 3;
                for(int i=0;i<6;i++) { Key key=new Key(i); if(map[&key].Id!=i*10)return 4; }
                var removed=map.Extract(&query); if(map.Count!=5 || removed.Value.Id!=30)return 5;
                caught=false; try { var unused=map.Extract(&query); } catch { caught=true; }
                if(!caught || map.Count!=5)return 6;
                map.Add(new Key(3),new Payload(300)); if(map[&query].Id!=300)return 7;
            }
            if(made!=dead || allocations!=releases)return 8; return 0; }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CollisionTombstonesAndRehashAfterExtractionMatchDictionary(int optimization)
    {
        var random=new Random(140981); var expected=new Dictionary<int,int>();
        var source=new StringBuilder("""
            using std;
            struct Key:IHashable,IEquatable<Key> {
                public int Id; public Key(int id){Id=id;}
                public readonly nuint GetHashCode()=>1;
                public readonly bool Equals(readonly(Key)* other)=>Id==other.Id;
            }
            int main(){HashMap<Key,int> map=new();
            """);
        for(int step=0;step<180;step++)
        {
            int id=random.Next(40), value=random.Next(10000);
            if(expected.TryGetValue(id,out int previous) && random.Next(3)!=0)
            {
                expected.Remove(id);
                source.AppendLine($"{{Key key=new({id}); var entry=map.Extract(&key); if(entry.Key.Id!={id} || entry.Value!={previous})return 1;}}");
            }
            else
            {
                expected[id]=value;
                source.AppendLine($"map.Set(new Key({id}),{value});");
            }
            if(step%19==0)source.AppendLine($"map.Reserve({expected.Count+1});");
            source.AppendLine($"if(map.Count!={expected.Count})return 2;");
            // Check complete contents periodically without expanding thousands of
            // equivalent lookup call sites into one native optimization unit.
            if(step%9==0 || step==179)
                foreach(var pair in expected)
                    source.AppendLine($"{{Key key=new({pair.Key}); int*? found=map.Find(&key); if(found==null || *found! != {pair.Value})return 3;}}");
        }
        source.AppendLine("return 0;}");
        Run(source.ToString(),optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ExtractedManagedFieldsStayRootedAfterMapClearAndReleaseAtScopeExit(int optimization)
        => Run("""
            using std;
            class Node {public int Id;public Node^? Next;public Node(int id){Id=id;}}
            struct Key:IHashable,IEquatable<Key> {
                public int Id; public Node^ Root;
                public Key(int id,Node^ root){Id=id;Root=root;}
                public readonly nuint GetHashCode()=>1;
                public readonly bool Equals(readonly(Key)* other)=>Id==other.Id;
            }
            void Fill(HashMap<Key,Node^>* map) {
                Node^ a=new^ Node(1);a.Next=a;Node^ b=new^ Node(2);b.Next=b;
                Node^ c=new^ Node(3);c.Next=c;Node^ d=new^ Node(4);d.Next=d;
                map.Add(new Key(1,a),b);map.Add(new Key(2,c),d);
            }
            HashMap<Key,Node^>::Entry Take() {
                HashMap<Key,Node^> map=new();Fill(&map);
                var iterator=map.GetEnumerator();readonly(Key)*? query=null;
                while(iterator.MoveNext())if(iterator.Current.Key.Id==1)query=iterator.Current.Key;
                var entry=map.Extract(query!);
                map.Clear();return entry;
            }
            int main(){ {
                var entry=Take();GC::Collect();
                if(TracingCollector::ManagedCount!=2 || entry.Key.Root.Id!=1 || entry.Value.Id!=2)return 1;
                GC::Collect();if(TracingCollector::ManagedCount!=2)return 2;
            }
            GC::Collect();return (int)TracingCollector::ManagedCount;}
            """, optimization,managed:true);

    private static void Run(string program,int optimization,bool managed=false)
    {
        string[] files=["IHashable.gf","IEquatable.gf","IComparable.gf","Hashing.gf","HashMap.gf","IStringConvertable.gf","String.gf"];
        IEnumerable<SourceFile> sources=files.Select(file=>new SourceFile(file,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core",file))));
        if(managed)sources=sources.Concat(new[]{"GC.gf","TracingCollector.gf"}.Select(file=>new SourceFile(file,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/libc",file)))));
        sources=sources.Append(new SourceFile("extraction.gf",program));
        string directory=Path.Combine(Path.GetTempPath(),"gflat_extract_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try {
            string ir=Path.Combine(directory,"extract.ll"),executable=Path.Combine(directory,"extract.exe");
            File.WriteAllText(ir,Compiler.Emit(sources,options:new(){InternalizeFunctions=true}));
            var compilation=NativeToolchain.Compile(ir,executable,optimizationLevel:optimization);Assert.True(compilation.ExitCode==0,compilation.StandardError);
            var result=NativeToolchain.Run(executable,[]);Assert.True(result.ExitCode==0,$"Extraction exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }finally{Directory.Delete(directory,true);}
    }
}
