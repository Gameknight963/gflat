using System.Text;

namespace gflat.Tests;

public sealed class StandardWhileQueryTests
{
    private const string CounterSource = """
        using std; using std::Numerics; using std::Queries; using std::Collections;
        int moves = 0; int reads = 0; int calls = 0;
        struct Source {
            private int count;
            public Source(int size) { count = size; }
            public struct Iterator {
                private int count; private int index;
                public Iterator(int size) { count = size; index = 0; }
                public bool MoveNext() { moves++; if (index == count) return false; index++; return true; }
                public int Current { get { reads++; return index-1; } }
            }
            public Source::Iterator GetEnumerator() => new Source::Iterator(count);
        }
        bool LessThree(int value) { calls++; return value < 3; }
        bool Always(int value) { calls++; return true; }
        bool Never(int value) { calls++; return false; }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PredicateBoundariesMatchLinqAndFinishedIteratorsNeverAdvanceAgain(int optimization)
    {
        var source = new StringBuilder(CounterSource).AppendLine("int main() {");
        foreach (int count in new[] { 0, 1, 3, 4, 7 })
        foreach (string predicate in new[] { "LessThree", "Always", "Never" })
        foreach (bool skip in new[] { false, true })
        {
            Func<int, bool> test = predicate == "LessThree" ? value => value < 3 : predicate == "Always" ? _ => true : _ => false;
            int[] input = Enumerable.Range(0, count).ToArray();
            int[] expected = (skip ? input.SkipWhile(test) : input.TakeWhile(test)).ToArray();
            int boundary = input.TakeWhile(test).Count();
            int predicateCalls = boundary == count ? count : boundary + 1;
            int moveCalls = skip || boundary == count ? count + 1 : boundary + 1;
            source.AppendLine($"{{ moves=0; reads=0; calls=0; var query=new Enumerable<int,Source,Source::Iterator>(new Source({count}));");
            source.AppendLine($"var result=query.{(skip ? "SkipWhile" : "TakeWhile")}({predicate}); if(moves!=0 || reads!=0 || calls!=0) return 1;");
            source.AppendLine("var iterator=result.GetEnumerator(); int seen=0; int invalid=0; try { iterator.Current; } catch { invalid++; }");
            source.AppendLine($"int[{Math.Max(1, expected.Length)}] expected=[" + string.Join(",", expected.Length == 0 ? [0] : expected) + "];");
            source.AppendLine($"while(iterator.MoveNext()) {{ if(seen>={expected.Length} || iterator.Current!=expected[seen])return 2; seen++; }}");
            source.AppendLine($"if(seen!={expected.Length} || calls!={predicateCalls} || moves!={moveCalls})return 3;");
            source.AppendLine("int priorReads=reads; if(iterator.MoveNext() || iterator.MoveNext())return 4; try { iterator.Current; } catch { invalid++; }");
            source.AppendLine($"if(invalid!=2 || reads!=priorReads || calls!={predicateCalls} || moves!={moveCalls})return 5; }}");
        }
        source.AppendLine("return 0; }");
        Run(source.ToString(), optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CompositionReuseAndUncachedCurrentKeepExistingQuerySemantics(int optimization)
        => Run(CounterSource + """
            int projects=0;
            int Project(int value) { projects++; return value+1; }
            bool Even(int value) => value%2==0;
            int Double(int value) => value*2;
            bool LessFour(int value) => value<4;
            bool LessTwelve(int value) => value<12;
            int main() {
                var source=new Enumerable<int,Source,Source::Iterator>(new Source(5));
                var take=source.Select(Project).TakeWhile(LessThree); var iterator=take.GetEnumerator();
                if(!iterator.MoveNext() || projects!=1 || calls!=1)return 1;
                int a=iterator.Current; int b=iterator.Current;
                if(a!=1 || b!=1 || projects!=3 || calls!=1)return 2;
                var skip=source.Select(Project).SkipWhile(LessThree); var skipped=skip.GetEnumerator();
                if(!skipped.MoveNext() || projects!=6 || calls!=4)return 3;
                if(skipped.Current!=3 || skipped.Current!=3 || projects!=8 || calls!=4)return 4;
                if(!skipped.MoveNext() || projects!=8 || calls!=4)return 5;
                Vector<int> values=new();for(int i=0;i<8;i++)values.Add(i);
                var query=values.AsEnumerable().Where(Even).Select(Double).SkipWhile(LessFour).TakeWhile(LessTwelve);
                Vector<int> first=query.ToVector(); Vector<int> again=query.ToVector();
                if(first.Count!=2 || first[0]!=4 || first[1]!=8 || again.Count!=2 || again[0]!=4)return 6;
                values[2]=3; Vector<int> changed=query.ToVector();
                if(changed.Count!=1 || changed[0]!=8)return 7;
                int beforeCalls=calls; int beforeProjects=projects; int beforeMoves=moves;
                if(take.Take(0).Any() || skip.Take(0).Any() || calls!=beforeCalls || projects!=beforeProjects || moves!=beforeMoves)return 8;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OwningPredicatesAndIteratorsCleanUpOnShortCircuitAndThrow(int optimization)
        => Run("""
            using std; using std::Numerics; using std::Queries; using std::Collections;
            private extern void*? malloc(nuint bytes); private extern void free(void* storage);
            int allocations=0;int releases=0;int created=0;int destroyed=0;int calls=0;int failAt=0;
            namespace Memory {
                public replace void*? Allocate(nuint bytes) { void*? result=malloc(bytes);if(result!=null)allocations++;return result; }
                public replace void Free(void* storage) nothrow { releases++;free(storage); }
            }
            struct Source {
                public struct Iterator {
                    private String token;private int index;
                    public Iterator() {token=s"iterator";index=0;created++;}
                    public bool MoveNext() {if(index==3)return false;index++;return true;}
                    public String Current {get {if(index==1)return s"a";if(index==2)return s"bb";return s"ccc";}}
                    ~Iterator(){destroyed++;}
                }
                public Source::Iterator GetEnumerator()=>new Source::Iterator();
            }
            bool Short(String value){calls++;if(calls==failAt)throw new Exception("predicate");return value.Length<2;}
            bool Keep(String value)=>true;
            String Identity(String value)=>move value;
            int main(){
                var query=new Enumerable<String,Source,Source::Iterator>(new Source()).Where(Keep).Select(Identity);
                int baseline=allocations-releases;
                calls=0;query.TakeWhile(Short).ToVector();if(calls!=2 || created!=destroyed || allocations-releases!=baseline)return 1;
                calls=0;query.SkipWhile(Short).ToVector();if(calls!=2 || created!=destroyed || allocations-releases!=baseline)return 2;
                for(int failure=1;failure<=2;failure++){
                    failAt=failure;calls=0;bool caught=false;try{query.TakeWhile(Short).ToVector();}catch{caught=true;}
                    if(!caught || calls!=failure || created!=destroyed || allocations-releases!=baseline)return 3;
                    calls=0;caught=false;try{query.SkipWhile(Short).ToVector();}catch{caught=true;}
                    if(!caught || calls!=failure || created!=destroyed || allocations-releases!=baseline)return 4;
                }
                failAt=0;calls=0;
                {String first=query.TakeWhile(Short).First();if(first.Length!=1 || calls!=1)return 5;}
                calls=0;{String first=query.SkipWhile(Short).First();if(first.Length!=2 || calls!=2)return 6;}
                if(created!=destroyed || allocations!=releases)return 7;
                return 0;
            }
            """, optimization);

    private static void Run(string program, int optimization)
    {
        string[] files=["Collections/IEnumerable.gf","Collections/IEnumerator.gf","Queries/Enumerable.gf","Queries/enumerable_predicates.gf", "Queries/enumerable_count.gf", "Queries/enumerable_elements.gf", "Queries/enumerable_for_each.gf", "Queries/enumerable_to_vector.gf", "Queries/enumerable_select.gf", "Queries/enumerable_aggregate.gf", "Queries/enumerable_where.gf", "Queries/enumerable_slice.gf", "Queries/enumerable_while.gf",
            "Queries/WhereSource.gf","Queries/SelectSource.gf","Queries/SliceSource.gf","Queries/TakeWhileSource.gf","Queries/SkipWhileSource.gf",
            "Collections/Vector.gf","Span.gf","IStringConvertable.gf","IHashable.gf","IEquatable.gf","IComparable.gf","Hashing/primitive_hashing.gf","String.gf", "ByteSearch.gf"];
        var sources=files.Select(file=>new SourceFile(file,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core",file))))
            .Append(new SourceFile("while.gf",program));
        string directory=Path.Combine(Path.GetTempPath(),"gflat_while_query_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try {
            string ir=Path.Combine(directory,"while.ll"),executable=Path.Combine(directory,"while.exe");
            File.WriteAllText(ir,Compiler.Emit(sources,options:new(){InternalizeFunctions=true}));
            var compiled=NativeToolchain.Compile(ir,executable,optimizationLevel:optimization);Assert.True(compiled.ExitCode==0,compiled.StandardError);
            var result=NativeToolchain.Run(executable,[]);Assert.True(result.ExitCode==0,$"While query exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        } finally {Directory.Delete(directory,true);}
    }
}
