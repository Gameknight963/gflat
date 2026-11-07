namespace gflat.Tests.Std.Queries;

public sealed partial class OrderByTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ValueOrderingGuideExamplesCompile(int index)
        => StandardLibraryGuideTests.CheckProgram("QUERY_ORDER.md", index);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DefaultDescendingAndFreestandingFormsReturnNumericOrder(int optimization)
        => Run("""
            using Standard::Memory;
            using Standard::Queries;

            int Compare(immutable(int)* left, immutable(int)* right)
            {
                if (*left < *right) return -2147483647 - 1;
                if (*left > *right) return 2147483647;
                return 0;
            }
            int Identity(int value) => value;
            int main()
            {
                int[6] input = [3, -1, 2, 3, 0, -1];
                int[6] expected = [3, 3, 2, 0, -1, -1];
                var source = new Span<int>(&input[0], 6).AsEnumerable();
                var a = source.OrderDescending().GetEnumerator();
                var b = Standard::Queries::OrderDescending(source).GetEnumerator();
                var c = Standard::Queries::OrderDescending(source, Compare).GetEnumerator();
                var d = source.OrderDescending(Compare).GetEnumerator();
                var e = source.Order().ThenBy(Identity).GetEnumerator();
                for (nuint i = 0; i < 6; i++)
                {
                    if (!a.MoveNext() || !b.MoveNext() || !c.MoveNext() || !d.MoveNext() || !e.MoveNext()) return 1;
                    if (a.Current != expected[i] || b.Current != expected[i]
                        || c.Current != expected[i] || d.Current != expected[i] || e.Current != expected[5 - i]) return 2;
                }
                if (a.MoveNext() || b.MoveNext() || c.MoveNext() || d.MoveNext() || e.MoveNext()) return 3;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DescendingBorrowsFreshNoncopyableOwnersAndCleansEveryObservedFailure(int optimization)
        => Run("""
            using Standard;
            using Standard::Collections;
            using Standard::Memory;
            using Standard::Queries;
            using Standard::Text;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            int attempts = 0;
            int failAt = 0;
            int allocated = 0;
            int released = 0;
            int owners = 0;
            int starts = 0;
            int closes = 0;
            int mode = 0;
            bool deny = false;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes)
                {
                    attempts++;
                    if (deny || attempts == failAt) return null;
                    void*? result = malloc(bytes);
                    if (result != null) allocated++;
                    return result;
                }
                public replace void Free(void* storage) nothrow { released++; free(storage); }
            }
            struct Owner
            {
                public int Id;
                public String Text;
                public Owner(int id)
                {
                    Id = id;
                    Text = s"owner";
                    if (mode == 1 && id == 4) throw new Exception("construct", 31);
                    owners++;
                }
                ~Owner() { owners--; }
            }
            struct Source
            {
                public Enumerator GetEnumerator()
                {
                    if (mode == 5) throw new Exception("GetEnumerator", 35);
                    return new Enumerator();
                }
                public struct Enumerator
                {
                    private int position;
                    private String token;
                    public Enumerator() { position = 0; token = s"iterator"; starts++; }
                    public bool MoveNext()
                    {
                        position++;
                        if (mode == 3 && position == 4) throw new Exception("MoveNext", 33);
                        return position <= 9;
                    }
                    public Owner Current
                    {
                        get
                        {
                            if (mode == 4 && position == 4) throw new Exception("Current", 34);
                            return new Owner(position);
                        }
                    }
                    ~Enumerator() { closes++; }
                }
            }
            int Compare(immutable(Owner)* left, immutable(Owner)* right)
            {
                if (mode == 2) throw new Exception("compare", 32);
                return left.Id % 3 - right.Id % 3;
            }
            int main()
            {
                var source = new Enumerable<Owner, Source, Source::Enumerator>(new Source());
                var query = source.OrderDescending(Compare);
                int observed = 0;
                {
                    var cursor = query.GetEnumerator();
                    if (starts != 0 || owners != 0) return 1;
                    attempts = 0;
                    if (!cursor.MoveNext()) return 2;
                    observed = attempts;
                    if (owners != 9 || starts != closes) return 3;
                    int[9] expected = [2, 5, 8, 1, 4, 7, 3, 6, 9];
                    deny = true;
                    for (nuint i = 0; i < 9; i++)
                    {
                        immutable(Owner)* value = &cursor.Current;
                        if (value.Id != expected[i] || &cursor.Current != value || !value.Text.Equals("owner")) return 4;
                        if (cursor.MoveNext() != (i < 8)) return 5;
                    }
                    deny = false;
                    if (owners != 0 || cursor.MoveNext()) return 6;
                }
                if (allocated != released || observed == 0) return 7;
                for (int failure = 1; failure <= observed + 5; failure++)
                {
                    {
                        var cursor = query.GetEnumerator();
                        attempts = 0;
                        if (failure <= observed) failAt = failure;
                        else mode = failure - observed;
                        bool caught = false;
                        int code = 0;
                        try { cursor.MoveNext(); }
                        catch (Exception* error) { caught = true; code = error.GetCode(); }
                        failAt = 0;
                        mode = 0;
                        if (!caught || owners != 0 || starts != closes || cursor.MoveNext()) return 8;
                        if (failure > observed && code != 30 + failure - observed) return 9;
                        bool invalid = false;
                        try { cursor.Current; } catch (Exception* error) { invalid = true; }
                        if (!invalid) return 10;
                    }
                    if (allocated != released) return 11;
                }
                {
                    var cursor = query.GetEnumerator();
                    if (!cursor.MoveNext() || owners != 9) return 12;
                }
                if (allocated != released || owners != 0 || starts != closes) return 13;
                return 0;
            }
            """, optimization);
}
