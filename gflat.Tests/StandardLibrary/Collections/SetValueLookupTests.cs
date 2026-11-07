namespace gflat.Tests.Std.Collections;

public sealed partial class SetValueLookupTests
{
    private static IEnumerable<SourceFile> Sources(string program, bool sorted)
        => StandardTestSources.Combine(StandardTestSources.Values, sorted
            ? StandardTestSources.Load("core/Collections/SortedMap.gf", "core/Collections/SortedSet.gf", "core/Collections/sorted_set_lookup.gf")
            : StandardTestSources.Load("core/Collections/HashMap.gf", "core/Collections/HashSet.gf", "core/Collections/hash_set_lookup.gf"))
            .Append(new SourceFile("set-value-lookup.gf", program));

    private static void Run(string program, bool sorted, int optimization)
    {
        var result = CompilerTestHelper.Run(Sources(program, sorted), optimization);
        Assert.True(result.ExitCode == 0, $"Set value lookup exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("SET_VALUE_LOOKUP.md", 0);

    private sealed record Row(int Id, int Payload);
    private sealed class RowEquality : IEqualityComparer<Row>
    {
        public bool Equals(Row? left, Row? right) => left?.Id == right?.Id;
        public int GetHashCode(Row value) => 0;
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 2)]
    public void StoredRepresentativesMatchDotNetAcrossDuplicatesAndMissingKeys(bool sorted, int optimization)
    {
        var random = new Random(827194);
        string path = Path.Combine(Path.GetTempPath(), "gflat_set_lookup_" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            for (int sample = 0; sample < 64; sample++)
            {
                Row[] input = Enumerable.Range(0, sample == 0 ? 0 : random.Next(1, 40)).Select(index => new Row(random.Next(-5, 6), index)).ToArray();
                var hash = new HashSet<Row>(new RowEquality());
                var tree = new SortedSet<Row>(Comparer<Row>.Create((a, b) => a.Id.CompareTo(b.Id)));
                foreach (Row row in input) { hash.Add(row); tree.Add(row); }
                writer.Write(input.Length);
                foreach (Row row in input) { writer.Write(row.Id); writer.Write(row.Payload); }
                writer.Write(sorted ? tree.Count : hash.Count);
                writer.Write(tree.Min?.Id ?? 0);
                writer.Write(tree.Max?.Id ?? 0);
                for (int key = -6; key <= 6; key++)
                {
                    bool found = sorted ? tree.TryGetValue(new Row(key, -1), out Row? actual) : hash.TryGetValue(new Row(key, -1), out actual);
                    writer.Write(key); writer.Write(found ? 1 : 0); writer.Write(found ? actual!.Payload : -77);
                }
            }
            string literal = "\"" + path.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            string kind = sorted ? "SortedSet" : "HashSet";
            string constructor = sorted ? "new(Compare)" : "new()";
            string endpoints = sorted ? "if (distinct != 0 && (view.Min.Id != minimum || view.Max.Id != maximum)) return 12;" : "";
            Run($$"""
                using Standard;
                using Standard::Collections;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" nuint fread(void* buffer, nuint size, nuint count, void* file);
                extern "C" int fclose(void* file);
                extern "C" int fgetc(void* file);
                struct Row : IHashable, IEquatable<Row>
                {
                    public int Id;
                    public int Payload;
                    public Row(int id, int payload) { Id = id; Payload = payload; }
                    public immutable nuint GetHashCode() => 0;
                    public immutable bool Equals(immutable(Row)* other) => Id == other.Id;
                }
                int Compare(immutable(Row)* left, immutable(Row)* right) => left.Id - right.Id;
                int main()
                {
                    void*? file = fopen({{literal}}, "rb");
                    if (file == null) return 1;
                    defer { fclose(file!); }
                    for (int sample = 0; sample < 64; sample++)
                    {
                        int count = 0;
                        if (fread(&count, 4, 1, file!) != 1) return 2;
                        {{kind}}<Row> values = {{constructor}};
                        for (int i = 0; i < count; i++)
                        {
                            int[2] row = default(int[2]);
                            if (fread(&row[0], 4, 2, file!) != 2) return 3;
                            values.Add(new Row(row[0], row[1]));
                        }
                        int distinct = 0;
                        if (fread(&distinct, 4, 1, file!) != 1 || values.Count != (nuint)distinct) return 4;
                        immutable({{kind}}<Row>)* view = &values;
                        int minimum = 0;
                        int maximum = 0;
                        if (fread(&minimum, 4, 1, file!) != 1 || fread(&maximum, 4, 1, file!) != 1) return 13;
                        {{endpoints}}
                        for (int i = 0; i < 13; i++)
                        {
                            int[3] row = default(int[3]);
                            if (fread(&row[0], 4, 3, file!) != 3) return 5;
                            Row query = new(row[0], -1);
                            Row output = new(99, -77);
                            if (view.TryGetValue(&query, ref output) != (row[1] != 0) || output.Payload != row[2]) return 6;
                            if (row[1] != 0 && output.Id != query.Id) return 7;
                            if (row[1] == 0 && output.Id != 99) return 8;
                            output = new Row(99, -77);
                            if (TryGetValue(view, &query, ref output) != (row[1] != 0) || output.Payload != row[2]) return 9;
                            if (values.Count != (nuint)distinct) return 10;
                        }
                    }
                    if (fgetc(file!) != -1) return 11;
                    return 0;
                }
                """, sorted, optimization);
        }
        finally { File.Delete(path); }
    }
}
