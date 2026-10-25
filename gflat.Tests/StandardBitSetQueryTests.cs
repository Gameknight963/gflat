namespace gflat.Tests;

public sealed class StandardBitSetQueryTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ProtectedQueriesBorrowSetIndicesLazilyAndObserveContentEdits(int optimization)
        => Run("""
            using std;
            extern void*? malloc(nuint size);extern void free(void* pointer);
            int allocations=0;int predicates=0;int selections=0;
            namespace Memory{public replace void*? Allocate(nuint size){allocations++;return malloc(size);}public replace void Free(void* pointer) nothrow{free(pointer);}}
            bool Odd(nuint index){predicates++;return index%2==1;}
            nuint Twice(nuint index){selections++;return index*2;}
            nuint Sum(nuint total,nuint index)=>total+index;
            int main(){
                BitSet bits=new(100);bits[1]=true;bits[2]=true;bits[31]=true;bits[32]=true;bits[63]=true;bits[64]=true;bits[99]=true;
                int before=allocations;
                readonly(BitSet)* shallow=&bits;immutable(BitSet)* deep=&bits;
                var query=deep.AsEnumerable().Where(Odd).Select(Twice).Take(2);
                var other=shallow.AsEnumerable().Where(Odd).Select(Twice).Take(2);
                if(predicates!=0||selections!=0||bits.Length!=100||bits.CountSetBits()!=7)return 1;
                if(query.Aggregate((nuint)0,Sum)!=64||predicates!=3||selections!=2)return 2;
                predicates=0;selections=0;
                if(other.Aggregate((nuint)0,Sum)!=64||predicates!=3||selections!=2)return 3;
                bits[1]=false;bits[3]=true;bits[31]=false;bits[33]=true;
                predicates=0;selections=0;
                if(query.Aggregate((nuint)0,Sum)!=72||predicates!=4||selections!=2)return 4;
                predicates=0;selections=0;
                if(other.Aggregate((nuint)0,Sum)!=72||predicates!=4||selections!=2)return 5;
                predicates=0;selections=0;
                if(query.Take(0).Aggregate((nuint)0,Sum)!=0||predicates!=0||selections!=0)return 6;
                BitSet empty=new();
                if(empty.AsEnumerable().Any()||empty.AsEnumerable().Count()!=0)return 7;
                if(allocations!=before||bits.CountSetBits()!=7)return 8;
                return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NestedViewCapturesBorrowedMetadataAndRejectsNonemptyNullStorage(int optimization)
        => Run("""
            using std;
            nuint Sum(nuint total,nuint value)=>total+value;
            int main(){
                uint[2] words=[(uint)2,(uint)1];
                BitSet::View view=new(words,33);
                Enumerable<nuint,BitSet::View,BitSet::Enumerator> captured=new(view);
                view=new(null,0);
                if(view.GetEnumerator().MoveNext())return 1;
                if(captured.Aggregate((nuint)0,Sum)!=33)return 2;
                words[0]=(uint)8;
                if(captured.Aggregate((nuint)0,Sum)!=35)return 3;
                BitSet::View borrowed=captured.SourceView();immutable(BitSet::View)* deep=&borrowed;
                BitSet::Enumerator iterator=deep.GetEnumerator();
                if(!iterator.MoveNext()||iterator.Current!=3)return 4;
                bool caught=false;
                try{BitSet::View invalid=new(null,1);}catch(ref Exception error){caught=true;}
                if(!caught)return 5;
                return 0;
            }
            """,optimization);

    private static void Run(string code,int optimization)
    {
        string[] names=["Collections/BitSet.gf", "IEquatable.gf", "IHashable.gf", "Hashing.gf","Span.gf","Collections/Vector.gf","Collections/IEnumerable.gf","Collections/IEnumerator.gf","Collections/Enumerable.gf",
            "Collections/EnumerableExtensions.gf","Collections/WhereSource.gf","Collections/SelectSource.gf","Collections/SliceSource.gf","Collections/TakeWhileSource.gf","Collections/SkipWhileSource.gf"];
        var sources=names.Select(name=>new SourceFile(name,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core",name))))
            .Append(new SourceFile("bit-queries.gf",code));
        var result=CompilerTestHelper.Run(sources,optimization);
        Assert.True(result.ExitCode==0,$"Exit {result.ExitCode}: {result.StandardError}");
    }
}
