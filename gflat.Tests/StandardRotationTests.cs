using System.Text;
using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class StandardRotationTests
{
    private static IEnumerable<SourceFile> Sources(string program) => new[] {
        "Collections/IEnumerable.gf", "Queries/Enumerable.gf", "Collections/Algorithms.gf", "Span.gf", "Collections/Vector.gf"
    }.Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", name))))
        .Append(new SourceFile("main.gf", program));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EverySmallSplitMatchesIndependentConcatenationAndPreservesGuards(int optimization)
    {
        var checks=new StringBuilder();
        for (int size=0; size<=17; size++)
            for (int middle=0; middle<=size; middle++) {
                int[] original=Enumerable.Range(0,size).Select(i => i*31-size).ToArray();
                int[] expected=original.Skip(middle).Concat(original.Take(middle)).ToArray();
                checks.AppendLine($$"""
                    {
                        int[{{size+2}}] buffer=[{{string.Join(",", new[]{-111}.Concat(original).Append(-222))}}];
                        Span<int> view=new(&buffer[1],{{size}});
                        Rotate<int>(view,{{middle}});
                        if(buffer[0]!=-111 || buffer[{{size+1}}]!=-222)return 1;
                        {{string.Join("\n",expected.Select((value,i)=>$"if(buffer[{i+1}]!={value})return 2;"))}}
                    }
                    """);
            }
        var result=CompilerTestHelper.Run(Sources($$"""
            using std; using std::Queries; using std::Collections;
            int main(){
                Span<int> empty=new();Rotate<int>(empty,0);
                {{checks}}
                int[3] buffer=[4,5,6];Span<int> view=new(buffer,3);
                bool caught=false;try{Rotate<int>(view,4);}catch(Exception* error){caught=true;}
                if(!caught || buffer[0]!=4 || buffer[1]!=5 || buffer[2]!=6)return 3;
                caught=false;try{Rotate<int>(empty,1);}catch(Exception* error){caught=true;}
                if(!caught)return 4;
                return 0;
            }
            """),optimization);
        Assert.Equal(0,result.ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RotationRelocatesNoncopyableOwnersWithoutAllocationsOrDestructors(int optimization)
    {
        var result=CompilerTestHelper.Run(Sources("""
            using std; using std::Queries; using std::Collections;
            extern void* malloc(nuint size);extern void free(void* storage);
            int allocations=0;
            replace void*? Memory::Allocate(nuint bytes){allocations++;return malloc(bytes);}
            replace void Memory::Free(void* storage) nothrow{free(storage);}
            struct Item{
                private int* destroyed;public int Key;
                public Item(int* counts,int key){destroyed=counts;Key=key;}
                ~Item(){destroyed[Key]++;}
            }
            int main(){
                int[31] destroyed;for(int i=0;i<31;i++)destroyed[i]=0;
                {
                    Vector<Item> values=new();for(int i=0;i<31;i++)values.Add(new Item(destroyed,i));
                    Span<Item> span=values.AsSpan();Item* original=span.Data!;int before=allocations;
                    for(nuint middle=0;middle<=31;middle++){
                        Rotate<Item>(values.AsSpan(),middle);
                        for(nuint i=0;i<31;i++)if(values[i].Key!=(int)((i+middle)%31))return 1;
                        Rotate<Item>(values.AsSpan(),31-middle);
                        for(nuint i=0;i<31;i++)if(values[i].Key!=(int)i)return 2;
                    }
                    if(allocations!=before || span.Data!=original || &values[0]!=original)return 3;
                    for(int i=0;i<31;i++)if(destroyed[i]!=0)return 4;
                }
                for(int i=0;i<31;i++)if(destroyed[i]!=1)return 5;
                return 0;
            }
            """),optimization);
        Assert.Equal(0,result.ExitCode);
    }

    [Fact]
    public void ReadonlyElementsCannotBeRotated()
        => Assert.Throws<TypeCheckException>(()=>Compiler.Emit(Sources("""
            using std; using std::Queries; using std::Collections;
            int main(){int[2] values=[1,2];Span<readonly(int)> view=new(values,2);Rotate<readonly(int)>(view,1);return 0;}
            """)));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ManagedElementRootsFollowTheirNewSlotsAndAreReleased(int optimization)
    {
        var sources=Sources("""
            using std; using std::Queries; using std::Collections;
            class Node{public int Value;public Node^? Next;public Node(int value){Value=value;Next=null;}}
            void Check(){
                Vector<Node^> values=new();
                for(int i=0;i<31;i++){
                    Node^ node=new^ Node(i);node.Next=node;values.Add(node);
                }
                Rotate<Node^>(values.AsSpan(),7);GC::Collect();
                if(TracingCollector::ManagedCount!=31)throw new Exception("premature collection");
                for(nuint i=0;i<31;i++)if(values[i].Value!=(int)((i+7)%31))throw new Exception("wrong order");
                values.Clear();
                if(GC::Collect()!=31)throw new Exception("rotated roots survived");
            }
            int main(){Check();GC::Collect();return (int)TracingCollector::ManagedCount;}
            """).Concat(new[]{"GC.gf","TracingCollector.gf"}.Select(name=>new SourceFile("std/libc/"+name,
                File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/libc",name)))));
        Assert.Equal(0,CompilerTestHelper.Run(sources,optimization).ExitCode);
    }
}
