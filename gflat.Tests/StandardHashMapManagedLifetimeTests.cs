namespace gflat.Tests;

public class StandardHashMapManagedLifetimeTests
{
    [Fact]
    public void GrowthAndTombstoneRemovalKeepOnlyLiveManagedValuesReachable()
    {
        string[] files=["core/IHashable.gf","core/IEquatable.gf","core/Hashing.gf","core/Collections/HashMap.gf","libc/GC.gf","libc/TracingCollector.gf"];
        var sources=files.Select(file=>new SourceFile("std/"+file,
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std",file))));
        var result=CompilerTestHelper.Run(sources.Append(new SourceFile("main.gf","""
            using std; using std::Collections;
            class Node { public int Value; public Node^? Next; public Node(int n) { Value=n; } }
            void Fill(HashMap<int,Node^>* map) {
                for(int i=0;i<32;i++) {
                    Node^ value=new^ Node(i); value.Next=value; map.Add(i,value);
                    GC::Collect();
                }
            }
            int main() {
                HashMap<int,Node^> map=new(); Fill(&map); map.Reserve(1000); GC::Collect();
                if(TracingCollector::ManagedCount!=32) return 1;
                for(int i=0;i<32;i++) if(map[&i].Value!=i) return 2;
                for(int i=0;i<16;i++) map.Remove(&i);
                GC::Collect(); if(TracingCollector::ManagedCount!=16) return 3;
                for(int i=16;i<32;i++) if(map[&i].Value!=i) return 4;
                map.Clear(); GC::Collect();
                return (int)TracingCollector::ManagedCount;
            }
            """)));
        Assert.Equal(0,result.ExitCode);
    }
}
