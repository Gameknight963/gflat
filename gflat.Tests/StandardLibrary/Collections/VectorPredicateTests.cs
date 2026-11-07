namespace gflat.Tests.Std.Collections;

public sealed class VectorPredicateTests
{
    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("VECTOR_PREDICATES.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AllPredicateOutcomesMatchListAndBorrowNoncopyableValues(int optimization)
    {
        string path = Path.Combine(Path.GetTempPath(), "gflat_vector_predicates_" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            for (int length = 0; length <= 8; length++)
            for (int mask = 0; mask < 1 << length; mask++)
            {
                var values = Enumerable.Range(0, length).ToList();
                bool Predicate(int value) => (mask & (1 << value)) != 0;
                int Fix(int index) => index < 0 ? length : index;
                foreach (int item in new[] { length, mask, Fix(values.FindIndex(Predicate)), Fix(values.FindLastIndex(Predicate)),
                    values.TrueForAll(Predicate) ? 1 : 0, Fix(values.FindIndex(i => !Predicate(i))) }) writer.Write(item);
            }
            string literal = "\"" + path.Replace("\\", "\\\\") + "\"";
            var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
                StandardTestSources.Load("core/Collections/vector_predicates.gf"))
                .Append(new SourceFile("vector-predicates.gf", $$"""
                using Standard;
                using Standard::Collections;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" nuint fread(void* buffer, nuint size, nuint count, void* file);
                extern "C" int fclose(void* file);
                extern "C" int ferror(void* file);
                int mask = 0;
                int calls = 0;
                bool fail = false;
                struct Item
                {
                    public int Key;
                    public Item(int key) { Key = key; }
                    ~Item() { }
                }
                bool Predicate(immutable(Item)* value)
                {
                    calls++;
                    if (fail) throw new Exception("predicate failed");
                    return (mask & (1 << value.Key)) != 0;
                }
                int main()
                {
                    void*? file = fopen({{literal}}, "rb");
                    if (file == null) return 1;
                    defer { fclose(file!); }
                    int[6] row;
                    while (true)
                    {
                        nuint received = fread(&row[0], 1, 24, file!);
                        if (received == 0) break;
                        if (received != 24) return 2;
                        Vector<Item> values = new();
                        for (int i = 0; i < row[0]; i++) values.Add(new Item(i));
                        mask = row[1];
                        calls = 0;
                        nuint first = values.FindIndex(Predicate);
                        int forwardCalls = row[0];
                        if (row[2] != row[0]) forwardCalls = row[2] + 1;
                        if (first != (nuint)row[2] || calls != forwardCalls) return 3;
                        calls = 0;
                        if (values.FindLastIndex(Predicate) != (nuint)row[3]) return 4;
                        int reverseCalls = row[0];
                        if (row[3] != row[0]) reverseCalls = row[0] - row[3];
                        if (calls != reverseCalls) return 5;
                        calls = 0;
                        immutable(Item)*? found = values.Find(Predicate);
                        if (calls != forwardCalls) return 6;
                        if (first == values.Count) { if (found != null) return 7; }
                        else if (found != &values[first]) return 8;
                        calls = 0;
                        found = FindLast<Item>(&values, Predicate);
                        if (calls != reverseCalls) return 9;
                        if (row[3] == row[0]) { if (found != null) return 10; }
                        else if (found != &values[(nuint)row[3]]) return 11;
                        calls = 0;
                        if (values.Exists(Predicate) != (row[2] != row[0]) || calls != forwardCalls) return 12;
                        calls = 0;
                        if (values.TrueForAll(Predicate) != (row[4] != 0)) return 13;
                        int allCalls = row[0];
                        if (row[5] != row[0]) allCalls = row[5] + 1;
                        if (calls != allCalls) return 14;
                        if (values.Count != 0)
                        {
                            fail = true;
                            bool caught = false;
                            nuint output = 99;
                            try { output = values.FindIndex(Predicate); }
                            catch (Exception* error) { caught = true; }
                            fail = false;
                            if (!caught || output != 99 || values[0].Key != 0) return 15;
                        }
                    }
                    if (ferror(file!) != 0) return 16;
                    return 0;
                }
                """)), optimization);
            Assert.True(result.ExitCode == 0, $"Vector predicate exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }
        finally { File.Delete(path); }
    }
}
