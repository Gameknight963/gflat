namespace gflat.Tests.Std.Queries;

public sealed partial class ElementPredicateTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BorrowedNoncopyablePointeesRetainTheirStoredIdentity(int optimization)
    {
        var sources = StandardTestSources.Combine(Sources("""
            using Standard;
            using Standard::Collections;
            using Standard::Queries;

            int owners = 0;
            bool denyCompare = false;
            struct Owner
            {
                public int Value;
                public Owner(int value) { Value = value; owners++; }
                ~Owner() { owners--; }
            }
            int Compare(immutable(Owner)* left, immutable(Owner)* right)
            {
                if (denyCompare) throw new Exception("unexpected comparison");
                if (left.Value < right.Value) return -1;
                if (left.Value > right.Value) return 1;
                return 0;
            }
            bool IsSeven(immutable(Owner)* value) => value.Value == 7;
            bool Positive(immutable(Owner)* value) => value.Value > 0;
            int main()
            {
                {
                    PriorityQueue<Owner> queue = new(Compare);
                    queue.Enqueue(new Owner(3));
                    queue.Enqueue(new Owner(0));
                    queue.Enqueue(new Owner(7));
                    Owner sentinel = new(99);
                    immutable(Owner)* output = &sentinel;
                    var query = queue.UnorderedItems.AsEnumerable();
                    denyCompare = true;
                    immutable(Owner)* selected = query.Single(IsSeven);
                    if (selected.Value != 7 || query.First(IsSeven) != selected || query.Last(IsSeven) != selected) return 1;
                    if (query.TrySingle(Positive, ref output) || output != &sentinel) return 2;
                    if (!query.TryFirst(IsSeven, ref output) || output != selected) return 3;
                    output = &sentinel;
                    if (!query.TryLast(IsSeven, ref output) || output != selected || owners != 4) return 4;
                }
                if (owners != 0) return 5;
                return 0;
            }
            """), StandardTestSources.Load("core/Collections/PriorityQueue.gf", "core/Collections/priority_queue_queries.gf"));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Borrowed predicate terminal exit {result.ExitCode}: {result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CurrentIsReadOnceAndEveryFailureCleansCandidatesAndPreservesTryOutputs(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            using Standard;
            using Standard::Queries;
            using Standard::Text;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            int live = 0;
            int owners = 0;
            int starts = 0;
            int ends = 0;
            int reads = 0;
            int calls = 0;
            int mode = 0;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes)
                {
                    void*? result = malloc(bytes);
                    if (result != null) live++;
                    return result;
                }
                public replace void Free(void* storage) nothrow { live--; free(storage); }
            }
            struct Item
            {
                public int Key;
                public String Token;
                public Item(int key) { Key = key; Token = s"item"; owners++; }
                public copy Item(immutable(Item)* source)
                {
                    Key = source.Key;
                    Token = source.Token;
                    if (mode == 8) throw new Exception("copy", 78);
                    owners++;
                }
                ~Item() { owners--; }
            }
            struct Source
            {
                public struct Iterator
                {
                    private int position;
                    private String token;
                    public Iterator() { position = 0; token = s"iterator"; starts++; }
                    public bool MoveNext()
                    {
                        if (mode == 2) throw new Exception("MoveNext", 72);
                        if (mode == 5 && position == 2) throw new Exception("later MoveNext", 75);
                        if (position == 4) return false;
                        position++;
                        return true;
                    }
                    public Item Current
                    {
                        get
                        {
                            reads++;
                            if (mode == 3) throw new Exception("Current", 73);
                            return new Item(position);
                        }
                    }
                    ~Iterator() { ends++; }
                }
                public Iterator GetEnumerator()
                {
                    if (mode == 1) throw new Exception("GetEnumerator", 71);
                    return new Iterator();
                }
            }
            bool Match(Item value)
            {
                calls++;
                if (mode == 4) throw new Exception("predicate", 74);
                if (mode == 6) return value.Key % 2 != 0;
                if (mode == 7) return false;
                return value.Key == 2;
            }
            int main()
            {
                var query = new Enumerable<Item, Source, Source::Iterator>(new Source());
                for (int operation = 0; operation < 6; operation++)
                for (mode = 0; mode < 9; mode++)
                {
                    {
                        Item output = new(99);
                        readonly(char)* original = output.Token.Data;
                        reads = 0;
                        calls = 0;
                        bool found = false;
                        bool caught = false;
                        try
                        {
                            if (operation == 0) { output = query.First(Match); found = true; }
                            else if (operation == 1) { output = query.Last(Match); found = true; }
                            else if (operation == 2) { output = query.Single(Match); found = true; }
                            else if (operation == 3) found = query.TryFirst(Match, ref output);
                            else if (operation == 4) found = query.TryLast(Match, ref output);
                            else found = query.TrySingle(Match, ref output);
                        }
                        catch (ref Exception error)
                        {
                            if (mode >= 1 && mode <= 5 && error.GetCode() != 70 + mode) return 1;
                            if (mode == 8 && error.GetCode() != 78) return 1;
                            caught = true;
                        }
                        bool first = operation == 0 || operation == 3;
                        bool last = operation == 1 || operation == 4;
                        bool expectedFound = mode == 0 || (mode == 5 && first) || (mode == 6 && (first || last));
                        bool expectedCaught = (mode >= 1 && mode <= 4) || (mode == 5 && !first) ||
                            (mode == 6 && operation == 2) || (mode == 7 && operation < 3) || mode == 8;
                        if (found != expectedFound || caught != expectedCaught || starts != ends || owners != 1 || live != 1) return 2;
                        int expected = 99;
                        if (expectedFound)
                        {
                            expected = 2;
                            if (mode == 6) { expected = 1; if (last) expected = 3; }
                        }
                        if (output.Key != expected || output.Token.Length != 4) return 3;
                        if (!expectedFound && output.Token.Data != original) return 4;
                        int expectedReads = 4;
                        if ((mode == 0 && first) || mode == 5) expectedReads = 2;
                        if (mode == 1 || mode == 2) expectedReads = 0;
                        if (mode == 3 || mode == 4 || mode == 8 || (mode == 6 && first)) expectedReads = 1;
                        if (mode == 6 && !first && !last) expectedReads = 3;
                        if (reads != expectedReads) return 5;
                        int expectedCalls = expectedReads;
                        if (mode == 3 || mode == 8) expectedCalls = 0;
                        if (calls != expectedCalls) return 6;
                    }
                    if (owners != 0 || live != 0) return 7;
                }
                return 0;
            }
            """), optimization);
        Assert.True(result.ExitCode == 0, $"Owning predicate terminal exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
