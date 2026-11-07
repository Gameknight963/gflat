namespace gflat.Tests.Std.Collections;

public sealed class VectorEnumerableInsertRangeTests
{
    private static void Run(string code, int optimization)
    {
        var sources = StandardTestSources.Combine(StandardTestSources.Values, StandardTestSources.Load(
            "core/Collections/vector_add_range.gf", "core/Collections/vector_add_enumerable_range.gf",
            "core/Collections/vector_insert_range.gf", "core/Collections/vector_insert_enumerable_range.gf",
            "core/Collections/span_algorithms.gf"))
            .Append(new SourceFile("vector-enumerable-insert-range.gf", code));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Vector enumerable insert exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Fact]
    public void GuideCompiles() => StandardLibraryGuideTests.CheckProgram("VECTOR_ENUMERABLE_INSERT_RANGE.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EverySmallInsertionPositionAndSelfSlicePreserveInputAndSurvivorOrder(int optimization)
        => Run(Tracking + """
            int main()
            {
                for (nuint count = 0; count <= 9; count++)
                for (nuint start = 0; start <= count; start++)
                for (nuint length = 0; length <= count - start; length++)
                for (nuint index = 0; index <= count; index++)
                for (int separate = 0; separate < 2; separate++)
                for (int roomy = 0; roomy < 2; roomy++)
                {
                    Vector<int> target = new();
                    if (roomy != 0) target.Reserve(32);
                    else target.Reserve(count);
                    Vector<int> other = new();
                    for (nuint i = 0; i < count; i++) { target.Add((int)i); other.Add(100 + (int)i); }
                    Span<int> input = target.AsSpan().Slice(start, length);
                    if (separate != 0) input = other.AsSpan().Slice(start, length);
                    int before = attempts;
                    if (index % 2 == 0) target.InsertRange(index, input.AsEnumerable());
                    else Standard::Collections::InsertRange(ref target, index, input.AsEnumerable());
                    if (length == 0 && attempts != before) return 1;
                    if (target.Count != count + length) return 2;
                    if (roomy != 0 && target.Capacity != 32) return 3;
                    for (nuint i = 0; i < target.Count; i++)
                    {
                        int expected = (int)i;
                        if (i >= index && i < index + length) expected = (int)(start + i - index) + separate * 100;
                        else if (i >= index + length) expected = (int)(i - length);
                        if (target[i] != expected) return 4;
                    }
                    for (nuint i = 0; i < count; i++) if (other[i] != 100 + (int)i) return 5;
                }
                return live;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryAllocationAndCopyFailurePreservesVectorAndAllOwners(int optimization)
        => Run(Tracking + """
            int owners = 0;
            int copies = 0;
            int failCopy = 0;
            struct Owner
            {
                public int Id;
                public String Text;
                public Owner(int id) { Id = id; Text = s"owned"; owners++; }
                public copy Owner(immutable(Owner)* source)
                {
                    Id = source.Id; Text = source.Text;
                    copies++;
                    if (copies == failCopy) throw new Exception("copy");
                    owners++;
                }
                ~Owner() { owners--; }
            }
            void Apply(Vector<Owner>* target, Vector<Owner>* input, int form)
            {
                if (form == 0) target.InsertRange(0, input.AsEnumerable());
                else Standard::Collections::InsertRange(ref *target, 0, input.AsEnumerable());
            }
            int main()
            {
                for (int form = 0; form < 2; form++)
                for (int reserved = 0; reserved < 2; reserved++)
                {
                    int observed = 0;
                    for (int point = 0; point <= observed; point++)
                    {
                        Vector<Owner> target = new();
                        target.Reserve(4);
                        for (int i = 0; i < 4; i++) target.Add(new Owner(i));
                        { Owner removed = target.RemoveAt(0); }
                        target.Add(new Owner(4));
                        if (reserved != 0) target.Reserve(32);
                        nuint capacity = target.Capacity;
                        nuint first = (&target[0]).reinterpret<nuint>();
                        Vector<Owner> input = new();
                        for (int i = 5; i < 14; i++) input.Add(new Owner(i));
                        int baseline = live;
                        attempts = 0;
                        failAt = point;
                        bool caught = false;
                        try { Apply(&target, &input, form); }
                        catch (Exception* error) { caught = true; }
                        failAt = 0;
                        if (point == 0)
                        {
                            observed = attempts;
                            if (caught || owners != 22 || target.Count != 13) return 1;
                            if (reserved != 0 && ((&target[0]).reinterpret<nuint>() != first || target.Capacity != capacity)) return 9;
                        }
                        else
                        {
                            if (!caught || live != baseline || owners != 13 || target.Count != 4 || target.Capacity != capacity) return 2;
                            if ((&target[0]).reinterpret<nuint>() != first) return 3;
                        }
                        if (point == 0)
                            for (int i = 5; i < 14; i++) { Owner item = target.RemoveAt(0); if (item.Id != i) return 5; }
                        for (int i = 1; i <= 4; i++) { Owner item = target.RemoveAt(0); if (item.Id != i) return 4; }
                        for (nuint i = 0; i < 9; i++) if (input[i].Id != (int)i + 5) return 6;
                    }
                    for (int point = 1; point <= 9; point++)
                    {
                        Vector<Owner> target = new();
                        target.Add(new Owner(1));
                        Vector<Owner> input = new();
                        for (int i = 0; i < 9; i++) input.Add(new Owner(i));
                        int baseline = live;
                        copies = 0; failCopy = point;
                        bool caught = false;
                        try { Apply(&target, &input, form); }
                        catch (Exception* error) { caught = true; }
                        failCopy = 0;
                        if (!caught || live != baseline || owners != 10 || target.Count != 1 || target[0].Id != 1) return 7;
                    }
                }
                if (owners != 0) return 8;
                return live;
            }
            """, optimization);

    private const string Tracking = """
        using Standard;
        using Standard::Collections;
        using Standard::Memory;
        using Standard::Queries;
        using Standard::Text;

        extern "C" void*? malloc(nuint bytes);
        extern "C" void free(void* storage);
        int live = 0;
        int attempts = 0;
        int failAt = 0;
        void*? latest = null;
        namespace Memory
        {
            public replace void*? Allocate(nuint bytes)
            {
                attempts++;
                if (attempts == failAt) return null;
                void*? result = malloc(bytes);
                if (result != null) live++;
                latest = result;
                return result;
            }
            public replace void Free(void* storage) nothrow { live--; free(storage); }
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FreshMoveOnlyEnumerableValuesRemainUsableWithOptionalSpanOverloadPresent(int optimization)
        => Run(Tracking + """
            int owners = 0;
            int cursors = 0;
            int acquisitions = 0;
            int failure = 0;
            struct Owner
            {
                public int Id;
                public String Text;
                public Owner(int id) { Id = id; Text = s"owned"; owners++; }
                ~Owner() { owners--; }
            }
            struct Source
            {
                public Cursor GetEnumerator()
                {
                    acquisitions++;
                    if (failure == 1) throw new Exception("acquire");
                    return new();
                }
                public struct Cursor
                {
                    private int position;
                    private String token;
                    public Cursor() { position = 0; token = s"cursor"; cursors++; }
                    ~Cursor() { cursors--; }
                    public bool MoveNext()
                    {
                        position++;
                        if (failure == 2 && position == 3) throw new Exception("advance");
                        return position <= 6;
                    }
                    public Owner Current
                    {
                        get
                        {
                            if (failure == 3 && position == 3) throw new Exception("read");
                            return new Owner(7 - position);
                        }
                    }
                }
            }
            int main()
            {
                {
                    Vector<Owner> target = new();
                    target.Add(new Owner(0));
                    nuint capacity = target.Capacity;
                    int baseline = live;
                    var values = new Enumerable<Owner, Source, Source::Cursor>(new Source());
                    for (int invalid = 0; invalid < 2; invalid++)
                    {
                        nuint index = target.Count + 1;
                        if (invalid != 0) index = (nuint)0 - 1;
                        bool caught = false;
                        try { target.InsertRange(index, values); }
                        catch (Exception* error) { caught = true; }
                        if (!caught || acquisitions != 0 || target.Count != 1 || target.Capacity != capacity || owners != 1 || live != baseline) return 7;
                    }
                    for (int mode = 1; mode <= 3; mode++)
                    {
                        failure = mode;
                        bool caught = false;
                        try { target.InsertRange(0, values); } catch (Exception* error) { caught = true; }
                        failure = 0;
                        if (!caught || owners != 1 || cursors != 0 || live != baseline || target.Count != 1 || target.Capacity != capacity) return 1;
                    }
                    target.InsertRange(0, values);
                    if (owners != 7 || cursors != 0 || target.Count != 7) return 2;
                    for (int i = 0; i < 7; i++)
                    {
                        Owner item = target.RemoveAt(0);
                        if (i == 6 && item.Id != 0) return 3;
                        if (i != 6 && item.Id != 6 - i) return 3;
                    }
                    target.InsertRange(0, values);
                    if (owners != 6 || cursors != 0 || target.Count != 6) return 4;
                    for (int i = 1; i <= 6; i++)
                    {
                        Owner item = target.RemoveAt(0);
                        if (item.Id != 7 - i) return 5;
                    }
                }
                if (owners != 0 || cursors != 0) return 6;
                return live;
            }
            """, optimization);
}
