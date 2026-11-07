namespace gflat.Tests.Std.Queries;

public sealed class GroupJoinTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void GuideExamplesCompile(int index) => StandardLibraryGuideTests.CheckProgram("QUERY_GROUP_JOIN.md", index);

    private static IEnumerable<SourceFile> Sources(string code) => StandardTestSources.Combine(StandardTestSources.Values,
        StandardTestSources.Load("core/Collections/HashMap.gf", "core/Threading/Interlocked.gf",
            "core/Queries/Group.gf", "core/Queries/GroupJoinSource.gf", "core/Queries/enumerable_group_join.gf",
            "core/Queries/IntRange.gf", "core/Queries/FilteredIntRange.gf",
            "core/Queries/enumerable_elements.gf", "core/Queries/enumerable_count.gf"))
        .Append(new SourceFile("group-join.gf", code));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InferredCallsPreserveUnmatchedOutersAndRetainedGroups(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            using Standard::Collections;
            using Standard::Queries;

            int Key(int value) => value % 3;
            Group<int> Keep(int outer, Group<int> matches) => move matches;
            int main()
            {
                Vector<Group<int>> saved = new();
                {
                    var outer = new IntRange(0, 4).AsEnumerable();
                    var inner = new IntRange(0, 2).AsEnumerable();
                    var query = outer.GroupJoin(inner, Key, Key, Keep);
                    foreach (Group<int> values in query) saved.Add(move values);
                    if (saved.Count != 4) return 1;
                    if (Standard::Queries::GroupJoin(outer, inner, Key, Key, Keep).Count() != 4) return 2;
                }
                if (saved[0].Count != 1 || saved[1].Count != 1 || !saved[2].IsEmpty || saved[3].Count != 1) return 3;
                Group<int> duplicate = saved[0];
                saved.Clear();
                var iterator = duplicate.GetEnumerator();
                duplicate = new Group<int>();
                if (!iterator.MoveNext() || iterator.Current != 0 || iterator.MoveNext()) return 4;
                return 0;
            }
            """), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}\n{result.StandardOutput}\n{result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SharedOwnedGroupsAndMoveOnlyResultsCleanUpAllObservedFailures(int optimization)
    {
        var result = CompilerTestHelper.Run(StandardTestSources.Combine(Sources("""
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
            int rows = 0;
            int requests = 0;
            int budget = -1;
            int phase = 0;
            bool deny = false;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes)
                {
                    requests++;
                    if (budget == 0) { budget = -1; return null; }
                    if (budget > 0) budget--;
                    if (deny) return null;
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
            struct Row
            {
                public int Id;
                public Group<Item> Matches;
                public Row(int id, Group<Item> matches) { Id = id; Matches = move matches; rows++; }
                ~Row() { rows--; }
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
            Unique<Row> Keep(Item outer, Group<Item> matches)
            {
                Unique<Row> result = new(new* Row(outer.Id, move matches));
                if (phase == 3) throw new Exception("result failed");
                return move result;
            }
            void Run()
            {
                Vector<Unique<Row>> saved = new();
                {
                    Vector<Item> outer = new();
                    Vector<Item> inner = new();
                    for (int i = 0; i < 3; i++) outer.Add(new Item(i));
                    for (int i = 0; i < 4; i++) inner.Add(new Item(i));
                    var query = outer.AsEnumerable().GroupJoin(inner.AsEnumerable(), OuterKey, InnerKey, Keep);
                    foreach (Unique<Row> row in query) saved.Add(move row);
                    if (saved.Count != 3 || rows != 3 || items != 11 || keys != 0)
                        throw new Exception("wrong buffered state");
                }
                if (items != 4 || rows != 3 || keys != 0) throw new Exception("groups did not retain values");
                int before = requests;
                deny = true;
                for (nuint i = 0; i < 3; i++)
                {
                    Group<Item> group = saved[i].Value.Matches;
                    Group<Item> copy = group;
                    var iterator = copy.GetEnumerator();
                    group = new Group<Item>();
                    copy = new Group<Item>();
                    if (!iterator.MoveNext() || iterator.Current.Id != (int)(i % 2)) throw new Exception("wrong first");
                    var second = iterator;
                    if (!iterator.MoveNext() || iterator.Current.Id != (int)(i % 2) + 2
                        || second.Current.Id != (int)(i % 2)) throw new Exception("wrong iterator copy");
                    if (iterator.MoveNext()) throw new Exception("wrong extent");
                    if (saved[i].Value.Matches.AsEnumerable().Count() != 2) throw new Exception("wrong count");
                }
                deny = false;
                if (requests != before) throw new Exception("group copies allocated");
                Group<Item>::Enumerator retained = new(new Group<Item>());
                {
                    Group<Item> group = saved[0].Value.Matches;
                    var query = group.AsEnumerable();
                    retained = group.GetEnumerator();
                    group = new Group<Item>();
                    saved.Clear();
                    if (rows != 0 || items != 2 || query.Count() != 2) throw new Exception("query lost group");
                }
                if (items != 2 || !retained.MoveNext() || retained.Current.Id != 0
                    || !retained.MoveNext() || retained.Current.Id != 2 || retained.MoveNext())
                    throw new Exception("iterator lost group");
                retained = new Group<Item>::Enumerator(new Group<Item>());
                if (items != 0 || keys != 0 || rows != 0) throw new Exception("group owners leaked");
            }
            int main()
            {
                Run();
                if (live != 0 || items != 0 || keys != 0 || rows != 0) return 1;
                int total = requests;
                for (int failure = 0; failure < total; failure++)
                {
                    budget = failure;
                    bool caught = false;
                    try { Run(); }
                    catch (BadAllocException* error) { caught = true; }
                    budget = -1;
                    if (!caught || live != 0 || items != 0 || keys != 0 || rows != 0) return 2;
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
                    if (!caught || live != 0 || items != 0 || keys != 0 || rows != 0) return 9;
                }
                phase = 0;
                {
                    Unique<Row> saved = new();
                    {
                        Vector<Item> outer = new();
                        Vector<Item> inner = new();
                        for (int i = 0; i < 2; i++) outer.Add(new Item(i));
                        for (int i = 0; i < 3; i++) inner.Add(new Item(i));
                        var query = outer.AsEnumerable().GroupJoin(inner.AsEnumerable(), OuterKey, InnerKey, Keep);
                        var iterator = query.GetEnumerator();
                        if (!iterator.MoveNext()) return 10;
                        saved = iterator.Current;
                        if (!iterator.MoveNext()) return 11;
                        phase = 3;
                        bool caught = false;
                        try { Unique<Row> result = iterator.Current; }
                        catch (Exception* error) { caught = true; }
                        phase = 0;
                        if (!caught || iterator.MoveNext() || keys != 0 || rows != 1
                            || saved.Value.Matches.Count != 2) return 12;
                    }
                    if (items != 2 || rows != 1 || saved.Value.Id != 0) return 13;
                    var values = saved.Value.Matches.GetEnumerator();
                    saved = new Unique<Row>();
                    if (!values.MoveNext() || values.Current.Id != 0 || !values.MoveNext()
                        || values.Current.Id != 2 || values.MoveNext()) return 14;
                }
                if (live != 0 || items != 0 || keys != 0 || rows != 0) return 15;
                return 0;
            }
            """), StandardTestSources.Load("core/Memory/Unique.gf")), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}\n{result.StandardOutput}\n{result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void StableGroupsUnmatchedValuesAndReplayMatchLinq(int optimization)
    {
        string path = Path.Combine(Path.GetTempPath(), "gflat_group_join_" + Guid.NewGuid().ToString("N"));
        try
        {
            var random = new Random(639273);
            var cases = new List<(int[] Outer, int[] Inner)> { ([], []), ([], [1]), ([1], []), ([0, 0, 1], [0, 0]), ([-1, 0, 1], [-1, 0, 1]) };
            for (int i = 0; i < 256; i++)
                cases.Add((Enumerable.Range(0, random.Next(17)).Select(_ => random.Next(-12, 13)).ToArray(),
                    Enumerable.Range(0, random.Next(17)).Select(_ => random.Next(-12, 13)).ToArray()));
            using (var writer = new BinaryWriter(File.Create(path)))
            foreach (var (outer, inner) in cases)
            {
                int[][] expected = outer.GroupJoin(inner, value => value % 4, value => value % 4,
                    (_, values) => values.ToArray()).ToArray();
                writer.Write(outer.Length);
                writer.Write(inner.Length);
                foreach (int value in outer.Concat(inner)) writer.Write(value);
                foreach (int[] group in expected)
                {
                    writer.Write(group.Length);
                    foreach (int value in group) writer.Write(value);
                }
            }
            var result = CompilerTestHelper.Run(Sources($$"""
                using Standard::Collections;
                using Standard::Memory;
                using Standard::Queries;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" nuint fread(void* output, nuint size, nuint count, void* file);
                extern "C" int fclose(void* file);
                extern "C" int ferror(void* file);
                int Key(int value) => value % 4;
                Group<int> Keep(int outer, Group<int> matches) => move matches;
                int main()
                {
                    void*? file = fopen("{{path.Replace("\\", "\\\\")}}", "rb");
                    if (file == null) return 1;
                    defer { fclose(file!); }
                    int[2] header = default(int[2]);
                    int[16] first;
                    int[16] second;
                    int[16] expected;
                    while (true)
                    {
                        nuint bytes = fread(&header[0], 1, 8, file!);
                        if (bytes == 0) break;
                        if (bytes != 8) return 2;
                        nuint outerCount = (nuint)header[0];
                        nuint innerCount = (nuint)header[1];
                        if (fread(&first[0], 4, outerCount, file!) != outerCount) return 3;
                        if (fread(&second[0], 4, innerCount, file!) != innerCount) return 4;
                        var outer = new Span<int>(&first[0], outerCount).AsEnumerable();
                        var inner = new Span<int>(&second[0], innerCount).AsEnumerable();
                        var query = outer.GroupJoin(inner, Key, Key, Keep);
                        Vector<Group<int>> saved = new();
                        foreach (Group<int> group in query) saved.Add(move group);
                        if (saved.Count != outerCount) return 5;
                        nuint index = 0;
                        foreach (Group<int> group in query)
                        {
                            if (index >= saved.Count || group.Count != saved[index].Count) return 6;
                            var left = group.GetEnumerator();
                            var right = saved[index].GetEnumerator();
                            while (left.MoveNext())
                                if (!right.MoveNext() || left.Current != right.Current) return 7;
                            if (right.MoveNext()) return 8;
                            index++;
                        }
                        if (index != outerCount) return 9;
                        for (nuint i = 0; i < innerCount; i++) second[i] = -777;
                        for (nuint i = 0; i < outerCount; i++)
                        {
                            int count = 0;
                            if (fread(&count, 4, 1, file!) != 1) return 10;
                            if (fread(&expected[0], 4, (nuint)count, file!) != (nuint)count) return 11;
                            if (saved[i].Count != (nuint)count) return 12;
                            var iterator = saved[i].GetEnumerator();
                            for (nuint at = 0; at < (nuint)count; at++)
                                if (!iterator.MoveNext() || iterator.Current != expected[at]) return 13;
                            if (iterator.MoveNext()) return 14;
                        }
                    }
                    if (ferror(file!) != 0) return 15;
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
    public void EmptyOuterSkipsInnerAndEmptyInnerStillVisitsEveryOuter(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            using Standard::Queries;

            int innerFactories = 0;
            int outerReads = 0;
            int outerKeys = 0;
            int results = 0;
            bool rejectInner = true;
            struct OuterIterator
            {
                private int position;
                private int count;
                public OuterIterator(int length) { position = -1; count = length; }
                public bool MoveNext() { position++; return position < count; }
                public int Current { get { outerReads++; return position; } }
            }
            struct OuterSource
            {
                private int count;
                public OuterSource(int length) { count = length; }
                public OuterIterator GetEnumerator() => new OuterIterator(count);
            }
            struct InnerSource
            {
                public InnerSource() { }
                public IntRange::Enumerator GetEnumerator()
                {
                    innerFactories++;
                    if (rejectInner) throw new Exception("inner factory");
                    return new IntRange(0, 0).GetEnumerator();
                }
            }
            int OuterKey(int value) { outerKeys++; return value; }
            int InnerKey(int value) => value;
            int CountMatches(int outer, Group<int> matches)
            {
                results++;
                if (!matches.IsEmpty) throw new Exception("expected empty");
                return outer;
            }
            int main()
            {
                var inner = new Enumerable<int, InnerSource, IntRange::Enumerator>(new InnerSource());
                var empty = new Enumerable<int, OuterSource, OuterIterator>(new OuterSource(0));
                if (empty.GroupJoin(inner, OuterKey, InnerKey, CountMatches).Count() != 0
                    || innerFactories != 0 || outerReads != 0 || outerKeys != 0 || results != 0) return 1;
                rejectInner = false;
                var outer = new Enumerable<int, OuterSource, OuterIterator>(new OuterSource(3));
                var query = outer.GroupJoin(inner, OuterKey, InnerKey, CountMatches);
                int expected = 0;
                foreach (int value in query)
                {
                    if (value != expected) return 2;
                    expected++;
                }
                if (expected != 3 || innerFactories != 1 || outerReads != 3 || outerKeys != 3 || results != 3) return 3;
                rejectInner = true;
                var iterator = query.GetEnumerator();
                bool caught = false;
                try { iterator.MoveNext(); }
                catch (Exception* error) { caught = true; }
                if (!caught || iterator.MoveNext()) return 4;
                bool invalid = false;
                try { int value = iterator.Current; }
                catch (Exception* error) { invalid = true; }
                if (!invalid || outerReads != 3 || results != 3) return 5;
                return 0;
            }
            """), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}\n{result.StandardOutput}\n{result.StandardError}");
    }
}
