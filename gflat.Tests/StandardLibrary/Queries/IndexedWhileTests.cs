using System.Text;

namespace gflat.Tests.Std.Queries;

public sealed class IndexedWhileTests
{
    [Fact]
    public void GuideCompiles() => StandardLibraryGuideTests.CheckProgram("QUERY_INDEXED_WHILE.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void IndexedBoundariesMatchLinqAndStopWithoutExtraReads(int optimization)
    {
        var code = new StringBuilder("""
            using Standard;
            using Standard::Collections;
            using Standard::Queries;

            int moves = 0;
            int reads = 0;
            int calls = 0;
            int mode = 0;
            bool bad = false;
            struct Source
            {
                private int count;
                public Source(int size) { count = size; }
                public struct Cursor
                {
                    private int count;
                    private int index;
                    public Cursor(int size) { count = size; index = 0; }
                    public bool MoveNext() { moves++; if (index == count) return false; index++; return true; }
                    public int Current { get { reads++; return (index - 1) * 3 - 5; } }
                }
                public Cursor GetEnumerator() => new Cursor(count);
            }
            bool Keep(int value, nuint index)
            {
                if (index != (nuint)calls || value != (int)index * 3 - 5) bad = true;
                calls++;
                if (mode == 0) return index < 3;
                if (mode == 1) return (value + (int)index * 2) % 7 != 0;
                return mode == 2;
            }
            int main()
            {
            """);
        for (int size = 0; size <= 9; size++)
        for (int mode = 0; mode < 4; mode++)
        foreach (bool skip in new[] { false, true })
        {
            Func<int, int, bool> predicate = (value, index) => mode == 0 ? index < 3 : mode == 1 ? (value + index * 2) % 7 != 0 : mode == 2;
            int[] input = Enumerable.Range(0, size).Select(i => i * 3 - 5).ToArray();
            int[] expected = (skip ? input.SkipWhile(predicate) : input.TakeWhile(predicate)).ToArray();
            int boundary = input.TakeWhile(predicate).Count();
            int calls = boundary == size ? size : boundary + 1;
            int moves = skip || boundary == size ? size + 1 : boundary + 1;
            string method = skip ? "SkipWhile" : "TakeWhile";
            code.AppendLine($$"""
                {
                    mode = {{mode}};
                    moves = 0; reads = 0; calls = 0; bad = false;
                    var query = new Enumerable<int, Source, Source::Cursor>(new Source({{size}}));
                    var filtered = query.{{method}}(Keep);
                    if (moves != 0 || reads != 0 || calls != 0) return 1;
                    for (int pass = 0; pass < 2; pass++)
                    {
                        moves = 0; reads = 0; calls = 0;
                        var cursor = filtered.GetEnumerator();
                        bool invalid = false;
                        try { cursor.Current; } catch { invalid = true; }
                        if (!invalid) return 2;
                        int[{{Math.Max(1, expected.Length)}}] expected = [{{string.Join(",", expected.Length == 0 ? [0] : expected)}}];
                        int seen = 0;
                        while (cursor.MoveNext())
                        {
                            if (seen >= {{expected.Length}} || cursor.Current != expected[seen] || cursor.Current != expected[seen]) return 3;
                            seen++;
                        }
                        if (seen != {{expected.Length}} || calls != {{calls}} || moves != {{moves}} || bad) return 4;
                        if (reads != {{calls + expected.Length * 2}}) return 5;
                        int before = reads;
                        if (cursor.MoveNext() || cursor.MoveNext()) return 6;
                        invalid = false;
                        try { cursor.Current; } catch { invalid = true; }
                        if (!invalid || reads != before || moves != {{moves}} || calls != {{calls}}) return 7;
                    }
                    calls = 0;
                    var free = Standard::Queries::{{method}}<int, Source, Source::Cursor>(query, Keep);
                    if (free.Count() != {{expected.Length}} || calls != {{calls}}) return 8;
                }
                """);
        }
        code.AppendLine("return 0; }");
        Run(code.ToString(), optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OwningPredicatesAndIteratorsCleanUpOnShortCircuitAndThrow(int optimization)
        => Run("""
            using Standard;
            using Standard::Text;
            using Standard::Memory;
            using Standard::Queries;
            using Standard::Collections;

            private extern "C" void*? malloc(nuint bytes); private extern "C" void free(void* storage);
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
            bool Short(String value, nuint index){if(index!=(nuint)calls)throw new Exception("index");calls++;if(calls==failAt)throw new Exception("predicate");return value.Length<2;}
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


    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ComposedSequencesHaveIndependentIndicesWithoutAdapterAllocations(int optimization)
        => Run("""
            using Standard;
            using Standard::Collections;
            using Standard::Memory;
            using Standard::Queries;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            bool deny = false;
            int attempts = 0;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes)
                {
                    attempts++;
                    if (deny) return null;
                    return malloc(bytes);
                }
                public replace void Free(void* storage) nothrow => free(storage);
            }
            bool Odd(int value) => value % 2 != 0;
            bool Prefix(int value, nuint index) => index < 2 && value == (int)index * 2 + 1;
            int main()
            {
                int[6] data = [0, 1, 2, 3, 4, 5];
                Span<int> input = new(&data[0], 6);
                var source = input.AsEnumerable().Where(Odd);
                int before = attempts;
                deny = true;
                var take = source.TakeWhile(Prefix);
                var skip = source.SkipWhile(Prefix);
                var left = take.GetEnumerator();
                var right = take.GetEnumerator();
                if (!left.MoveNext() || left.Current != 1 || !left.MoveNext() || left.Current != 3) return 1;
                if (!right.MoveNext() || right.Current != 1 || left.MoveNext()) return 2;
                if (!right.MoveNext() || right.Current != 3 || right.MoveNext()) return 3;
                if (take.Count() != 2 || skip.Count() != 1) return 4;
                var tail = skip.GetEnumerator();
                if (!tail.MoveNext() || tail.Current != 5 || tail.MoveNext()) return 5;
                deny = false;
                if (attempts != before) return 6;
                return 0;
            }
            """, optimization);

    private static void Run(string code, int optimization)
    {
        var sources = StandardTestSources.Combine(StandardTestSources.Values, StandardTestSources.Load(
            "core/Queries/IndexedTakeWhileSource.gf", "core/Queries/IndexedSkipWhileSource.gf",
            "core/Queries/enumerable_indexed_while.gf", "core/Queries/WhereSource.gf", "core/Queries/SelectSource.gf",
            "core/Queries/SliceSource.gf", "core/Queries/enumerable_where.gf", "core/Queries/enumerable_select.gf",
            "core/Queries/enumerable_slice.gf", "core/Queries/enumerable_count.gf", "core/Queries/enumerable_elements.gf",
            "core/Queries/enumerable_to_vector.gf"))
            .Append(new SourceFile("indexed-while.gf", code));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Indexed while exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
