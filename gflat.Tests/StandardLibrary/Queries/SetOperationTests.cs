namespace gflat.Tests.Std.Queries;

public sealed partial class SetOperationTests
{
    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("QUERY_SET_OPERATIONS.md", 0);

    private static IEnumerable<SourceFile> Sources(string program)
        => StandardTestSources.Combine(StandardTestSources.Values, StandardTestSources.Load(
            "core/Collections/HashMap.gf", "core/Collections/HashSet.gf", "core/Collections/EqualityComparer.gf",
            "core/Collections/default_equality.gf", "core/Queries/ConcatSource.gf", "core/Queries/DistinctSource.gf",
            "core/Queries/UnionSource.gf", "core/Queries/SetFilterSource.gf", "core/Queries/enumerable_union.gf",
            "core/Queries/enumerable_except.gf", "core/Queries/enumerable_intersect.gf", "core/Queries/enumerable_to_vector.gf"))
            .Append(new SourceFile("query-sets.gf", program));

    private static void Run(string program, int optimization)
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
            foreach (var sequence in new[] { first, second, first.Union(second).ToArray(), first.Except(second).ToArray(), first.Intersect(second).ToArray() })
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
                        var union = first.AsEnumerable().Union(second.AsSpan().AsEnumerable());
                        var except = first.AsEnumerable().Except(second.AsSpan().AsEnumerable());
                        var intersect = first.AsEnumerable().Intersect(second.AsSpan().AsEnumerable());
                        for (int pass = 0; pass < 2; pass++)
                        {
                            Vector<int> a = union.ToVector();
                            Vector<int> b = except.ToVector();
                            Vector<int> c = intersect.ToVector();
                            if (!Equal(&a, &expectedUnion)) return 1;
                            if (!Equal(&b, &expectedExcept)) return 2;
                            if (!Equal(&c, &expectedIntersect)) return 3;
                        }
                        Vector<int> freeUnion = Union(first.AsEnumerable(), second.AsSpan().AsEnumerable()).ToVector();
                        Vector<int> freeExcept = Except(first.AsEnumerable(), second.AsSpan().AsEnumerable()).ToVector();
                        Vector<int> freeIntersect = Intersect(first.AsEnumerable(), second.AsSpan().AsEnumerable()).ToVector();
                        if (!Equal(&freeUnion, &expectedUnion) || !Equal(&freeExcept, &expectedExcept) || !Equal(&freeIntersect, &expectedIntersect)) return 4;
                    }
                    if (fgetc(input!) != -1) return 91;
                    return 0;
                }
                """, optimization);
        }
        finally { Directory.Delete(directory, true); }
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
