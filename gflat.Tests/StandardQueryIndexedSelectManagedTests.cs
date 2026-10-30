namespace gflat.Tests;

public sealed class StandardQueryIndexedSelectManagedTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ManagedProjectionResultsRemainRootedWithoutCachingAcrossGetterRetry(int optimization)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Standard"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("indexed-select-managed.gf", Program));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }

    private const string Program = """
        using Standard; using Standard::Queries;
        class Cell
        {
            public int Value;
            public nuint Index;
            public Cell^? Self;
            public Cell(int value, nuint index) { Value = value; Index = index; Self = null; }
        }
        Cell^? kept = null;
        nuint baseline = 0;
        int calls = 0;
        bool fail = true;

        Cell^ Project(int value, nuint index)
        {
            GC::Collect();
            nuint expected = baseline;
            if (kept != null) expected++;
            if (TracingCollector::ManagedCount != expected) throw new Exception("cached result");
            if (kept != null && (kept!.Value != 20 || kept!.Self != kept))
                throw new Exception("lost retained result");
            calls++;
            if (index == 2 && fail)
            {
                fail = false;
                throw new Exception("projection retry", 71);
            }
            Cell^ result = new^ Cell(value, index);
            result.Self = result;
            return result;
        }

        void Work()
        {
            IntRange input = new(20, 4);
            var cursor = input.AsEnumerable().Select(Project).GetEnumerator();
            for (nuint index = 0; index < 4; index++)
            {
                if (!cursor.MoveNext()) throw new Exception("missing item");
                if (index == 2)
                {
                    bool caught = false;
                    try { cursor.Current; }
                    catch (ref Exception error) { caught = error.GetCode() == 71; }
                    if (!caught || fail) throw new Exception("missing callback failure");
                    GC::Collect();
                    if (TracingCollector::ManagedCount != baseline + 1)
                        throw new Exception("failed result retained");
                }
                {
                    Cell^ current = cursor.Current;
                    if (current.Value != 20 + (int)index || current.Index != index || current.Self != current)
                        throw new Exception("wrong projected result");
                    if (index == 0) kept = current;
                    if (GC::Collect() != 0) throw new Exception("live result collected");
                    nuint expected = baseline + 2;
                    if (index == 0) expected = baseline + 1;
                    if (TracingCollector::ManagedCount != expected) throw new Exception("wrong live result count");
                }
            }
            if (cursor.MoveNext() || calls != 5) throw new Exception("wrong projection count");
        }

        int main()
        {
            GC::Collect();
            baseline = TracingCollector::ManagedCount;
            Work();
            GC::Collect();
            if (TracingCollector::ManagedCount != baseline + 1 || kept == null) return 1;
            if (kept!.Value != 20 || kept!.Index != 0 || kept!.Self != kept) return 2;
            kept = null;
            GC::Collect();
            if (TracingCollector::ManagedCount != baseline) return 3;
            return 0;
        }
        """;
}
