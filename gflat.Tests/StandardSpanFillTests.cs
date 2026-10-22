using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class StandardSpanFillTests
{
    private static IEnumerable<SourceFile> Sources(string program, bool managed = false)
    {
        string[] files = ["core/Collections/IEnumerable.gf", "core/Collections/Enumerable.gf", "core/Collections/SpanFill.gf",
            "core/Span.gf", "core/Vector.gf", "core/IStringConvertable.gf", "core/IHashable.gf", "core/IEquatable.gf",
            "core/IComparable.gf", "core/Hashing.gf", "core/String.gf", "core/ByteSearch.gf"];
        return files.Concat(managed ? ["libc/GC.gf", "libc/TracingCollector.gf"] : Array.Empty<string>())
            .Select(file => new SourceFile(file, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", file))))
            .Append(new SourceFile("span-fill.gf", program));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PrimitiveFillChangesOnlySelectedInitializedElementsAndEmptyViewsAreSafe(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std;
            int main(){int[7] buffer=[0,1,2,3,4,5,6];Span<int> values=new((int*)buffer,7);
                values.Slice(2,3).Fill(42);
                for(nuint i=0;i<7;i++){int expected=(int)i;if(i>=2 && i<5)expected=42;if(buffer[i]!=expected)return 1;}
                Span<int> empty=new();empty.Fill(99);values.Slice(7).Fill(99);
                values.Fill(-1);for(nuint i=0;i<7;i++)if(buffer[i]!=-1)return 2;
                return 0;}
            """), optimization);
        Assert.Equal(0, result.ExitCode);
    }

    private const string Owners = """
        using std;
        int made=0;int dead=0;int copies=0;int budget=-1;
        struct Owner{
            public int Id;public String Text;
            public Owner(int id){Id=id;Text=s"owned";made++;}
            public copy Owner(readonly(Owner)* source){
                copies++;Id=source.Id;Text=source.Text;
                if(budget==0)throw new Exception("copy failed");
                if(budget>0)budget--;made++;
            }
            ~Owner(){dead++;}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AliasedValueIsSnapshottedAndEveryAssignmentOwnsAnIndependentCopy(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources(Owners + """
            int main(){
                {
                    Vector<Owner> values=new();for(int i=0;i<3;i++)values.Add(new Owner(i));
                    values.AsSpan().Fill(values[1]);
                    if(copies!=4 || made!=7 || dead!=4)return 1;
                    for(nuint i=0;i<3;i++)if(values[i].Id!=1 || values[i].Text.Length!=5)return 2;
                    values[0].Text[0]='X';
                    if(values[1].Text.Data[0]!='o' || values[2].Text.Data[0]!='o')return 3;
                    Span<Owner> empty=new();empty.Fill(values[1]);
                    if(copies!=5 || made!=8 || dead!=5)return 4;
                }
                return made-dead;
            }
            """), optimization);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ThrowingParameterAndElementCopiesPreserveUnchangedOwnersAndCompletedPrefix(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources(Owners + """
            int main(){
                {
                    Vector<Owner> values=new();for(int i=0;i<4;i++)values.Add(new Owner(i));
                    Owner source=new Owner(9);int caught=0;
                    budget=0;try{values.AsSpan().Fill(source);}catch{caught++;}
                    if(caught!=1 || copies!=1 || made!=5 || dead!=0)return 1;
                    for(nuint i=0;i<4;i++)if(values[i].Id!=(int)i)return 2;
                    Span<Owner> empty=new();try{empty.Fill(source);}catch{caught++;}
                    if(caught!=2 || copies!=2 || made!=5 || dead!=0)return 3;
                    budget=3;try{values.AsSpan().Fill(source);}catch{caught++;}
                    if(caught!=3 || copies!=6 || made!=8 || dead!=3)return 4;
                    if(values[0].Id!=9 || values[1].Id!=9 || values[2].Id!=2 || values[3].Id!=3 || source.Id!=9)return 5;
                    if(values[0].Text.Data==values[1].Text.Data || values[0].Text.Data==source.Text.Data)return 6;
                }
                return made-dead;
            }
            """), optimization);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ManagedSnapshotSurvivesCollectionInReplacedOwnerDestructors(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std;
            class Node{public int Id;public Node^? Next;public Node(int id){Id=id;}}
            int dead=0;bool bad=false;
            struct Owner{
                public readonly(Node^) Root;
                public Owner(int id){Node^ node=new^ Node(id);node.Next=node;Root=node;}
                public copy Owner(readonly(Owner)* source){Root=source.Root;}
                ~Owner(){GC::Collect();if(Root.Id<0)bad=true;dead++;}
            }
            void Exercise(){
                Vector<Owner> values=new();for(int i=0;i<4;i++)values.Add(new Owner(i));
                Owner source=new Owner(9);values.AsSpan().Fill(move source);
                GC::Collect();if(TracingCollector::ManagedCount!=1 || dead!=5)bad=true;
                for(nuint i=0;i<4;i++)if(values[i].Root.Id!=9)bad=true;
            }
            int main(){Exercise();GC::Collect();if(bad || dead!=9)return 1;return (int)TracingCollector::ManagedCount;}
            """, managed: true), optimization);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void FillRejectsReadonlyElementsAndNoncopyableOwnersOnlyWhenCalled()
    {
        Assert.Throws<TypeCheckException>(() => Compiler.Check(Sources("""
            using std;int main(){int[1] value=[1];Span<readonly(int)> span=new((readonly(int)*)value,1);span.Fill(2);return 0;}
            """)));
        const string owner = "using std;struct Owner{public int Id;~Owner(){}}";
        Compiler.Check(Sources(owner + "int main(){Span<Owner> span=new();return 0;}"));
        Assert.Throws<TypeCheckException>(() => Compiler.Check(Sources(owner + "int main(){Span<Owner> span=new();span.Fill(new Owner());return 0;}")));
    }
}
