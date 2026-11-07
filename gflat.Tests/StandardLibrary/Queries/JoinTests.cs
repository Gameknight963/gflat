namespace gflat.Tests.Std.Queries;

public sealed class JoinTests
{
    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("QUERY_JOIN.md", 0);

    private static IEnumerable<SourceFile> Sources(string code) => StandardTestSources.Combine(StandardTestSources.Values,
        StandardTestSources.Load("core/Collections/HashMap.gf", "core/Queries/IntRange.gf", "core/Queries/FilteredIntRange.gf",
            "core/Queries/JoinSource.gf", "core/Queries/enumerable_join.gf",
            "core/Queries/enumerable_count.gf", "core/Queries/enumerable_elements.gf",
            "core/Queries/WhereSource.gf", "core/Queries/enumerable_where.gf",
            "core/Queries/SliceSource.gf", "core/Queries/enumerable_slice.gf", "core/Memory/Unique.gf"))
        .Append(new SourceFile("query-join.gf", code));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InferenceReplayAndStableDuplicateOrderingWork(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            using Standard::Queries;

            int outerCalls = 0;
            int innerCalls = 0;
            int results = 0;
            int OuterKey(int value) { outerCalls++; return value % 2; }
            int InnerKey(int value) { innerCalls++; return value % 2; }
            int Combine(int outer, int inner) { results++; return outer * 10 + inner; }
            int main()
            {
                var outer = new IntRange(0, 3).AsEnumerable();
                var inner = new IntRange(0, 4).AsEnumerable();
                var query = outer.Join(inner, OuterKey, InnerKey, Combine);
                if (outerCalls != 0 || innerCalls != 0 || results != 0) return 1;
                int[6] expected = [0, 2, 11, 13, 20, 22];
                for (int pass = 0; pass < 2; pass++)
                {
                    nuint at = 0;
                    foreach (int value in query)
                    {
                        if (at >= 6 || value != expected[at]) return 2;
                        at++;
                    }
                    if (at != 6 || outerCalls != (pass + 1) * 3
                        || innerCalls != (pass + 1) * 4 || results != (pass + 1) * 6) return 3;
                }
                if (Standard::Queries::Join(outer, inner, OuterKey, InnerKey, Combine).First() != 0) return 4;
                if (new IntRange(0, 0).AsEnumerable().Join(inner, OuterKey, InnerKey, Combine).Count() != 0) return 5;
                return 0;
            }
            """), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}\n{result.StandardOutput}\n{result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void StableMatchesReplayAndComposedResultsMatchLinq(int optimization)
    {
        string path = Path.Combine(Path.GetTempPath(), "gflat_join_" + Guid.NewGuid().ToString("N"));
        try
        {
            var random = new Random(738129);
            var cases = new List<(int[] Outer, int[] Inner)> { ([], []), ([], [1]), ([1], []), ([0, 0], [0, 0, 0]), ([-1, 0, 1], [-1, 0, 1]) };
            for (int i = 0; i < 512; i++)
                cases.Add((Enumerable.Range(0, random.Next(17)).Select(_ => random.Next(-12, 13)).ToArray(),
                    Enumerable.Range(0, random.Next(17)).Select(_ => random.Next(-12, 13)).ToArray()));
            using (var writer = new BinaryWriter(File.Create(path)))
            foreach (var (outer, inner) in cases)
            {
                int[] expected = outer.Join(inner, value => value % 5, value => value % 5,
                    (left, right) => left * 100 + right).ToArray();
                int[] filtered = expected.Where(value => value % 2 != 0).Skip(1).Take(7).ToArray();
                writer.Write(outer.Length);
                writer.Write(inner.Length);
                writer.Write(expected.Length);
                writer.Write(filtered.Length);
                foreach (int value in outer.Concat(inner).Concat(expected).Concat(filtered)) writer.Write(value);
            }
            var result = CompilerTestHelper.Run(Sources($$"""
                using Standard::Memory;
                using Standard::Queries;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" nuint fread(void* output, nuint size, nuint count, void* file);
                extern "C" int fclose(void* file);
                extern "C" int ferror(void* file);
                int Key(int value) => value % 5;
                int Combine(int outer, int inner) => outer * 100 + inner;
                bool Odd(int value) => value % 2 != 0;
                int main()
                {
                    void*? file = fopen("{{path.Replace("\\", "\\\\")}}", "rb");
                    if (file == null) return 1;
                    defer { fclose(file!); }
                    int[4] header = default(int[4]);
                    int[16] first;
                    int[16] second;
                    int[272] expected;
                    while (true)
                    {
                        nuint bytes = fread(&header[0], 1, 16, file!);
                        if (bytes == 0) break;
                        if (bytes != 16) return 2;
                        nuint outerCount = (nuint)header[0];
                        nuint innerCount = (nuint)header[1];
                        nuint count = (nuint)header[2];
                        nuint filteredCount = (nuint)header[3];
                        if (fread(&first[0], 4, outerCount, file!) != outerCount) return 3;
                        if (fread(&second[0], 4, innerCount, file!) != innerCount) return 4;
                        nuint payload = count + filteredCount;
                        if (fread(&expected[0], 4, payload, file!) != payload) return 5;
                        var outer = new Span<int>(&first[0], outerCount).AsEnumerable();
                        var inner = new Span<int>(&second[0], innerCount).AsEnumerable();
                        var query = outer.Join(inner, Key, Key, Combine);
                        for (int pass = 0; pass < 2; pass++)
                        {
                            nuint at = 0;
                            foreach (int value in query)
                            {
                                if (at >= count || value != expected[at]) return 6;
                                at++;
                            }
                            if (at != count) return 7;
                        }
                        nuint at = 0;
                        foreach (int value in query.Where(Odd).Skip(1).Take(7))
                        {
                            if (at >= filteredCount || value != expected[count + at]) return 8;
                            at++;
                        }
                        if (at != filteredCount || Standard::Queries::Join(outer, inner, Key, Key, Combine).Count() != count) return 9;
                    }
                    if (ferror(file!) != 0) return 10;
                    return 0;
                }
                """), optimization);
            Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}\n{result.StandardOutput}\n{result.StandardError}");
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OwnedCollidingKeysValuesAndResultsCleanUpEveryObservedFailure(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            using Standard;
            using Standard::Collections;
            using Standard::Memory;
            using Standard::Queries;
            using Standard::Text;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            int live = 0;
            int items = 0;
            int keys = 0;
            int requests = 0;
            int budget = -1;
            int phase = 0;
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
            struct Item
            {
                public int Id;
                private String text;
                public Item(int value) { Id = value; text = s"item"; items++; }
                public copy Item(immutable(Item)* source)
                {
                    Id = source.Id;
                    text = source.text;
                    if (phase == 6) throw new Exception("copy failed");
                    items++;
                }
                ~Item() { items--; }
            }
            struct Key : IHashable, IEquatable<Key>
            {
                private int id;
                private String text;
                public Key(int value) { id = value; text = s"key"; keys++; }
                public immutable nuint GetHashCode()
                {
                    if (phase == 4) throw new Exception("hash failed");
                    return 1;
                }
                public immutable bool Equals(immutable(Key)* other)
                {
                    if (phase == 5) throw new Exception("equality failed");
                    return id == other.id;
                }
                ~Key() { keys--; }
            }
            Key OuterKey(Item value)
            {
                if (phase == 1) throw new Exception("outer key failed");
                return new Key(value.Id % 2);
            }
            Key InnerKey(Item value)
            {
                if (phase == 2) throw new Exception("inner key failed");
                return new Key(value.Id % 2);
            }
            Unique<Item> Combine(Item outer, Item inner)
            {
                Unique<Item> result = new(new* Item(outer.Id * 10 + inner.Id));
                if (phase == 3) throw new Exception("result failed");
                return move result;
            }
            void Run()
            {
                Vector<Item> outer = new();
                Vector<Item> inner = new();
                for (int i = 0; i < 3; i++) outer.Add(new Item(i));
                for (int i = 0; i < 4; i++) inner.Add(new Item(i));
                var query = outer.AsEnumerable().Join(inner.AsEnumerable(), OuterKey, InnerKey, Combine);
                int[6] expected = [0, 2, 11, 13, 20, 22];
                nuint at = 0;
                foreach (Unique<Item> value in query)
                {
                    if (at >= 6 || value.Value.Id != expected[at]) throw new Exception("wrong result");
                    at++;
                }
                if (at != 6 || items != 7 || keys != 0 || outer.Count != 3 || inner.Count != 4)
                    throw new Exception("wrong final state");
            }
            int main()
            {
                Run();
                if (live != 0 || items != 0 || keys != 0) return 1;
                int total = requests;
                for (int failure = 0; failure < total; failure++)
                {
                    budget = failure;
                    bool caught = false;
                    try { Run(); }
                    catch (BadAllocException* error) { caught = true; }
                    budget = -1;
                    if (!caught || live != 0 || items != 0 || keys != 0) return 2;
                }
                for (phase = 1; phase <= 6; phase++)
                {
                    bool caught = false;
                    try { Run(); }
                    catch (Exception* error)
                    {
                        caught = true;
                        if (phase == 1 && !CString::Equals(error.GetMessage(), "outer key failed")) return 3;
                        if (phase == 2 && !CString::Equals(error.GetMessage(), "inner key failed")) return 4;
                        if (phase == 3 && !CString::Equals(error.GetMessage(), "result failed")) return 5;
                        if (phase == 4 && !CString::Equals(error.GetMessage(), "hash failed")) return 6;
                        if (phase == 5 && !CString::Equals(error.GetMessage(), "equality failed")) return 7;
                        if (phase == 6 && !CString::Equals(error.GetMessage(), "copy failed")) return 8;
                    }
                    if (!caught || live != 0 || items != 0 || keys != 0) return 9;
                }
                phase = 0;
                {
                    Vector<Item> outer = new();
                    Vector<Item> inner = new();
                    outer.Add(new Item(1));
                    inner.Add(new Item(1));
                    inner.Add(new Item(3));
                    var query = outer.AsEnumerable().Join(inner.AsEnumerable(), OuterKey, InnerKey, Combine);
                    Unique<Item> saved = query.First();
                    if (saved.Value.Id != 11 || items != 4 || keys != 0) return 10;
                    {
                        var iterator = query.GetEnumerator();
                        if (!iterator.MoveNext()) return 11;
                        var moved = move iterator;
                        Unique<Item> first = moved.Current;
                        if (first.Value.Id != 11 || !moved.MoveNext()) return 12;
                        Unique<Item> second = moved.Current;
                        if (second.Value.Id != 13) return 13;
                        phase = 3;
                        bool caught = false;
                        try { Unique<Item> value = moved.Current; }
                        catch (Exception* error) { caught = true; }
                        phase = 0;
                        if (!caught || keys != 0 || moved.MoveNext()) return 14;
                    }
                    if (saved.Value.Id != 11 || items != 4 || keys != 0) return 15;
                }
                if (live != 0 || items != 0 || keys != 0) return 16;
                return 0;
            }
            """), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}\n{result.StandardOutput}\n{result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EmptyInputsIteratorFailuresAndCursorMovesFollowTheLazyProtocol(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            using Standard::Queries;

            int outerCount = 0;
            int innerCount = 0;
            int outerFactories = 0;
            int innerFactories = 0;
            int outerMoves = 0;
            int innerMoves = 0;
            int outerReads = 0;
            int innerReads = 0;
            int outerCloses = 0;
            int innerCloses = 0;
            int outerKeys = 0;
            int innerKeys = 0;
            int results = 0;
            bool failInnerFactory = false;
            bool failInnerMove = false;
            bool failOuterRead = false;
            bool failOuterMove = false;
            bool failResult = false;
            void Reset()
            {
                outerFactories = 0; innerFactories = 0;
                outerMoves = 0; innerMoves = 0;
                outerReads = 0; innerReads = 0;
                outerCloses = 0; innerCloses = 0;
                outerKeys = 0; innerKeys = 0; results = 0;
            }
            struct Cursor
            {
                private bool isOuter;
                private int position;
                private int count;
                public Cursor(bool first)
                {
                    isOuter = first;
                    position = -1;
                    count = innerCount;
                    if (first) count = outerCount;
                }
                public bool MoveNext()
                {
                    if (isOuter)
                    {
                        outerMoves++;
                        if (failOuterMove) throw new Exception("outer move");
                    }
                    else innerMoves++;
                    position++;
                    if (!isOuter && failInnerMove && position == 1) throw new Exception("inner move");
                    return position < count;
                }
                public int Current
                {
                    get
                    {
                        if (isOuter)
                        {
                            outerReads++;
                            if (failOuterRead) throw new Exception("outer read");
                        }
                        else innerReads++;
                        return position;
                    }
                }
                ~Cursor()
                {
                    if (isOuter) outerCloses++;
                    else innerCloses++;
                }
            }
            struct Source
            {
                private bool isOuter;
                public Source(bool first) { isOuter = first; }
                public Cursor GetEnumerator()
                {
                    if (isOuter) outerFactories++;
                    else
                    {
                        innerFactories++;
                        if (failInnerFactory) throw new Exception("inner factory");
                    }
                    return new Cursor(isOuter);
                }
            }
            int OuterKey(int value) { outerKeys++; return value % 2; }
            int InnerKey(int value) { innerKeys++; return value % 2; }
            int Combine(int outer, int inner)
            {
                results++;
                if (failResult) throw new Exception("result");
                return outer * 10 + inner;
            }
            int main()
            {
                var outer = new Enumerable<int, Source, Cursor>(new Source(true));
                var inner = new Enumerable<int, Source, Cursor>(new Source(false));
                var query = outer.Join(inner, OuterKey, InnerKey, Combine);
                if (outerFactories != 0 || innerFactories != 0) return 1;
                outerCount = 0; innerCount = 4; failInnerFactory = true;
                if (query.Count() != 0 || outerMoves != 1 || outerReads != 0 || innerFactories != 0
                    || outerCloses != 1 || innerCloses != 0 || outerKeys != 0 || innerKeys != 0) return 2;
                Reset();
                outerCount = 3; innerCount = 0; failInnerFactory = false; failOuterRead = true;
                if (query.Count() != 0 || outerMoves != 1 || outerReads != 0 || innerFactories != 1
                    || innerMoves != 1 || innerCloses != 1 || outerCloses != 1 || outerKeys != 0) return 3;
                Reset();
                innerCount = 4; failOuterRead = false;
                {
                    var iterator = query.GetEnumerator();
                    if (outerFactories != 1 || innerFactories != 0) return 4;
                    bool invalid = false;
                    try { int value = iterator.Current; }
                    catch (Exception* error) { invalid = true; }
                    if (!invalid || !iterator.MoveNext() || innerFactories != 1 || innerCloses != 1
                        || innerReads != 4 || innerMoves != 5 || outerReads != 1 || results != 0) return 5;
                    var moved = move iterator;
                    if (moved.Current != 0 || moved.Current != 0 || results != 2) return 6;
                    if (!moved.MoveNext() || moved.Current != 2 || outerReads != 1) return 7;
                    if (!moved.MoveNext() || moved.Current != 11 || outerReads != 2) return 8;
                    failResult = true;
                    bool caught = false;
                    try { int value = moved.Current; }
                    catch (Exception* error) { caught = true; }
                    failResult = false;
                    if (!caught || moved.MoveNext() || outerReads != 2) return 9;
                }
                if (outerCloses != 1 || innerCloses != 1) return 10;
                Reset();
                failInnerFactory = true;
                {
                    var iterator = query.GetEnumerator();
                    bool caught = false;
                    try { iterator.MoveNext(); }
                    catch (Exception* error) { caught = true; }
                    if (!caught || iterator.MoveNext() || outerReads != 0 || innerCloses != 0) return 11;
                }
                if (outerCloses != 1) return 12;
                Reset();
                failInnerFactory = false; failInnerMove = true;
                {
                    var iterator = query.GetEnumerator();
                    bool caught = false;
                    try { iterator.MoveNext(); }
                    catch (Exception* error) { caught = true; }
                    if (!caught || iterator.MoveNext() || innerCloses != 1 || innerReads != 1
                        || outerReads != 0 || results != 0) return 13;
                }
                if (outerCloses != 1) return 14;
                Reset();
                failInnerMove = false;
                {
                    var iterator = query.GetEnumerator();
                    if (!iterator.MoveNext() || !iterator.MoveNext()) return 15;
                    failOuterMove = true;
                    bool caught = false;
                    try { iterator.MoveNext(); }
                    catch (Exception* error) { caught = true; }
                    failOuterMove = false;
                    if (!caught || iterator.MoveNext() || innerCloses != 1) return 16;
                }
                if (outerCloses != 1) return 17;
                return 0;
            }
            """), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}\n{result.StandardOutput}\n{result.StandardError}");
    }
}
