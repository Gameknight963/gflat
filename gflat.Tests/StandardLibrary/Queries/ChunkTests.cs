using System.Text;

namespace gflat.Tests.Std.Queries;

public sealed class ChunkTests
{
    private static void Run(string code, int optimization)
    {
        var sources = StandardTestSources.Combine(StandardTestSources.Values, StandardTestSources.Load(
            "core/Threading/Interlocked.gf", "core/Memory/SharedStorage.gf", "core/Queries/Group.gf",
            "core/Queries/ChunkSource.gf", "core/Queries/enumerable_chunk.gf",
            "core/Queries/WhereSource.gf", "core/Queries/enumerable_where.gf", "core/Queries/SelectSource.gf",
            "core/Queries/enumerable_select.gf", "core/Queries/enumerable_to_vector.gf"))
            .Append(new SourceFile("chunk-tests.gf", code));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Chunk exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Fact]
    public void GuideCompiles() => StandardLibraryGuideTests.CheckProgram("QUERY_CHUNK.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ChunkCompositionRetainsSnapshotsAndEmptyInputAllocatesNoChunkStorage(int optimization)
        => Run("""
            using Standard;
            using Standard::Collections;
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
            bool Full(Group<int> group) => group.Count == 2;
            int Sum(Group<int> group)
            {
                int sum = 0;
                foreach (int item in group) sum += item;
                return sum;
            }
            int main()
            {
                Vector<int> empty = new();
                int before = attempts;
                deny = true;
                var none = empty.AsEnumerable().Chunk((nuint)0 - 1).GetEnumerator();
                if (none.MoveNext() || attempts != before) return 1;
                deny = false;
                Vector<int> values = new();
                for (int i = 1; i <= 7; i++) values.Add(i);
                var query = values.AsEnumerable().Chunk(2);
                Vector<int> sums = query.Where(Full).Select<int>(Sum).ToVector();
                if (sums.Count != 3 || sums[0] != 3 || sums[1] != 7 || sums[2] != 11) return 2;
                Vector<Group<int>> saved = query.ToVector();
                values.Clear();
                if (saved.Count != 4 || saved[3][0] != 7 || saved[1][1] != 4) return 3;
                Group<int> share = saved[0];
                before = attempts;
                deny = true;
                Group<int> again = share;
                if (&again[0] != &share[0] || again[1] != 2 || attempts != before) return 4;
                deny = false;
                bool caught = false;
                try { again[again.Count]; } catch (Exception* error) { caught = true; }
                if (!caught) return 5;
                caught = false;
                try { again[(nuint)0 - 1]; } catch (Exception* error) { caught = true; }
                if (!caught || again[0] != 1) return 6;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ChunkBoundariesMatchLinqWithoutReadingAheadAndCurrentSharesStableValues(int optimization)
    {
        var program = new StringBuilder("""
            using Standard;
            using Standard::Collections;
            using Standard::Queries;

            int moves = 0;
            int reads = 0;
            int gets = 0;
            struct Source
            {
                private int count;
                public Source(int size) { count = size; }
                public Cursor GetEnumerator() { gets++; return new Cursor(count); }
                public struct Cursor
                {
                    private int count;
                    private int position;
                    public Cursor(int size) { count = size; position = 0; }
                    public bool MoveNext() { moves++; if (position == count) return false; position++; return true; }
                    public int Current { get { reads++; return (position - 1) * 3 - 7; } }
                }
            }
            int main()
            {
            """);
        foreach (int size in new[] { 0, 1, 2, 4, 5, 16, 17 })
        foreach (int limit in new[] { 1, 2, 3, 4, 8, 16, 32, int.MaxValue })
        {
            int[][] expected = Enumerable.Range(0, size).Select(i => i * 3 - 7).Chunk(limit).ToArray();
            string chunkSize = limit == int.MaxValue ? "(nuint)0 - 1" : limit.ToString();
            program.AppendLine($$"""
                {
                    moves = 0; reads = 0; gets = 0;
                    var source = new Enumerable<int, Source, Source::Cursor>(new Source({{size}}));
                    var query = source.Chunk({{chunkSize}});
                    if (moves != 0 || reads != 0 || gets != 0) return 1;
                    Group<int> saved = new();
                    for (int pass = 0; pass < 2; pass++)
                    {
                        moves = 0; reads = 0;
                        var cursor = query.GetEnumerator();
                        if (moves != 0 || reads != 0) return 2;
                """);
            int consumed = 0;
            foreach (int[] chunk in expected)
            {
                consumed += chunk.Length;
                int expectedMoves = consumed + (chunk.Length < limit ? 1 : 0);
                program.AppendLine($$"""
                    {
                        if (!cursor.MoveNext()) return 3;
                        Group<int> group = cursor.Current;
                        Group<int> again = cursor.Current;
                        if (group.Count != {{chunk.Length}} || again.Count != group.Count || &group[0] != &again[0]) return 4;
                        if (moves != {{expectedMoves}} || reads != {{consumed}}) return 5;
                        int[{{chunk.Length}}] expected = [{{string.Join(",", chunk)}}];
                        for (nuint i = 0; i < group.Count; i++) if (group[i] != expected[i]) return 6;
                        if (saved.IsEmpty) saved = group;
                    }
                    """);
            }
            program.AppendLine($$"""
                        if (cursor.MoveNext() || moves != {{size + 1}} || reads != {{size}}) return 7;
                        if (cursor.MoveNext() || moves != {{size + 1}}) return 8;
                        bool invalid = false;
                        try { cursor.Current; } catch (Exception* error) { invalid = true; }
                        if (!invalid) return 9;
                    }
                    if (!saved.IsEmpty && saved[0] != -7) return 10;
                    if (gets != 2) return 11;
                    var free = Standard::Queries::Chunk<int, Source, Source::Cursor>(source, {{chunkSize}});
                    var independent = free.GetEnumerator();
                    if (independent.MoveNext() != {{(size != 0).ToString().ToLowerInvariant()}}) return 12;
                }
                """);
        }
        program.AppendLine("""
                {
                    gets = 0;
                    var source = new Enumerable<int, Source, Source::Cursor>(new Source(5));
                    bool caught = false;
                    try { source.Chunk(0); } catch (Exception* error) { caught = true; }
                    if (!caught || gets != 0) return 13;
                    Group<int> empty = new();
                    caught = false;
                    try { empty[0]; } catch (Exception* error) { caught = true; }
                    if (!caught) return 14;
                }
                return 0;
            }
            """);
        Run(program.ToString(), optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MoveOnlySnapshotsOutliveIteratorsAndEveryAllocationOrSourceFailureCleansUp(int optimization)
        => Run("""
            using Standard;
            using Standard::Collections;
            using Standard::Queries;
            using Standard::Text;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            int live = 0;
            int owners = 0;
            int cursors = 0;
            int attempts = 0;
            int failAt = 0;
            int failMove = 0;
            int failRead = 0;
            int reads = 0;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes)
                {
                    attempts++;
                    if (attempts == failAt) return null;
                    void*? result = malloc(bytes);
                    if (result != null) live++;
                    return result;
                }
                public replace void Free(void* storage) nothrow { live--; free(storage); }
            }
            struct Owner
            {
                public int Id;
                public String Text;
                public Owner(int id) { Id = id; Text = s"owned"; owners++; }
                ~Owner() { owners--; }
            }
            struct Source
            {
                public Cursor GetEnumerator() => new();
                public struct Cursor
                {
                    private int position;
                    private String token;
                    public Cursor() { position = 0; token = s"cursor"; cursors++; }
                    ~Cursor() { cursors--; }
                    public bool MoveNext()
                    {
                        position++;
                        if (position == failMove) throw new Exception("move", 31);
                        return position <= 5;
                    }
                    public Owner Current
                    {
                        get
                        {
                            reads++;
                            if (position == failRead) throw new Exception("read", 32);
                            return new Owner(position);
                        }
                    }
                }
            }
            Group<Owner> Build()
            {
                var query = new Enumerable<Owner, Source, Source::Cursor>(new Source()).Chunk(3);
                var cursor = query.GetEnumerator();
                if (!cursor.MoveNext()) throw new Exception("empty");
                return cursor.Current;
            }
            int main()
            {
                {
                    Vector<Group<Owner>> saved = new();
                    {
                        var query = new Enumerable<Owner, Source, Source::Cursor>(new Source()).Chunk(2);
                        var cursor = query.GetEnumerator();
                        while (cursor.MoveNext()) saved.Add(cursor.Current);
                    }
                    if (saved.Count != 3 || owners != 5 || cursors != 0) return 1;
                    int expected = 1;
                    for (nuint i = 0; i < saved.Count; i++)
                    for (nuint j = 0; j < saved[i].Count; j++)
                    {
                        if (saved[i][j].Id != expected || !saved[i][j].Text.Equals("owned")) return 2;
                        expected++;
                    }
                    Group<Owner> target = saved[0];
                    readonly(char)* original = target[0].Text.Data!;
                    int baseline = live;
                    attempts = 0;
                    { Group<Owner> sample = Build(); if (sample.Count != 3) return 3; }
                    int observed = attempts;
                    if (observed == 0 || live != baseline || owners != 5 || cursors != 0) return 4;
                    for (int failure = 1; failure <= observed; failure++)
                    {
                        attempts = 0; failAt = failure;
                        bool caught = false;
                        try { target = Build(); } catch (Exception* error) { caught = true; }
                        failAt = 0;
                        if (!caught || live != baseline || owners != 5 || cursors != 0 || target.Count != 2 || target[0].Text.Data != original) return 5;
                    }
                    for (int mode = 0; mode < 2; mode++)
                    {
                        if (mode == 0) failMove = 2;
                        else failRead = 2;
                        {
                            var query = new Enumerable<Owner, Source, Source::Cursor>(new Source()).Chunk(3);
                            var cursor = query.GetEnumerator();
                            bool caught = false;
                            try { cursor.MoveNext(); }
                            catch (Exception* error) { caught = error.GetCode() == 31 + mode; }
                            if (!caught || owners != 5) return 6;
                            int before = reads;
                            if (cursor.MoveNext() || reads != before) return 7;
                            caught = false;
                            try { cursor.Current; } catch (Exception* error) { caught = true; }
                            if (!caught) return 8;
                        }
                        failMove = 0; failRead = 0;
                        if (live != baseline || cursors != 0 || owners != 5) return 9;
                    }
                }
                if (owners != 0 || cursors != 0) return 10;
                return live;
            }
            """, optimization);
}
