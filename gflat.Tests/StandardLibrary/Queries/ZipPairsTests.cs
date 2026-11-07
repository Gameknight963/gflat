namespace gflat.Tests.Std.Queries;

public sealed class ZipPairsTests
{
    private static void Run(string code, int optimization)
    {
        var sources = StandardTestSources.Combine(StandardTestSources.Values, StandardTestSources.Load(
            "core/Queries/ZipSource.gf", "core/Queries/Zipped.gf", "core/Queries/enumerable_zip.gf",
            "core/Queries/enumerable_zip_pairs.gf"))
            .Append(new SourceFile("zip-pairs.gf", code));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Zip pairs exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Fact]
    public void GuideCompiles() => StandardLibraryGuideTests.CheckProgram("QUERY_ZIP_PAIRS.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PairFieldsAndAllSmallPrefixLengthsMatchLinqWithoutAllocation(int optimization)
    {
        int[] left = [17, -2, 4, 98, 5, 6, -11, 8, 93, 10, 0, 12];
        int[] right = [41, 8, -16, 99, 0, 3, -2, 1];
        var expected = left.Zip(right).ToArray();
        var counts = new List<int>();
        for (int l = 0; l <= left.Length; l++)
        for (int r = 0; r <= right.Length; r++) counts.Add(left.Take(l).Zip(right.Take(r)).Count());
        Run($$"""
            using Standard;
            using Standard::Collections;
            using Standard::Memory;
            using Standard::Queries;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            int attempts = 0;
            bool deny = false;
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
            int Add(int first, int second) => first + second;
            int main()
            {
                int[{{left.Length}}] left = [{{string.Join(",", left)}}];
                int[{{right.Length}}] right = [{{string.Join(",", right)}}];
                int[{{expected.Length}}] first = [{{string.Join(",", expected.Select(p => p.First))}}];
                int[{{expected.Length}}] second = [{{string.Join(",", expected.Select(p => p.Second))}}];
                int[{{counts.Count}}] counts = [{{string.Join(",", counts)}}];
                Span<int> leftView = new(&left[0], {{left.Length}});
                Span<int> rightView = new(&right[0], {{right.Length}});
                int before = attempts;
                deny = true;
                for (nuint l = 0; l <= {{left.Length}}; l++)
                for (nuint r = 0; r <= {{right.Length}}; r++)
                {
                    var a = leftView.Slice(0, l).AsEnumerable();
                    var b = rightView.Slice(0, r).AsEnumerable();
                    var query = a.Zip(b);
                    nuint expectedCount = (nuint)counts[l * {{right.Length + 1}} + r];
                    var cursor = query.GetEnumerator();
                    nuint count = 0;
                    while (cursor.MoveNext())
                    {
                        var pair = cursor.Current;
                        var again = cursor.Current;
                        if (pair.First != first[count] || pair.Second != second[count]) return 1;
                        if (pair.First != again.First || pair.Second != again.Second) return 2;
                        pair.First = 123;
                        if (left[count] != first[count]) return 3;
                        count++;
                    }
                    if (count != expectedCount || cursor.MoveNext()) return 4;
                    var free = Standard::Queries::Zip(a, b);
                    count = 0;
                    foreach (Zipped<int, int> pair in free)
                    {
                        if (pair.First != first[count] || pair.Second != second[count]) return 5;
                        count++;
                    }
                    if (count != expectedCount) return 6;
                    var selected = a.Zip<int, int, Span<int>, Span<int>::Enumerator>(b, Add);
                    count = 0;
                    foreach (int value in selected)
                    {
                        if (value != first[count] + second[count]) return 7;
                        count++;
                    }
                    if (count != expectedCount) return 8;
                }
                deny = false;
                if (attempts != before) return 9;
                var reusable = leftView.AsEnumerable().Zip(rightView.AsEnumerable());
                var one = reusable.GetEnumerator();
                var two = reusable.GetEnumerator();
                if (!one.MoveNext() || !one.MoveNext() || !two.MoveNext()) return 10;
                if (one.Current.First != left[1] || two.Current.First != left[0]) return 11;
                bool invalid = false;
                var unopened = reusable.GetEnumerator();
                try { unopened.Current; } catch (Exception* error) { invalid = true; }
                if (!invalid) return 12;
                left[0] = 999;
                var fresh = reusable.GetEnumerator();
                if (!fresh.MoveNext() || fresh.Current.First != 999) return 13;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MoveOnlyPairsOwnBothValuesAndEveryAcquisitionFailurePreservesOldTarget(int optimization)
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
            int failRead = 0;
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
                private int side;
                public Source(int id) { side = id; }
                public Cursor GetEnumerator() => new Cursor(side);
                public struct Cursor
                {
                    private int side;
                    private String token;
                    public Cursor(int id) { side = id; token = s"cursor"; cursors++; }
                    ~Cursor() { cursors--; }
                    public bool MoveNext() => true;
                    public Owner Current
                    {
                        get
                        {
                            if (failRead == side) throw new Exception("read", 71);
                            return new Owner(side * 10 + 1);
                        }
                    }
                }
            }
            Zipped<Owner, Owner> Build()
            {
                var left = new Enumerable<Owner, Source, Source::Cursor>(new Source(1));
                var right = new Enumerable<Owner, Source, Source::Cursor>(new Source(2));
                var cursor = left.Zip(right).GetEnumerator();
                if (!cursor.MoveNext()) throw new Exception("empty");
                return cursor.Current;
            }
            int main()
            {
                {
                    Zipped<Owner, Owner> target = new(new Owner(7), new Owner(8));
                    readonly(char)* first = target.First.Text.Data!;
                    readonly(char)* second = target.Second.Text.Data!;
                    int baseline = live;
                    attempts = 0;
                    {
                        Zipped<Owner, Owner> sample = Build();
                        if (sample.First.Id != 11 || sample.Second.Id != 21 || cursors != 0 || owners != 4) return 1;
                    }
                    int observed = attempts;
                    if (observed == 0 || live != baseline || owners != 2) return 2;
                    for (int failure = 1; failure <= observed; failure++)
                    {
                        attempts = 0; failAt = failure;
                        bool caught = false;
                        try { target = Build(); } catch (Exception* error) { caught = true; }
                        failAt = 0;
                        if (!caught || live != baseline || owners != 2 || cursors != 0) return 3;
                        if (target.First.Id != 7 || target.Second.Id != 8 || target.First.Text.Data != first || target.Second.Text.Data != second) return 4;
                    }
                    for (int side = 1; side <= 2; side++)
                    {
                        failRead = side;
                        bool caught = false;
                        try { target = Build(); } catch (Exception* error) { caught = error.GetCode() == 71; }
                        failRead = 0;
                        if (!caught || live != baseline || owners != 2 || cursors != 0) return 5;
                        if (target.First.Text.Data != first || target.Second.Text.Data != second) return 6;
                    }
                    target = Build();
                    if (target.First.Id != 11 || target.Second.Id != 21 || cursors != 0 || owners != 2) return 7;
                    Zipped<String, String> snapshot = new(s"old", s"old");
                    {
                        Vector<String> left = new();
                        Vector<String> right = new();
                        left.Add(s"first");
                        right.Add(s"second");
                        var cursor = left.AsEnumerable().Zip(right.AsEnumerable()).GetEnumerator();
                        if (!cursor.MoveNext()) return 9;
                        snapshot = cursor.Current;
                        Zipped<String, String> again = cursor.Current;
                        if (snapshot.First.Data == again.First.Data || snapshot.Second.Data == again.Second.Data) return 10;
                        if (snapshot.First.Data == left[0].Data || snapshot.Second.Data == right[0].Data) return 11;
                    }
                    if (!snapshot.First.Equals("first") || !snapshot.Second.Equals("second")) return 12;
                }
                if (owners != 0 || cursors != 0) return 8;
                return live;
            }
            """, optimization);
}
