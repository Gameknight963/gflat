namespace gflat.Tests;

public sealed class StandardTryQueryTests
{
    private const string Prelude = """
        using std; using std::Text; using std::Memory; using std::Queries; using std::Collections;
        private extern void*? malloc(nuint bytes); private extern void free(void* storage);
        int allocations=0;int releases=0;
        namespace Memory {
            public replace void*? Allocate(nuint bytes){void*? result=malloc(bytes);if(result!=null)allocations++;return result;}
            public replace void Free(void* storage) nothrow{releases++;free(storage);}
        }
        """;

    private const string OwningSource = """
        int created=0;int destroyed=0;int moves=0;int reads=0;int failMove=0;int failRead=0;int calls=0;
        struct Source {
            private int count;
            public Source(int size){count=size;}
            public struct Iterator {
                private String token;private int count;private int index;
                public Iterator(int size){token=s"iterator";count=size;index=0;created++;}
                public bool MoveNext(){moves++;if(moves==failMove)throw new Exception("advance");if(index==count)return false;index++;return true;}
                public String Current{get{
                    String local=s"getter temporary";reads++;if(reads==failRead)throw new Exception("read");
                    if(index==1)return s"a";if(index==2)return s"bb";return s"ccc";
                }}
                ~Iterator(){destroyed++;}
            }
            public Source::Iterator GetEnumerator()=>new Source::Iterator(count);
        }
        bool Fail(String value){calls++;if(calls==2)throw new Exception("predicate");return true;}
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EmptyMultipleAndSingleBoundariesHaveExactReadsAndPreserveOutput(int optimization)
        => Run(Prelude + OwningSource + """
            int main(){{
                var empty=new Enumerable<String,Source,Source::Iterator>(new Source(0));
                var one=new Enumerable<String,Source,Source::Iterator>(new Source(1));
                var many=new Enumerable<String,Source,Source::Iterator>(new Source(3));
                String output=s"unchanged";readonly(char)* original=output.Data;
                moves=0;reads=0;if(empty.TryLast(ref output) || output.Data!=original || moves!=1 || reads!=0)return 1;
                moves=0;reads=0;if(empty.TrySingle(ref output) || output.Data!=original || moves!=1 || reads!=0)return 2;
                moves=0;reads=0;if(many.TrySingle(ref output) || output.Data!=original || moves!=2 || reads!=1)return 3;
                moves=0;reads=0;if(!one.TrySingle(ref output) || output.Length!=1 || output[0]!='a' || moves!=2 || reads!=1)return 4;
                moves=0;reads=0;if(!many.TryLast(ref output) || output.Length!=3 || output[0]!='c' || moves!=4 || reads!=3)return 5;
                if(created!=5 || destroyed!=5 || allocations-releases!=1)return 6;
            }return allocations-releases;}
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AdvanceGetterAndPredicateFailuresLeaveOutputUntouchedAndDisposeIterators(int optimization)
        => Run(Prelude + OwningSource + """
            int main(){{
                var query=new Enumerable<String,Source,Source::Iterator>(new Source(3));
                String output=s"unchanged";readonly(char)* original=output.Data;int baseline=allocations-releases;
                for(int mode=0;mode<4;mode++){
                    moves=0;reads=0;failMove=0;failRead=0;
                    if(mode==0)failMove=2;if(mode==1)failMove=4;if(mode==2)failRead=1;if(mode==3)failRead=2;
                    bool caught=false;try{query.TryLast(ref output);}catch{caught=true;}
                    if(!caught || output.Data!=original || created!=destroyed || allocations-releases!=baseline)return 1;
                }
                for(int mode=0;mode<2;mode++){
                    moves=0;reads=0;failMove=0;failRead=0;if(mode==0)failMove=2;else failRead=1;
                    bool caught=false;try{query.TrySingle(ref output);}catch{caught=true;}
                    if(!caught || output.Data!=original || created!=destroyed || allocations-releases!=baseline)return 2;
                }
                failMove=0;failRead=0;calls=0;bool caught=false;
                try{query.Where(Fail).TryLast(ref output);}catch{caught=true;}
                if(!caught || calls!=2 || output.Data!=original || created!=destroyed || allocations-releases!=baseline)return 3;
                calls=0;caught=false;try{query.Where(Fail).TrySingle(ref output);}catch{caught=true;}
                if(!caught || calls!=2 || output.Data!=original || created!=destroyed || allocations-releases!=baseline)return 4;
            }return allocations-releases;}
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ThrowingCopiesAndSourceAliasingKeepOrdinaryOwningValueSemantics(int optimization)
        => Run(Prelude + """
            int live=0;int copies=0;int failAt=0;
            struct Item{
                public String Text;
                public Item(String text){Text=move text;live++;}
                public copy Item(immutable(Item)* source){copies++;Text=source.Text;if(copies==failAt)throw new Exception("copy");live++;}
                ~Item(){live--;}
            }
            int main(){{
                Vector<Item> values=new();values.Add(new Item(s"a"));values.Add(new Item(s"bb"));values.Add(new Item(s"ccc"));
                var query=values.AsEnumerable();Item output=new Item(s"unchanged");readonly(char)* original=output.Text.Data;
                int baseline=allocations-releases;copies=0;failAt=2;bool caught=false;
                try{query.TryLast(ref output);}catch{caught=true;}
                if(!caught || copies!=2 || output.Text.Data!=original || live!=4 || allocations-releases!=baseline)return 1;
                copies=0;failAt=1;caught=false;try{query.Take(1).TrySingle(ref output);}catch{caught=true;}
                if(!caught || copies!=1 || output.Text.Data!=original || live!=4 || allocations-releases!=baseline)return 2;
                copies=0;failAt=2;if(query.TrySingle(ref output) || copies!=1 || output.Text.Data!=original || live!=4)return 3;
                failAt=0;copies=0;if(!query.TryLast(ref output) || copies!=3 || output.Text.Length!=3 || output.Text.Data==values[2].Text.Data)return 4;
                if(!query.Take(1).TrySingle(ref values[0]) || values[0].Text.Length!=1 || live!=4)return 5;
                if(!query.TryLast(ref values[0]) || values[0].Text.Length!=3 || values[0].Text.Data==values[2].Text.Data || live!=4)return 6;
            }if(live!=0 || allocations!=releases)return 7;return 0;}
            """, optimization);

    private static void Run(string program,int optimization)
    {
        string[] files=["Collections/IEnumerable.gf","Collections/IEnumerator.gf","Queries/Enumerable.gf","Queries/enumerable_predicates.gf", "Queries/enumerable_count.gf", "Queries/enumerable_elements.gf", "Queries/enumerable_for_each.gf", "Queries/enumerable_to_vector.gf", "Queries/enumerable_select.gf", "Queries/enumerable_aggregate.gf", "Queries/enumerable_where.gf", "Queries/enumerable_slice.gf", "Queries/enumerable_while.gf",
            "Queries/WhereSource.gf","Queries/SelectSource.gf","Queries/SliceSource.gf","Queries/TakeWhileSource.gf","Queries/SkipWhileSource.gf",
            "Collections/Vector.gf","Memory/Span.gf","IStringConvertable.gf","IHashable.gf","IEquatable.gf","IComparable.gf","Hashing/primitive_hashing.gf","Text/String.gf", "Text/ByteSearch.gf"];
        var sources=files.Select(file=>new SourceFile(file,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core",file))))
            .Append(new SourceFile("try.gf",program));
        string directory=Path.Combine(Path.GetTempPath(),"gflat_try_query_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try{
            string ir=Path.Combine(directory,"try.ll"),executable=Path.Combine(directory,"try.exe");
            File.WriteAllText(ir,Compiler.Emit(sources,options:new(){InternalizeFunctions=true}));
            var compiled=NativeToolchain.Compile(ir,executable,optimizationLevel:optimization);Assert.True(compiled.ExitCode==0,compiled.StandardError);
            var result=NativeToolchain.Run(executable,[]);Assert.True(result.ExitCode==0,$"Try query exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }finally{Directory.Delete(directory,true);}
    }
}
