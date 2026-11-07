namespace gflat.Tests.Std.Collections;

public sealed class SpanCountTests
{
    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("SPAN_COUNT.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CountsBorrowedOwnersInOrderAndPreservesStateOnEqualityFailure(int optimization)
    {
        var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/Collections/EqualityComparer.gf", "core/Collections/span_algorithms.gf",
                "core/Collections/default_equality.gf", "core/Collections/span_find_run.gf", "core/Collections/span_subsequence_search.gf"))
            .Append(new SourceFile("span-count.gf", """
            using Standard;
            using Standard::Collections;
            using Standard::Memory;

            int calls = 0;
            int failAt = -1;
            int next = 0;
            struct Item : IEquatable<Item>
            {
                public int Value;
                public int Index;
                public Item(int value, int index) { Value = value; Index = index; }
                ~Item() { }
                public immutable bool Equals(immutable(Item)* other)
                {
                    if (Index != next) throw new Exception("out of order");
                    next++;
                    calls++;
                    if (Index == failAt) throw new Exception("equality failed");
                    return Value == other.Value;
                }
            }
            bool Equal(immutable(Item)* left, immutable(Item)* right) => left.Equals(right);
            int main()
            {
                Item[5] values = [new Item(3, 0), new Item(7, 1), new Item(3, 2), new Item(9, 3), new Item(3, 4)];
                Span<immutable(Item)> source = new(&values[0], 5);
                Item key = new(3, 99);
                for (int form = 0; form < 4; form++)
                {
                    calls = 0; next = 0;
                    nuint count = 0;
                    if (form == 0) count = Count<Item>(source, &key);
                    else if (form == 1) count = Count<Item>(source, &values[0], Equal);
                    else if (form == 2) count = Count<Item>(source, source.Slice(0, 1));
                    else count = Count<Item>(source, source.Slice(0, 1), Equal);
                    if (count != 3 || calls != 5) return 1;
                }
                calls = 0; next = 0;
                if (Count<Item>(new Span<immutable(Item)>(), &key) != 0 || calls != 0) return 2;
                for (int failure = 0; failure < 5; failure++)
                {
                    failAt = failure; calls = 0; next = 0;
                    nuint output = 99;
                    bool caught = false;
                    try { output = Count<Item>(source, source.Slice(0, 1)); }
                    catch (Exception* error) { caught = true; }
                    if (!caught || output != 99 || calls != failure + 1) return 3;
                    if (values[0].Value != 3 || values[4].Value != 3 || key.Value != 3) return 4;
                }
                return 0;
            }
            """)), optimization);
        Assert.True(result.ExitCode == 0, $"Span count exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
