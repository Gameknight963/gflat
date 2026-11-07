namespace gflat.Tests.Std.Queries;

public sealed class IndexedSelectManyTests
{
    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("QUERY_INDEXED_SELECT_MANY.md", 0);

    private static IEnumerable<SourceFile> Sources(string code) => StandardTestSources.Combine(StandardTestSources.Values,
        StandardTestSources.Load("core/Memory/Unique.gf", "core/Queries/IntRange.gf", "core/Queries/FilteredIntRange.gf",
            "core/Queries/SelectManySource.gf", "core/Queries/enumerable_select_many.gf",
            "core/Queries/IndexedSelectManySource.gf", "core/Queries/enumerable_indexed_select_many.gf",
            "core/Queries/enumerable_count.gf", "core/Queries/enumerable_elements.gf",
            "core/Queries/WhereSource.gf", "core/Queries/enumerable_where.gf",
            "core/Queries/SliceSource.gf", "core/Queries/enumerable_slice.gf"))
        .Append(new SourceFile("indexed-select-many.gf", code));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void UpstreamFiltersUseNewOuterIndicesAndSimultaneousCursorsRemainIndependent(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            using Standard::Queries;

            int calls = 0;
            bool Even(int value) => value % 2 == 0;
            Enumerable<int, IntRange, IntRange::Enumerator> Expand(int value, nuint index)
            {
                if (value != (int)index * 2) throw new Exception("wrong filtered index");
                calls++;
                return new IntRange(value * 100 + (int)index * 10, value % 3).AsEnumerable();
            }
            int main()
            {
                var query = new IntRange(0, 6).AsEnumerable().Where(Even).SelectMany(Expand);
                if (calls != 0) return 1;
                {
                    var first = query.GetEnumerator();
                    var second = query.GetEnumerator();
                    if (!first.MoveNext() || first.Current != 210) return 2;
                    if (!second.MoveNext() || second.Current != 210 || calls != 4) return 3;
                    if (first.Current != 210 || calls != 4) return 4;
                    if (!first.MoveNext() || first.Current != 211) return 5;
                    if (!second.MoveNext() || second.Current != 211) return 6;
                    if (!first.MoveNext() || first.Current != 420) return 7;
                    if (!second.MoveNext() || second.Current != 420 || calls != 6) return 8;
                    if (first.MoveNext() || second.MoveNext() || calls != 6) return 9;
                }
                for (int pass = 0; pass < 2; pass++)
                {
                    int[3] expected = [210, 211, 420];
                    nuint at = 0;
                    foreach (int value in query)
                    {
                        if (at >= 3 || value != expected[at]) return 10;
                        at++;
                    }
                    if (at != 3) return 11;
                }
                if (calls != 12) return 12;
                return 0;
            }
            """), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InferenceFlatteningAndIndependentEnumerationsWork(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            using Standard::Queries;

            int projections = 0;
            Enumerable<int, IntRange, IntRange::Enumerator> Expand(int value, nuint index)
            {
                projections++;
                if ((nuint)value != index) throw new Exception("wrong outer index");
                return new IntRange(value * 10, value).AsEnumerable();
            }
            int main()
            {
                var query = new IntRange(0, 4).AsEnumerable().SelectMany(Expand);
                if (projections != 0) return 1;
                int[6] expected = [10, 20, 21, 30, 31, 32];
                for (int pass = 0; pass < 2; pass++)
                {
                    nuint index = 0;
                    foreach (int item in query)
                    {
                        if (index >= 6 || item != expected[index]) return 2;
                        index++;
                    }
                    if (index != 6 || projections != (pass + 1) * 4) return 3;
                }
                if (new IntRange(0, 0).AsEnumerable().SelectMany(Expand).Count() != 0) return 4;
                if (new IntRange(0, 4).AsEnumerable().SelectMany(Expand).First() != 10) return 5;
                return 0;
            }
            """), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}\n{result.StandardOutput}\n{result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void StableInnerStorageAndEveryFailureReleaseResources(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            using Standard;
            using Standard::Memory;
            using Standard::Queries;
            using Standard::Text;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            int live = 0;
            int requests = 0;
            int budget = -1;
            int innerLive = 0;
            int outerLive = 0;
            int sourceLive = 0;
            int badOrder = 0;
            int projections = 0;
            int innerMoves = 0;
            int currentReads = 0;
            int phase = 0;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes)
                {
                    requests++;
                    if (budget == 0) { budget = -1; return null; }
                    if (budget > 0) budget--;
                    void*? value = malloc(bytes);
                    if (value != null) live++;
                    return value;
                }
                public replace void Free(void* storage) nothrow { live--; free(storage); }
            }
            struct InnerIterator
            {
                private immutable(String)* text;
                private int position;
                private int count;
                private int first;
                public InnerIterator(immutable(String)* input, int start, int length)
                {
                    if (phase == 2) throw new Exception("inner factory");
                    text = input;
                    position = -1;
                    count = length;
                    first = start;
                    innerLive++;
                }
                public bool MoveNext()
                {
                    innerMoves++;
                    if (phase == 3) throw new Exception("inner move");
                    position++;
                    return position < count;
                }
                public int Current
                {
                    get
                    {
                        currentReads++;
                        if (!text.Equals("alive")) throw new Exception("source moved or destroyed");
                        if (phase == 4) throw new Exception("inner current");
                        return first + position;
                    }
                }
                ~InnerIterator()
                {
                    if (!text.Equals("alive")) badOrder++;
                    innerLive--;
                }
            }
            struct OuterIterator
            {
                private String token;
                private int position;
                public OuterIterator() { token = s"outer"; position = -1; outerLive++; }
                public bool MoveNext()
                {
                    if (phase == 5) throw new Exception("outer move");
                    position++;
                    return position < 4;
                }
                public int Current
                {
                    get
                    {
                        if (phase == 6) throw new Exception("outer current");
                        return position;
                    }
                }
                ~OuterIterator() { outerLive--; }
            }
            struct OuterSource
            {
                public OuterSource() { }
                public OuterIterator GetEnumerator() => new OuterIterator();
            }
            struct InnerSource
            {
                private String text;
                private int start;
                private int count;
                public InnerSource(int first, int length)
                {
                    text = s"alive";
                    start = first;
                    count = length;
                    sourceLive++;
                }
                public copy InnerSource(immutable(InnerSource)* input)
                {
                    text = input.text;
                    start = input.start;
                    count = input.count;
                    sourceLive++;
                }
                public InnerIterator GetEnumerator() => new InnerIterator(&text, start, count);
                ~InnerSource() { sourceLive--; }
            }
            Enumerable<int, InnerSource, InnerIterator> Expand(int value, nuint index)
            {
                projections++;
                if (phase == 1) throw new Exception("selector");
                return new Enumerable<int, InnerSource, InnerIterator>(new InnerSource(value * 10, value));
            }
            void Run()
            {
                var query = new Enumerable<int, OuterSource, OuterIterator>(new OuterSource()).SelectMany(Expand);
                var iterator = query.GetEnumerator();
                if (!iterator.MoveNext()) throw new Exception("missing first");
                if (iterator.Current != 10) throw new Exception("wrong first");
                var moved = move iterator;
                if (moved.Current != 10) throw new Exception("move invalidated inner");
                int expected = 20;
                while (moved.MoveNext())
                {
                    int actual = moved.Current;
                    if (actual != expected) throw new Exception("wrong element");
                    if (expected == 21) expected = 30;
                    else expected++;
                }
                if (expected != 33 || moved.MoveNext()) throw new Exception("wrong extent");
            }
            int main()
            {
                Run();
                if (live != 0 || innerLive != 0 || outerLive != 0 || sourceLive != 0 || badOrder != 0 || projections != 4) return 1;
                int total = requests;
                for (int failure = 0; failure < total; failure++)
                {
                    budget = failure;
                    bool caught = false;
                    try { Run(); }
                    catch (BadAllocException* error) { caught = true; }
                    budget = -1;
                    if (!caught || live != 0 || innerLive != 0 || outerLive != 0 || sourceLive != 0 || badOrder != 0) return 2;
                }
                for (phase = 1; phase <= 6; phase++)
                {
                    bool caught = false;
                    try { Run(); }
                    catch (Exception* error)
                    {
                        caught = true;
                        if (phase == 1 && !CString::Equals(error.GetMessage(), "selector")) return 3;
                        if (phase == 2 && !CString::Equals(error.GetMessage(), "inner factory")) return 4;
                        if (phase == 3 && !CString::Equals(error.GetMessage(), "inner move")) return 5;
                        if (phase == 4 && !CString::Equals(error.GetMessage(), "inner current")) return 6;
                        if (phase == 5 && !CString::Equals(error.GetMessage(), "outer move")) return 17;
                        if (phase == 6 && !CString::Equals(error.GetMessage(), "outer current")) return 18;
                    }
                    if (!caught || live != 0 || innerLive != 0 || outerLive != 0 || sourceLive != 0 || badOrder != 0) return 7;
                }
                phase = 0;
                projections = 0;
                innerMoves = 0;
                currentReads = 0;
                {
                    var query = new IntRange(0, 4).AsEnumerable().SelectMany(Expand);
                    if (projections != 0) return 8;
                    var iterator = query.GetEnumerator();
                    bool invalid = false;
                    try { int value = iterator.Current; }
                    catch (Exception* error) { invalid = true; }
                    if (!invalid || projections != 0) return 9;
                    if (!iterator.MoveNext() || projections != 2 || innerLive != 1) return 10;
                    if (iterator.Current != 10 || iterator.Current != 10 || currentReads != 2) return 11;
                }
                if (live != 0 || innerLive != 0 || sourceLive != 0 || badOrder != 0 || projections != 2) return 12;
                {
                    var query = new IntRange(1, 3).AsEnumerable().SelectMany(Expand);
                    var iterator = query.GetEnumerator();
                    if (!iterator.MoveNext()) return 13;
                    phase = 3;
                    bool caught = false;
                    try { iterator.MoveNext(); }
                    catch (Exception* error) { caught = true; }
                    phase = 0;
                    if (!caught || innerLive != 0 || sourceLive != 0 || iterator.MoveNext()) return 14;
                    bool invalid = false;
                    try { int value = iterator.Current; }
                    catch (Exception* error) { invalid = true; }
                    if (!invalid) return 15;
                }
                if (live != 0 || innerLive != 0 || sourceLive != 0 || badOrder != 0) return 16;
                return 0;
            }
            """), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}\n{result.StandardOutput}\n{result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FlatteningReplayAndComposedFiltersMatchLinq(int optimization)
    {
        string path = Path.Combine(Path.GetTempPath(), "gflat_indexed_select_many_" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            for (int start = -8; start <= 8; start++)
            for (int length = 0; length <= 16; length++)
            {
                int[] expected = Enumerable.Range(start, length)
                    .SelectMany((value, index) => Enumerable.Range(value * 7 + index * 100, value * value % 5)).ToArray();
                int[] filtered = expected.Where(value => value % 3 == 0).Skip(1).Take(7).ToArray();
                writer.Write(start);
                writer.Write(length);
                writer.Write(expected.Length);
                writer.Write(filtered.Length);
                foreach (int value in expected.Concat(filtered)) writer.Write(value);
            }
            var result = CompilerTestHelper.Run(Sources($$"""
                using Standard::Queries;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" nuint fread(void* data, nuint size, nuint count, void* file);
                extern "C" int fclose(void* file);
                extern "C" int ferror(void* file);
                Enumerable<int, IntRange, IntRange::Enumerator> Expand(int value, nuint index)
                    => new IntRange(value * 7 + (int)index * 100, value * value % 5).AsEnumerable();
                bool Selected(int value) => value % 3 == 0;
                int main()
                {
                    void*? file = fopen("{{path.Replace("\\", "\\\\")}}", "rb");
                    if (file == null) return 1;
                    defer { fclose(file!); }
                    int[4] header = default(int[4]);
                    int[80] expected;
                    while (true)
                    {
                        nuint bytes = fread(&header[0], 1, 16, file!);
                        if (bytes == 0) break;
                        if (bytes != 16) return 2;
                        nuint count = (nuint)header[2];
                        nuint filteredCount = (nuint)header[3];
                        nuint payload = (count + filteredCount) * 4;
                        if (fread(&expected[0], 1, payload, file!) != payload) return 3;
                        var source = new IntRange(header[0], header[1]).AsEnumerable();
                        var query = source.SelectMany(Expand);
                        for (int pass = 0; pass < 2; pass++)
                        {
                            nuint at = 0;
                            foreach (int value in query)
                            {
                                if (at >= count || value != expected[at]) return 4;
                                at++;
                            }
                            if (at != count) return 5;
                        }
                        nuint at = 0;
                        foreach (int value in query.Where(Selected).Skip(1).Take(7))
                        {
                            if (at >= filteredCount || value != expected[count + at]) return 6;
                            at++;
                        }
                        if (at != filteredCount) return 7;
                        if (Standard::Queries::SelectMany(source, Expand).Count() != count) return 8;
                        if (query.Take(0).Count() != 0) return 9;
                    }
                    if (ferror(file!) != 0) return 10;
                    return 0;
                }
                """), optimization);
            Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}\n{result.StandardOutput}\n{result.StandardError}");
        }
        finally { File.Delete(path); }
    }
}
