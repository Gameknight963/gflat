namespace gflat.Tests.Std.Queries;

public sealed class SelectManyResultTests
{
    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("QUERY_SELECT_MANY.md", 1);

    private static IEnumerable<SourceFile> Sources(string code) => StandardTestSources.Combine(StandardTestSources.Values,
        StandardTestSources.Load("core/Memory/Unique.gf", "core/Queries/IntRange.gf", "core/Queries/FilteredIntRange.gf",
            "core/Queries/SelectManySource.gf", "core/Queries/enumerable_select_many.gf",
            "core/Queries/SelectManyResultSource.gf", "core/Queries/enumerable_select_many_result.gf",
            "core/Queries/enumerable_count.gf", "core/Queries/enumerable_elements.gf",
            "core/Queries/WhereSource.gf", "core/Queries/enumerable_where.gf",
            "core/Queries/SliceSource.gf", "core/Queries/enumerable_slice.gf"))
        .Append(new SourceFile("select-many-result.gf", code));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InferenceFreeFunctionsAndRetainedOuterValuesWork(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            using Standard::Queries;

            int projections = 0;
            int combinations = 0;
            Enumerable<int, IntRange, IntRange::Enumerator> Expand(int value)
            {
                projections++;
                return new IntRange(0, value).AsEnumerable();
            }
            int Combine(int outer, int inner) { combinations++; return outer * 10 + inner; }
            int main()
            {
                var source = new IntRange(0, 4).AsEnumerable();
                var query = source.SelectMany(Expand, Combine);
                if (projections != 0 || combinations != 0) return 1;
                int[6] expected = [10, 20, 21, 30, 31, 32];
                for (int pass = 0; pass < 2; pass++)
                {
                    nuint at = 0;
                    foreach (int value in query)
                    {
                        if (at >= 6 || value != expected[at]) return 2;
                        at++;
                    }
                    if (at != 6 || projections != (pass + 1) * 4 || combinations != (pass + 1) * 6) return 3;
                }
                if (Standard::Queries::SelectMany(source, Expand, Combine).First() != 10) return 4;
                if (source.SelectMany(Expand).Count() != 6) return 5;
                var cursor = query.GetEnumerator();
                if (!cursor.MoveNext()) return 6;
                int before = combinations;
                if (cursor.Current != 10 || cursor.Current != 10 || combinations != before + 2) return 7;
                var moved = move cursor;
                if (!moved.MoveNext() || moved.Current != 20) return 8;
                return 0;
            }
            """), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}\n{result.StandardOutput}\n{result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OwningOuterSnapshotsAndMoveOnlyResultsSurviveAllAllocationFailures(int optimization)
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
            int requests = 0;
            int budget = -1;
            int phase = 0;
            int outerReads = 0;
            int projections = 0;
            int combinations = 0;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes)
                {
                    requests++;
                    if (budget == 0) { budget = -1; return null; }
                    if (budget > 0) budget--;
                    void*? result = malloc(bytes);
                    if (result != null) live++;
                    return result;
                }
                public replace void Free(void* storage) nothrow { live--; free(storage); }
            }
            struct OuterIterator
            {
                private String current;
                private int position;
                public OuterIterator() { current = new(); position = -1; }
                public bool MoveNext()
                {
                    position++;
                    if (position == 0) current = s"a";
                    if (position == 1) current = s"bb";
                    if (position == 2) current = s"ccc";
                    return position < 3;
                }
                public String Current { get { outerReads++; return current; } }
            }
            struct OuterSource
            {
                public OuterSource() { }
                public OuterIterator GetEnumerator() => new OuterIterator();
            }
            struct InnerSource
            {
                private int count;
                public InnerSource(int length) { count = length; }
                public IntRange::Enumerator GetEnumerator()
                {
                    if (phase == 3) throw new Exception("inner construction failed");
                    return new IntRange(0, count).GetEnumerator();
                }
            }
            struct Payload
            {
                public int Value;
                public Payload(int value) { Value = value; owners++; }
                ~Payload() { owners--; }
            }
            Enumerable<int, InnerSource, IntRange::Enumerator> Expand(String value)
            {
                projections++;
                if (phase == 1) throw new Exception("projection failed");
                return new Enumerable<int, InnerSource, IntRange::Enumerator>(new InnerSource((int)value.Length));
            }
            Unique<Payload> Combine(String value, int item)
            {
                combinations++;
                Unique<Payload> result = new(new* Payload((int)value[0] + item));
                if (phase == 2) throw new Exception("combination failed");
                return move result;
            }
            void Run()
            {
                var query = new Enumerable<String, OuterSource, OuterIterator>(new OuterSource()).SelectMany(Expand, Combine);
                int[6] expected = [97, 98, 99, 99, 100, 101];
                nuint at = 0;
                foreach (Unique<Payload> result in query)
                {
                    if (at >= 6 || result.Value.Value != expected[at]) throw new Exception("wrong result");
                    at++;
                }
                if (at != 6) throw new Exception("wrong extent");
            }
            int main()
            {
                Run();
                if (live != 0 || owners != 0 || outerReads != 3 || projections != 3 || combinations != 6) return 1;
                int total = requests;
                for (int failure = 0; failure < total; failure++)
                {
                    budget = failure;
                    bool caught = false;
                    try { Run(); }
                    catch (BadAllocException* error) { caught = true; }
                    budget = -1;
                    if (!caught || live != 0 || owners != 0) return 2;
                }
                for (phase = 1; phase <= 3; phase++)
                {
                    bool caught = false;
                    try { Run(); }
                    catch (Exception* error)
                    {
                        caught = true;
                        if (phase == 1 && !CString::Equals(error.GetMessage(), "projection failed")) return 3;
                        if (phase == 2 && !CString::Equals(error.GetMessage(), "combination failed")) return 4;
                        if (phase == 3 && !CString::Equals(error.GetMessage(), "inner construction failed")) return 13;
                    }
                    if (!caught || live != 0 || owners != 0) return 5;
                }
                phase = 0;
                {
                    var query = new Enumerable<String, OuterSource, OuterIterator>(new OuterSource()).SelectMany(Expand, Combine);
                    Unique<Payload> saved = query.First();
                    if (saved.Value.Value != 97 || owners != 1) return 6;
                    {
                        var iterator = query.GetEnumerator();
                        if (!iterator.MoveNext()) return 7;
                        Unique<Payload> one = iterator.Current;
                        Unique<Payload> two = iterator.Current;
                        if (one.Value.Value != 97 || two.Value.Value != 97 || owners != 3) return 8;
                        phase = 2;
                        bool caught = false;
                        try { Unique<Payload> value = iterator.Current; }
                        catch (Exception* error) { caught = true; }
                        phase = 0;
                        if (!caught || owners != 3 || iterator.MoveNext()) return 9;
                        bool invalid = false;
                        try { Unique<Payload> value = iterator.Current; }
                        catch (Exception* error) { invalid = true; }
                        if (!invalid) return 10;
                    }
                    if (owners != 1 || saved.Value.Value != 97) return 11;
                }
                if (live != 0 || owners != 0) return 12;
                return 0;
            }
            """), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}\n{result.StandardOutput}\n{result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CorrelatedResultsAndComposedFiltersMatchLinq(int optimization)
    {
        string path = Path.Combine(Path.GetTempPath(), "gflat_select_many_result_" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            for (int start = -9; start <= 9; start++)
            for (int length = 0; length <= 16; length++)
            {
                int[] expected = Enumerable.Range(start, length)
                    .SelectMany(value => Enumerable.Range(-value, Math.Abs(value) % 5),
                        (outer, inner) => outer * 1000 + inner).ToArray();
                int[] filtered = expected.Where(value => value % 2 != 0).Take(5).ToArray();
                writer.Write(start);
                writer.Write(length);
                writer.Write(expected.Length);
                writer.Write(filtered.Length);
                foreach (int value in expected.Concat(filtered)) writer.Write(value);
            }
            var result = CompilerTestHelper.Run(Sources($$"""
                using Standard::Queries;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" nuint fread(void* output, nuint size, nuint count, void* file);
                extern "C" int fclose(void* file);
                extern "C" int ferror(void* file);
                Enumerable<int, IntRange, IntRange::Enumerator> Expand(int value)
                {
                    int magnitude = value;
                    if (magnitude < 0) magnitude = -magnitude;
                    return new IntRange(-value, magnitude % 5).AsEnumerable();
                }
                int Combine(int outer, int inner) => outer * 1000 + inner;
                bool Odd(int value) => value % 2 != 0;
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
                        var query = source.SelectMany(Expand, Combine);
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
                        foreach (int value in query.Where(Odd).Take(5))
                        {
                            if (at >= filteredCount || value != expected[count + at]) return 6;
                            at++;
                        }
                        if (at != filteredCount) return 7;
                        if (Standard::Queries::SelectMany(source, Expand, Combine).Count() != count) return 8;
                    }
                    if (ferror(file!) != 0) return 9;
                    return 0;
                }
                """), optimization);
            Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}\n{result.StandardOutput}\n{result.StandardError}");
        }
        finally { File.Delete(path); }
    }
}
