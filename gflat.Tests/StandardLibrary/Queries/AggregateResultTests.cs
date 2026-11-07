namespace gflat.Tests.Std.Queries;

public sealed class AggregateResultTests
{
    private static IEnumerable<SourceFile> Sources(string code) => StandardTestSources.Combine(
        StandardTestSources.Values, StandardTestSources.Load("core/Memory/Unique.gf", "core/Queries/enumerable_aggregate.gf"))
        .Append(new SourceFile("aggregate-result.gf", code));

    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("QUERY_AGGREGATE_RESULT.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FoldAndProjectionMatchLinqForEmptyAndNonemptySequences(int optimization)
    {
        var cases = new List<string>();
        for (int length = 0; length <= 6; length++)
        for (int seed = -3; seed <= 3; seed++)
        {
            long expected = Enumerable.Range(1, length).Aggregate((long)seed,
                (total, next) => total * 3 + next, total => total * 7 - 2);
            cases.Add($"if (Run({length}, {seed}l) != {expected}l) return 1;");
        }
        var result = CompilerTestHelper.Run(Sources($$"""
            using Standard;
            using Standard::Collections;
            using Standard::Memory;
            using Standard::Queries;

            int calls = 0;
            int projections = 0;
            long Fold(long total, int next) { calls++; return total * 3 + (long)next; }
            long Project(long total) { projections++; return total * 7 - 2; }
            long Run(nuint count, long seed)
            {
                int[6] data = [1, 2, 3, 4, 5, 6];
                Span<int> values = new(&data[0], count);
                calls = 0;
                projections = 0;
                long result = values.AsEnumerable().Aggregate(seed, Fold, Project);
                if (calls != (int)count || projections != 1) throw new Exception("callback counts");
                calls = 0;
                projections = 0;
                long other = Aggregate(values.AsEnumerable(), seed, Fold, Project);
                if (other != result || calls != (int)count || projections != 1) throw new Exception("freestanding fold");
                return result;
            }
            int main()
            {
                {{string.Join('\n', cases)}}
                return 0;
            }
            """), optimization);
        Assert.True(result.ExitCode == 0, $"Aggregate projection oracle exit {result.ExitCode}: {result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NoncopyableAccumulatorsAndResultsTransferAndCleanUpOnEveryFailure(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            using Standard;
            using Standard::Memory;
            using Standard::Queries;
            using Standard::Text;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            int live = 0;
            int owners = 0;
            int starts = 0;
            int ends = 0;
            int calls = 0;
            int projections = 0;
            int mode = 0;
            int length = 3;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes)
                {
                    void*? result = malloc(bytes);
                    if (result != null) live++;
                    return result;
                }
                public replace void Free(void* storage) nothrow { live--; free(storage); }
            }
            struct Owner
            {
                public int Sum;
                private String token;
                public Owner(int value) { Sum = value; token = s"owner"; owners++; }
                ~Owner() { owners--; }
            }
            struct Source
            {
                public struct Iterator
                {
                    private int position;
                    private String token;
                    public Iterator() { position = 0; token = s"iterator"; starts++; }
                    public bool MoveNext()
                    {
                        if (mode == 2 && position == 1) throw new Exception("MoveNext", 72);
                        if (position == length) return false;
                        position++;
                        return true;
                    }
                    public int Current
                    {
                        get
                        {
                            if (mode == 3 && position == 2) throw new Exception("Current", 73);
                            return position;
                        }
                    }
                    ~Iterator() { ends++; }
                }
                public Iterator GetEnumerator()
                {
                    if (mode == 1) throw new Exception("GetEnumerator", 71);
                    return new Iterator();
                }
            }
            Owner Fold(Owner seed, int value)
            {
                calls++;
                if (mode == 4 && calls == 2) throw new Exception("accumulate", 74);
                seed.Sum += value;
                return move seed;
            }
            Unique<int> Project(Owner seed)
            {
                projections++;
                if (starts != ends) throw new Exception("iterator still alive", 99);
                Unique<int> result = new(new* int(seed.Sum));
                if (mode == 5) throw new Exception("projection", 75);
                return move result;
            }
            int main()
            {
                var query = new Enumerable<int, Source, Source::Iterator>(new Source());
                {
                    Owner seed = new(10);
                    Unique<int> result = query.Aggregate(move seed, Fold, Project);
                    if (result.Value != 16 || owners != 0 || calls != 3 || projections != 1 || starts != ends) return 1;
                }
                if (live != 0) return 2;
                length = 0;
                calls = 0;
                projections = 0;
                {
                    Unique<int> result = Aggregate<int, Owner, Unique<int>, Source, Source::Iterator>(query, new Owner(37), Fold, Project);
                    if (result.Value != 37 || owners != 0 || calls != 0 || projections != 1) return 3;
                }
                {
                    Owner result = Aggregate<int, Owner, Source, Source::Iterator>(query, new Owner(42), Fold);
                    if (result.Sum != 42 || owners != 1) return 4;
                }
                if (live != 0 || owners != 0) return 5;
                length = 3;
                for (int failure = 1; failure <= 5; failure++)
                {
                    mode = failure;
                    calls = 0;
                    projections = 0;
                    bool caught = false;
                    try { query.Aggregate(new Owner(10), Fold, Project); }
                    catch (ref Exception error)
                    {
                        if (error.GetCode() != 70 + failure) return 6;
                        caught = true;
                    }
                    if (!caught || owners != 0 || live != 0 || starts != ends) return 7;
                    int expectedProjections = 0;
                    if (failure == 5) expectedProjections = 1;
                    if (projections != expectedProjections) return 8;
                }
                return 0;
            }
            """), optimization);
        Assert.True(result.ExitCode == 0, $"Aggregate owning projection exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
