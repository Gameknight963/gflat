namespace gflat.Tests.Std.Queries;

public sealed class GroupByTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void GuideExamplesCompile(int index) => StandardLibraryGuideTests.CheckProgram("QUERY_GROUP_BY.md", index);

    private static IEnumerable<SourceFile> Sources(string code) => StandardTestSources.Combine(StandardTestSources.Values,
        StandardTestSources.Load("core/Threading/Interlocked.gf", "core/Memory/SharedStorage.gf",
            "core/Collections/HashMap.gf", "core/Queries/Grouping.gf", "core/Queries/GroupBySource.gf",
            "core/Queries/enumerable_group_by.gf", "core/Queries/enumerable_count.gf",
            "core/Queries/enumerable_elements.gf"))
        .Append(new SourceFile("query-group-by.gf", code));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InferredCallsPreserveFirstKeyOrderAndRetainedValues(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            using Standard::Collections;
            using Standard::Memory;
            using Standard::Queries;

            int keyCalls = 0;
            int valueCalls = 0;
            int Key(int value) { keyCalls++; return value % 3; }
            int Project(int value) { valueCalls++; return value * 10; }
            int main()
            {
                Vector<Grouping<int, int>> saved = new();
                {
                    int[6] input = [2, 1, 5, 0, 4, 3];
                    var source = new Span<int>(&input[0], 6).AsEnumerable();
                    var query = source.GroupBy(Key);
                    if (keyCalls != 0) return 1;
                    foreach (Grouping<int, int> group in query) saved.Add(move group);
                    if (keyCalls != 6 || saved.Count != 3) return 2;
                    if (Standard::Queries::GroupBy(source, Key, Project).Count() != 3 || valueCalls != 6) return 3;
                }
                int[3] keys = [2, 1, 0];
                int[6] expected = [2, 5, 1, 4, 0, 3];
                for (nuint i = 0; i < 3; i++)
                {
                    if (saved[i].Key != keys[i] || saved[i].Count != 2) return 4;
                    var iterator = saved[i].GetEnumerator();
                    if (!iterator.MoveNext() || iterator.Current != expected[i * 2]
                        || !iterator.MoveNext() || iterator.Current != expected[i * 2 + 1] || iterator.MoveNext()) return 5;
                }
                Grouping<int, int> retained = saved[0];
                var values = retained.GetEnumerator();
                saved.Clear();
                if (retained.Key != 2 || !values.MoveNext() || values.Current != 2) return 6;
                return 0;
            }
            """), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}\n{result.StandardOutput}\n{result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NoncopyableCanonicalKeysAndMoveOnlyElementsOutliveEveryObservedFailure(int optimization)
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
                public Item(int id) { Id = id; text = s"item"; items++; }
                public copy Item(immutable(Item)* source)
                {
                    Id = source.Id;
                    text = source.text;
                    if (phase == 3) throw new Exception("copy failed");
                    items++;
                }
                ~Item() { items--; }
            }
            struct Key : IHashable, IEquatable<Key>
            {
                public int Representative;
                private String text;
                public Key(int value) { Representative = value; text = s"key"; keys++; }
                public immutable nuint GetHashCode()
                {
                    if (phase == 4) throw new Exception("hash failed");
                    return 1;
                }
                public immutable bool Equals(immutable(Key)* other)
                {
                    if (phase == 5) throw new Exception("equality failed");
                    return Representative % 2 == other.Representative % 2;
                }
                ~Key() { keys--; }
            }
            Key MakeKey(Item value)
            {
                if (phase == 1) throw new Exception("key failed");
                return new Key(value.Id);
            }
            Unique<Item> Project(Item value)
            {
                Unique<Item> result = new(new* Item(value.Id * 10));
                if (phase == 2) throw new Exception("element failed");
                return move result;
            }
            void Run()
            {
                Vector<Grouping<Key, Item>> copied = new();
                Vector<Grouping<Key, Unique<Item>>> projected = new();
                {
                    Vector<Item> input = new();
                    int[6] ids = [2, 1, 4, 3, 0, 5];
                    for (nuint i = 0; i < 6; i++) input.Add(new Item(ids[i]));
                    foreach (Grouping<Key, Item> group in input.AsEnumerable().GroupBy(MakeKey)) copied.Add(move group);
                    foreach (Grouping<Key, Unique<Item>> group in input.AsEnumerable().GroupBy(MakeKey, Project)) projected.Add(move group);
                    if (items != 18 || keys != 4 || copied.Count != 2 || projected.Count != 2)
                        throw new Exception("wrong buffered ownership");
                }
                if (items != 12 || keys != 4) throw new Exception("groups lost source values");
                int before = requests;
                deny = true;
                for (nuint i = 0; i < 2; i++)
                {
                    Grouping<Key, Unique<Item>> group = projected[i];
                    Grouping<Key, Unique<Item>> copy = group;
                    if (copy.Key.Representative != 2 - (int)i || copy.Count != 3) throw new Exception("wrong canonical key");
                    var values = copy.GetEnumerator();
                    int[3] expected = [20, 40, 0];
                    if (i == 1) { expected[0] = 10; expected[1] = 30; expected[2] = 50; }
                    for (nuint j = 0; j < 3; j++)
                        if (!values.MoveNext() || values.Current.ValueImmutable.Id != expected[j]) throw new Exception("wrong projection");
                    if (values.MoveNext() || copy.AsEnumerable().Count() != 3) throw new Exception("wrong extent");
                }
                deny = false;
                if (before != requests) throw new Exception("grouping copies allocated");
                Unique<Grouping<Key, Unique<Item>>::Enumerator> retained = new();
                {
                    Grouping<Key, Unique<Item>> group = projected[0];
                    immutable(Key)* key = &group.Key;
                    retained = new Unique<Grouping<Key, Unique<Item>>::Enumerator>(new* Grouping<Key, Unique<Item>>::Enumerator(move group));
                    copied.Clear();
                    projected.Clear();
                    if (keys != 1 || items != 3 || key.Representative != 2) throw new Exception("iterator lost key owner");
                }
                if (!retained.Value.MoveNext() || retained.Value.Current.ValueImmutable.Id != 20) throw new Exception("iterator lost values");
                retained = new Unique<Grouping<Key, Unique<Item>>::Enumerator>();
                if (items != 0 || keys != 0) throw new Exception("group owners leaked");
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
                for (phase = 1; phase <= 5; phase++)
                {
                    bool caught = false;
                    try { Run(); }
                    catch (Exception* error)
                    {
                        caught = true;
                        if (phase == 1 && !CString::Equals(error.GetMessage(), "key failed")) return 3;
                        if (phase == 2 && !CString::Equals(error.GetMessage(), "element failed")) return 4;
                        if (phase == 3 && !CString::Equals(error.GetMessage(), "copy failed")) return 5;
                        if (phase == 4 && !CString::Equals(error.GetMessage(), "hash failed")) return 6;
                        if (phase == 5 && !CString::Equals(error.GetMessage(), "equality failed")) return 7;
                    }
                    if (!caught || live != 0 || items != 0 || keys != 0) return 8;
                }
                return 0;
            }
            """), StandardTestSources.Load("core/Memory/Unique.gf")), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}\n{result.StandardOutput}\n{result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void GroupOrderValuesProjectionsAndReplayMatchLinq(int optimization)
    {
        string path = Path.Combine(Path.GetTempPath(), "gflat_group_by_" + Guid.NewGuid().ToString("N"));
        try
        {
            var random = new Random(529163);
            List<int[]> cases = [[], [1], [2, 1, 5, 0, 4, 3], [-1, 0, 1], [0, 0, 0]];
            for (int i = 0; i < 256; i++)
                cases.Add(Enumerable.Range(0, random.Next(33)).Select(_ => random.Next(-20, 21)).ToArray());
            using (var writer = new BinaryWriter(File.Create(path)))
            foreach (int[] input in cases)
            {
                var expected = input.GroupBy(value => value % 5).ToArray();
                writer.Write(input.Length);
                writer.Write(expected.Length);
                foreach (int value in input) writer.Write(value);
                foreach (var group in expected)
                {
                    writer.Write(group.Key);
                    writer.Write(group.Count());
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
                int Key(int value) => value % 5;
                int Project(int value) => value * value - 3;
                int main()
                {
                    void*? file = fopen("{{path.Replace("\\", "\\\\")}}", "rb");
                    if (file == null) return 1;
                    defer { fclose(file!); }
                    int[2] header = default(int[2]);
                    int[32] input;
                    int[32] expected;
                    while (true)
                    {
                        nuint bytes = fread(&header[0], 1, 8, file!);
                        if (bytes == 0) break;
                        if (bytes != 8) return 2;
                        nuint length = (nuint)header[0];
                        nuint count = (nuint)header[1];
                        if (fread(&input[0], 4, length, file!) != length) return 3;
                        var source = new Span<int>(&input[0], length).AsEnumerable();
                        var query = source.GroupBy(Key);
                        Vector<Grouping<int, int>> saved = new();
                        Vector<Grouping<int, int>> projected = new();
                        foreach (Grouping<int, int> group in query) saved.Add(move group);
                        foreach (Grouping<int, int> group in source.GroupBy(Key, Project)) projected.Add(move group);
                        if (saved.Count != count || projected.Count != count) return 4;
                        nuint index = 0;
                        foreach (Grouping<int, int> group in query)
                        {
                            if (index >= count || group.Key != saved[index].Key || group.Count != saved[index].Count) return 5;
                            index++;
                        }
                        if (index != count) return 6;
                        for (nuint i = 0; i < length; i++) input[i] = -999;
                        for (nuint i = 0; i < count; i++)
                        {
                            if (fread(&header[0], 4, 2, file!) != 2) return 7;
                            int key = header[0];
                            nuint values = (nuint)header[1];
                            if (fread(&expected[0], 4, values, file!) != values) return 8;
                            if (saved[i].Key != key || projected[i].Key != key || saved[i].Count != values || projected[i].Count != values) return 9;
                            var first = saved[i].GetEnumerator();
                            var second = projected[i].GetEnumerator();
                            for (nuint at = 0; at < values; at++)
                                if (!first.MoveNext() || !second.MoveNext() || first.Current != expected[at]
                                    || second.Current != Project(expected[at])) return 10;
                            if (first.MoveNext() || second.MoveNext()) return 11;
                        }
                    }
                    if (ferror(file!) != 0) return 12;
                    return 0;
                }
                """), optimization);
            Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}\n{result.StandardOutput}\n{result.StandardError}");
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("group.Key = 9;")]
    [InlineData("var iterator = group.GetEnumerator(); if (iterator.MoveNext()) iterator.Current = 9;")]
    public void KeysAndElementReferencesAreImmutable(string statement)
    {
        var error = Assert.Throws<gflat.CompileExceptions.TypeCheckException>(() => Compiler.Emit(Sources($$"""
            using Standard::Queries;

            void Change(Grouping<int, int>* group) { {{statement}} }
            int main() => 0;
            """)));
        Assert.Contains("readonly", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BufferingIsDeferredAndFactoryIteratorAndSelectorErrorsEndTheCursor(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            using Standard::Queries;

            int factories = 0;
            int reads = 0;
            int moves = 0;
            int closes = 0;
            int keyCalls = 0;
            int projections = 0;
            int phase = 0;
            struct Iterator
            {
                private int position;
                public Iterator() { position = -1; }
                public bool MoveNext()
                {
                    moves++;
                    position++;
                    if (phase == 2 && position == 1) throw new Exception("move failed");
                    return position < 4;
                }
                public int Current
                {
                    get
                    {
                        reads++;
                        if (phase == 3 && position == 1) throw new Exception("read failed");
                        return position;
                    }
                }
                ~Iterator() { closes++; }
            }
            struct Source
            {
                public Source() { }
                public Iterator GetEnumerator()
                {
                    factories++;
                    if (phase == 1) throw new Exception("factory failed");
                    return new Iterator();
                }
            }
            int Key(int value)
            {
                keyCalls++;
                if (phase == 4 && value == 1) throw new Exception("key failed");
                return value % 2;
            }
            int Project(int value)
            {
                projections++;
                if (phase == 5 && value == 1) throw new Exception("element failed");
                return value * 10;
            }
            int main()
            {
                var query = new Enumerable<int, Source, Iterator>(new Source()).GroupBy(Key, Project);
                {
                    var iterator = query.GetEnumerator();
                    if (factories != 0 || reads != 0 || keyCalls != 0 || projections != 0) return 1;
                    bool invalid = false;
                    try { Grouping<int, int> value = iterator.Current; }
                    catch (Exception* error) { invalid = true; }
                    if (!invalid || !iterator.MoveNext() || factories != 1 || reads != 4 || moves != 5
                        || keyCalls != 4 || projections != 4 || closes != 1) return 2;
                    Grouping<int, int> first = iterator.Current;
                    if (iterator.Current.Key != 0 || first.Key != 0 || first.Count != 2 || keyCalls != 4) return 3;
                    var moved = move iterator;
                    if (!moved.MoveNext() || moved.Current.Key != 1 || moved.MoveNext()) return 4;
                    if (first.Key != 0 || first.AsEnumerable().Count() != 2) return 5;
                }
                for (phase = 1; phase <= 5; phase++)
                {
                    int before = factories;
                    int closed = closes;
                    var iterator = query.GetEnumerator();
                    if (factories != before) return 6;
                    bool caught = false;
                    try { iterator.MoveNext(); }
                    catch (Exception* error) { caught = true; }
                    if (!caught || iterator.MoveNext() || factories != before + 1) return 7;
                    if (phase == 1 && closes != closed) return 8;
                    if (phase != 1 && closes != closed + 1) return 9;
                    bool invalid = false;
                    try { Grouping<int, int> value = iterator.Current; }
                    catch (Exception* error) { invalid = true; }
                    if (!invalid) return 10;
                }
                return 0;
            }
            """), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}\n{result.StandardOutput}\n{result.StandardError}");
    }
}
