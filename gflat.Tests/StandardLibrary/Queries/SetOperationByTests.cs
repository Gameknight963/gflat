namespace gflat.Tests.Std.Queries;

public sealed partial class SetOperationByTests
{
    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("QUERY_SET_OPERATIONS_BY.md", 0);

    private static IEnumerable<SourceFile> Sources(string program)
        => StandardTestSources.Combine(StandardTestSources.Values, StandardTestSources.Load(
            "core/Collections/HashMap.gf", "core/Collections/HashSet.gf", "core/Collections/EqualityComparer.gf",
            "core/Collections/default_equality.gf", "core/Queries/ConcatSource.gf", "core/Queries/DistinctSource.gf",
            "core/Queries/DistinctBySource.gf", "core/Queries/UnionBySource.gf", "core/Queries/SetFilterBySource.gf", "core/Queries/enumerable_union_by.gf",
            "core/Queries/enumerable_except_by.gf", "core/Queries/enumerable_intersect_by.gf", "core/Queries/enumerable_to_vector.gf"))
            .Append(new SourceFile("query-sets.gf", program));

    internal static void Run(string program, int optimization)
    {
        var result = CompilerTestHelper.Run(Sources(program), optimization);
        Assert.True(result.ExitCode == 0, $"Set operation exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void StableSetResultsMatchLinqAcrossEmptyDuplicateBoundaryAndRandomInputs(int optimization)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat_query_sets_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "oracle.bin");
            using (var writer = new BinaryWriter(File.Create(path)))
            foreach (var (first, second) in Cases())
            foreach (var sequence in new[] { first, second, first.UnionBy(second, value => value % 7).ToArray(), first.ExceptBy(second.Select(value => value % 7), value => value % 7).ToArray(), first.IntersectBy(second.Select(value => value % 7), value => value % 7).ToArray() })
            {
                writer.Write((ulong)sequence.Length);
                foreach (int value in sequence) writer.Write(value);
            }
            Run($$"""
                using Standard;
                using Standard::Collections;
                using Standard::Memory;
                using Standard::Queries;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" nuint fread(void* buffer, nuint size, nuint count, void* file);
                extern "C" int fgetc(void* file);
                extern "C" int fclose(void* file);
                Vector<int> Read(void* input)
                {
                    ulong count = 0ul;
                    if (fread(&count, (nuint)8, (nuint)1, input) != (nuint)1) throw new Exception("oracle header");
                    Vector<int> result = new();
                    result.Reserve((nuint)count);
                    for (ulong index = 0ul; index < count; index++)
                    {
                        int value = 0;
                        if (fread(&value, (nuint)4, (nuint)1, input) != (nuint)1) throw new Exception("oracle item");
                        result.Add(value);
                    }
                    return move result;
                }
                bool Equal(immutable(Vector<int>)* actual, immutable(Vector<int>)* expected)
                {
                    if (actual.Count != expected.Count) return false;
                    for (nuint i = 0; i < actual.Count; i++) if ((*actual)[i] != (*expected)[i]) return false;
                    return true;
                }
                int selections = 0;
                int Key(int value) { selections++; return value % 7; }
                int main()
                {
                    void*? input = fopen({{Literal(path)}}, "rb");
                    if (input == null) return 90;
                    defer { fclose(input!); }
                    for (int test = 0; test < {{Cases().Count()}}; test++)
                    {
                        Vector<int> first = Read(input!);
                        Vector<int> second = Read(input!);
                        Vector<int> expectedUnion = Read(input!);
                        Vector<int> expectedExcept = Read(input!);
                        Vector<int> expectedIntersect = Read(input!);
                        Vector<int> keys = new();
                        for (nuint i = 0; i < second.Count; i++) keys.Add(second[i] % 7);
                        selections = 0;
                        var union = first.AsEnumerable().UnionBy(second.AsSpan().AsEnumerable(), Key);
                        var except = first.AsEnumerable().ExceptBy(keys.AsSpan().AsEnumerable(), Key);
                        var intersect = first.AsEnumerable().IntersectBy(keys.AsSpan().AsEnumerable(), Key);
                        if (selections != 0) return 5;
                        for (int pass = 0; pass < 2; pass++)
                        {
                            selections = 0;
                            Vector<int> a = union.ToVector();
                            if ((nuint)selections != first.Count + second.Count) return 6;
                            selections = 0;
                            Vector<int> b = except.ToVector();
                            if ((nuint)selections != first.Count) return 7;
                            selections = 0;
                            Vector<int> c = intersect.ToVector();
                            if ((nuint)selections != first.Count) return 8;
                            if (!Equal(&a, &expectedUnion)) return 1;
                            if (!Equal(&b, &expectedExcept)) return 2;
                            if (!Equal(&c, &expectedIntersect)) return 3;
                        }
                        Vector<int> freeUnion = UnionBy(first.AsEnumerable(), second.AsSpan().AsEnumerable(), Key).ToVector();
                        Vector<int> freeExcept = ExceptBy(first.AsEnumerable(), keys.AsSpan().AsEnumerable(), Key).ToVector();
                        Vector<int> freeIntersect = IntersectBy(first.AsEnumerable(), keys.AsSpan().AsEnumerable(), Key).ToVector();
                        if (!Equal(&freeUnion, &expectedUnion) || !Equal(&freeExcept, &expectedExcept) || !Equal(&freeIntersect, &expectedIntersect)) return 4;
                    }
                    if (fgetc(input!) != -1) return 91;
                    return 0;
                }
                """, optimization);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NonhashableRowsAndSelectorFailuresUseKeysAndEndFailedEnumerators(int optimization)
    {
        Run("""
            using Standard;
            using Standard::Collections;
            using Standard::Memory;
            using Standard::Queries;

            struct Row { public int Id; public int Payload; }
            int calls = 0;
            int failAt = 0;
            int Select(Row row)
            {
                calls++;
                if (calls == failAt) throw new Exception("selector", 42);
                return row.Id;
            }
            int main()
            {
                Row[4] rows = default(Row[4]);
                rows[0].Id = 2; rows[0].Payload = 20;
                rows[1].Id = 1; rows[1].Payload = 10;
                rows[2].Id = 2; rows[2].Payload = 99;
                rows[3].Id = 3; rows[3].Payload = 30;
                int[2] ids = [2, 3];
                Span<Row> input = new(rows, 4);
                Span<int> keys = new(ids, 2);
                Vector<Row> union = input.AsEnumerable().UnionBy(input.AsEnumerable(), Select).ToVector();
                if (union.Count != 3 || union[0].Payload != 20 || union[1].Payload != 10 || union[2].Payload != 30) return 1;
                Vector<Row> except = input.AsEnumerable().ExceptBy(keys.AsEnumerable(), Select).ToVector();
                if (except.Count != 1 || except[0].Payload != 10) return 2;
                Vector<Row> intersect = input.AsEnumerable().IntersectBy(keys.AsEnumerable(), Select).ToVector();
                if (intersect.Count != 2 || intersect[0].Payload != 20 || intersect[1].Payload != 30) return 3;
                for (int operation = 0; operation < 3; operation++)
                {
                    calls = 0;
                    failAt = 2;
                    int caught = 0;
                    if (operation == 0)
                    {
                        var iterator = input.AsEnumerable().UnionBy(input.AsEnumerable(), Select).GetEnumerator();
                        try { while (iterator.MoveNext()) {} }
                        catch (Exception* error) { caught = error.GetCode(); }
                        if (iterator.MoveNext() || calls != 2) return 4;
                        bool invalid = false;
                        try { Row value = iterator.Current; }
                        catch (Exception* error) { invalid = true; }
                        if (!invalid) return 5;
                    }
                    else
                    {
                        var iterator = input.AsEnumerable().ExceptBy(keys.AsEnumerable(), Select).GetEnumerator();
                        if (operation == 2) iterator = input.AsEnumerable().IntersectBy(keys.AsEnumerable(), Select).GetEnumerator();
                        try { while (iterator.MoveNext()) {} }
                        catch (Exception* error) { caught = error.GetCode(); }
                        if (iterator.MoveNext() || calls != 2) return 6;
                        bool invalid = false;
                        try { Row value = iterator.Current; }
                        catch (Exception* error) { invalid = true; }
                        if (!invalid) return 7;
                    }
                    if (caught != 42) return 8;
                }
                return 0;
            }
            """, optimization);
    }

    private static IEnumerable<(int[] First, int[] Second)> Cases()
    {
        int[][] values = [[], [1], [1, 1], [0, -1, 0, int.MaxValue, int.MinValue], [4, 2, 4, 3, 2, 1], [9, 3, 9, 7, 1]];
        foreach (var first in values)
        foreach (var second in values) yield return (first, second);
        var random = new Random(24317);
        for (int i = 0; i < 128; i++)
        {
            int[] first = Enumerable.Range(0, random.Next(64)).Select(_ => random.Next(-8, 9)).ToArray();
            int[] second = Enumerable.Range(0, random.Next(64)).Select(_ => random.Next(-8, 9)).ToArray();
            yield return (first, second);
        }
    }

    private static string Literal(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
