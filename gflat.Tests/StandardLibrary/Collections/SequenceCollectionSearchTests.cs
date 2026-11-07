namespace gflat.Tests.Std.Collections;

public sealed class SequenceCollectionSearchTests
{
    private static void Run(string program, int optimization)
    {
        var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/Collections/Deque.gf", "core/Collections/EqualityComparer.gf",
                "core/Collections/vector_search.gf", "core/Collections/deque_search.gf"))
            .Append(new SourceFile("sequence-collection-search.gf", program)), optimization);
        Assert.True(result.ExitCode == 0, $"Collection search exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("SEQUENCE_COLLECTION_SEARCH.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SearchAndRemovalMatchListIncludingWrappedDequeStorage(int optimization)
    {
        string path = Path.Combine(Path.GetTempPath(), "gflat_collection_search_" + Guid.NewGuid().ToString("N"));
        try
        {
            var random = new Random(291834);
            using (var writer = new BinaryWriter(File.Create(path)))
            for (int row = 0; row < 256; row++)
            {
                var list = Enumerable.Range(0, row % 33).Select(_ => random.Next(-6, 7)).ToList();
                int key = random.Next(-8, 9);
                int first = list.IndexOf(key), last = list.LastIndexOf(key);
                writer.Write((long)list.Count); writer.Write((long)key);
                writer.Write(first < 0 ? (long)list.Count : first); writer.Write(last < 0 ? (long)list.Count : last);
                foreach (int value in list) writer.Write((long)value);
            }
            string literal = "\"" + path.Replace("\\", "\\\\") + "\"";
            Run($$"""
                using Standard;
                using Standard::Collections;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" nuint fread(void* buffer, nuint size, nuint count, void* file);
                extern "C" int fclose(void* file);
                int main()
                {
                    void*? file = fopen({{literal}}, "rb");
                    if (file == null) return 1;
                    defer { fclose(file!); }
                    for (int row = 0; row < 256; row++)
                    {
                        long[4] header = default(long[4]);
                        long[32] data = default(long[32]);
                        if (fread(&header[0], 8, 4, file!) != 4) return 2;
                        nuint count = (nuint)header[0];
                        if (fread(&data[0], 8, count, file!) != count) return 3;
                        Vector<long> vector = new();
                        Deque<long> deque = new();
                        deque.Reserve(count + 3);
                        for (nuint i = 0; i < count; i++) vector.Add(data[i]);
                        for (nuint i = count / 2; i < count; i++) deque.AddLast(data[i]);
                        for (nuint remaining = count / 2; remaining != 0; remaining--) deque.AddFirst(data[remaining - 1]);
                        long key = header[1];
                        if (vector.IndexOf(&key) != (nuint)header[2] || IndexOf(&deque, &key) != (nuint)header[2]) return 4;
                        if (LastIndexOf(&vector, &key) != (nuint)header[3] || deque.LastIndexOf(&key) != (nuint)header[3]) return 5;
                        bool found = header[2] != header[0];
                        if (vector.Contains(&key) != found || deque.Contains(&key) != found) return 6;
                        if (vector.RemoveValue(&key) != found || RemoveValue(&deque, &key) != found) return 7;
                        nuint expectedCount = count;
                        if (found) expectedCount--;
                        if (vector.Count != expectedCount || deque.Count != expectedCount) return 8;
                        for (nuint i = 0; i < expectedCount; i++)
                        {
                            nuint source = i;
                            if (found && i >= (nuint)header[2]) source++;
                            if (vector[i] != data[source] || deque[i] != data[source]) return 9;
                        }
                    }
                    return 0;
                }
                """, optimization);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CustomSearchBorrowsNoncopyableOwnersAndComparisonFailuresDoNotRemoveAnything(int optimization)
        => Run("""
            using Standard;
            using Standard::Collections;

            int live = 0;
            int calls = 0;
            int order = 0;
            int failAt = 0;
            struct Owner
            {
                public int Id;
                public Owner(int id) { Id = id; live++; }
                ~Owner() { live--; }
            }
            bool Equal(immutable(Owner)* left, immutable(Owner)* right)
            {
                calls++; order = order * 10 + left.Id;
                if (calls == failAt) throw new Exception("comparison failed");
                return left.Id == right.Id;
            }
            int main()
            {
                {
                    Vector<Owner> vector = new();
                    Deque<Owner> deque = new();
                    for (int i = 1; i <= 3; i++) { vector.Add(new Owner(i)); deque.AddLast(new Owner(i)); }
                    Owner key = new(99);
                    for (int position = 1; position <= 3; position++)
                    {
                        failAt = position;
                        for (int form = 0; form < 2; form++)
                        {
                            calls = 0; order = 0;
                            bool caught = false;
                            try
                            {
                                if (form == 0) vector.RemoveValue(&key, Equal);
                                else deque.RemoveValue(&key, Equal);
                            }
                            catch (Exception* error) { caught = true; }
                            if (!caught || calls != position || vector.Count != 3 || deque.Count != 3 || live != 7) return 1;
                            if (vector[0].Id != 1 || vector[1].Id != 2 || vector[2].Id != 3 || deque[0].Id != 1 || deque[1].Id != 2 || deque[2].Id != 3) return 2;
                        }
                    }
                    failAt = 0; calls = 0; order = 0;
                    if (vector.LastIndexOf(&vector[0], Equal) != 0 || calls != 3 || order != 321) return 3;
                    calls = 0; order = 0;
                    if (deque.IndexOf(&deque[2], Equal) != 2 || calls != 3 || order != 123) return 4;
                    if (!vector.RemoveValue(&vector[1], Equal) || !deque.RemoveValue(&deque[1], Equal)) return 5;
                    if (live != 5 || vector[1].Id != 3 || deque[1].Id != 3) return 6;
                    if (vector.RemoveValue(&key, Equal) || deque.RemoveValue(&key, Equal) || live != 5) return 7;
                    vector.Clear(); deque.Clear(); calls = 0;
                    if (vector.Contains(&key, Equal) || deque.Contains(&key, Equal) || calls != 0) return 8;
                }
                return live;
            }
            """, optimization);
}
