namespace gflat.Tests.Std.Collections;

public sealed class SpanSetSearchTests
{
    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("SPAN_SET_SEARCH.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NoncopyableValuesPreserveCallbackOrderAliasingAndFailureOutputs(int optimization)
    {
        var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/Collections/span_set_search.gf"))
            .Append(new SourceFile("span-set-search-owners.gf", """
            using Standard;
            using Standard::Collections;
            using Standard::Memory;

            long trace = 0;
            bool fail = false;
            struct Item
            {
                public int Value;
                public int Index;
                public Item(int value, int index) { Value = value; Index = index; }
                ~Item() { }
            }
            bool Equal(immutable(Item)* left, immutable(Item)* right)
            {
                trace = trace * 100l + (long)((left.Index + 1) * 10 + right.Index + 1);
                if (fail) throw new Exception("comparison failed");
                return left.Value == right.Value;
            }
            int main()
            {
                Item[3] data = [new Item(1, 0), new Item(2, 1), new Item(3, 2)];
                Item[2] choices = [new Item(2, 0), new Item(3, 1)];
                Span<immutable(Item)> source = new(&data[0], 3);
                Span<immutable(Item)> candidates = new(&choices[0], 2);
                if (IndexOfAny<Item>(source, candidates, Equal) != 1 || trace != 111221l) return 1;
                trace = 0;
                if (LastIndexOfAny<Item>(source, candidates, Equal) != 2 || trace != 3132l) return 2;
                trace = 0;
                if (IndexOfAnyExcept<Item>(source, candidates, Equal) != 0 || trace != 1112l) return 3;
                trace = 0;
                if (LastIndexOfAnyExcept<Item>(source, candidates, Equal) != 0 || trace != 3132211112l) return 4;
                trace = 0;
                if (!ContainsAny<Item>(source, source.Slice(1), Equal)) return 5;
                trace = 0;
                if (!ContainsAnyExcept<Item>(source, source.Slice(1), Equal)) return 6;
                fail = true;
                trace = 0;
                Span<immutable(Item)> empty = new();
                if (IndexOfAny<Item>(source, empty, Equal) != 3 || LastIndexOfAny<Item>(empty, candidates, Equal) != 0) return 7;
                if (IndexOfAnyExcept<Item>(source, empty, Equal) != 0 || LastIndexOfAnyExcept<Item>(source, empty, Equal) != 2) return 8;
                if (trace != 0) return 9;
                nuint output = 99;
                bool caught = false;
                try { output = LastIndexOfAnyExcept<Item>(source, candidates, Equal); }
                catch (Exception* error) { caught = true; }
                if (!caught || output != 99 || data[0].Value != 1 || choices[0].Value != 2) return 10;
                return 0;
            }
            """)), optimization);
        Assert.True(result.ExitCode == 0, $"Borrowed set search exit {result.ExitCode}: {result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ExhaustiveBinarySequencesMatchDotNet(int optimization)
    {
        string path = Path.Combine(Path.GetTempPath(), "gflat_set_search_" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            for (int length = 0; length <= 7; length++)
            for (int mask = 0; mask < 1 << length; mask++)
            for (int count = 0; count <= 4; count++)
            for (int candidateMask = 0; candidateMask < 1 << count; candidateMask++)
            {
                int[] source = Enumerable.Range(0, length).Select(i => mask >> i & 1).ToArray();
                int[] choices = Enumerable.Range(0, count).Select(i => candidateMask >> i & 1).ToArray();
                int Fix(int index) => index < 0 ? length : index;
                int first = Fix(MemoryExtensions.IndexOfAny<int>(source.AsSpan(), choices.AsSpan()));
                int last = Fix(MemoryExtensions.LastIndexOfAny<int>(source.AsSpan(), choices.AsSpan()));
                int firstExcept = Fix(MemoryExtensions.IndexOfAnyExcept<int>(source.AsSpan(), choices.AsSpan()));
                int lastExcept = Fix(MemoryExtensions.LastIndexOfAnyExcept<int>(source.AsSpan(), choices.AsSpan()));
                foreach (int item in new[] { length, mask, count, candidateMask, first, last, firstExcept, lastExcept }) writer.Write(item);
            }
            string literal = "\"" + path.Replace("\\", "\\\\") + "\"";
            var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
                StandardTestSources.Load("core/Collections/EqualityComparer.gf", "core/Collections/default_equality.gf",
                    "core/Collections/span_set_search.gf"))
                .Append(new SourceFile("span-set-search.gf", $$"""
                using Standard;
                using Standard::Collections;
                using Standard::Memory;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" nuint fread(void* buffer, nuint size, nuint count, void* file);
                extern "C" int fclose(void* file);
                extern "C" int ferror(void* file);
                bool Equal(immutable(int)* left, immutable(int)* right) => *left == *right;
                int main()
                {
                    void*? file = fopen({{literal}}, "rb");
                    if (file == null) return 1;
                    defer { fclose(file!); }
                    int[8] row;
                    int[7] source;
                    int[4] choices;
                    while (true)
                    {
                        nuint received = fread(&row[0], 1, 32, file!);
                        if (received == 0) break;
                        if (received != 32) return 2;
                        for (int i = 0; i < row[0]; i++) source[i] = (row[1] >> i) & 1;
                        for (int i = 0; i < row[2]; i++) choices[i] = (row[3] >> i) & 1;
                        Span<immutable(int)> values = new(&source[0], (nuint)row[0]);
                        Span<immutable(int)> candidates = new(&choices[0], (nuint)row[2]);
                        if (IndexOfAny<int>(values, candidates) != (nuint)row[4]) return 3;
                        if (LastIndexOfAny<int>(values, candidates) != (nuint)row[5]) return 4;
                        if (IndexOfAnyExcept<int>(values, candidates) != (nuint)row[6]) return 5;
                        if (LastIndexOfAnyExcept<int>(values, candidates) != (nuint)row[7]) return 6;
                        if (ContainsAny<int>(values, candidates) != (row[4] != row[0])) return 7;
                        if (ContainsAnyExcept<int>(values, candidates) != (row[6] != row[0])) return 8;
                        if (IndexOfAny<int>(values, candidates, Equal) != (nuint)row[4]) return 9;
                        if (LastIndexOfAny<int>(values, candidates, Equal) != (nuint)row[5]) return 10;
                        if (IndexOfAnyExcept<int>(values, candidates, Equal) != (nuint)row[6]) return 11;
                        if (LastIndexOfAnyExcept<int>(values, candidates, Equal) != (nuint)row[7]) return 12;
                    }
                    if (ferror(file!) != 0) return 13;
                    return 0;
                }
                """)), optimization);
            Assert.True(result.ExitCode == 0, $"Set search exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }
        finally { File.Delete(path); }
    }
}
