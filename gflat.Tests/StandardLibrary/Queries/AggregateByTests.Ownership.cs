namespace gflat.Tests.Std.Queries;

public sealed partial class AggregateByTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 2)]
    public void OwningCollisionKeysAndAccumulatorsCleanUpEveryObservedFailure(bool factory, int optimization)
    {
        string invocation = factory ? "query.AggregateBy(SelectKey, Seed, Reduce)" : "query.AggregateBy(SelectKey, seed, Reduce)";
        Run($$"""
            using Standard;
            using Standard::Collections;
            using Standard::Queries;
            using Standard::Text;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            int attempts = 0;
            int allocated = 0;
            int released = 0;
            int failAt = 0;
            int mode = 0;
            int keys = 0;
            int totals = 0;
            int copies = 0;
            int keyCopies = 0;
            int selections = 0;
            int reductions = 0;
            int seeds = 0;
            int hashes = 0;
            int equals = 0;
            int starts = 0;
            int ends = 0;
            int moves = 0;
            int reads = 0;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes)
                {
                    attempts++;
                    if (attempts == failAt) return null;
                    void*? result = malloc(bytes);
                    if (result != null) allocated++;
                    return result;
                }
                public replace void Free(void* storage) nothrow { released++; free(storage); }
            }
            struct Key : IHashable, IEquatable<Key>
            {
                public int Id;
                public int First;
                public String Text;
                public Key(int id, int first) { Id = id; First = first; Text = s"key"; keys++; }
                public copy Key(immutable(Key)* source)
                {
                    Id = source.Id; First = source.First; Text = source.Text;
                    keyCopies++;
                    if (mode == 7 && keyCopies == 2) throw new Exception("key copy", 107);
                    keys++;
                }
                public immutable nuint GetHashCode()
                {
                    hashes++;
                    if (mode == 3 && hashes == 3) throw new Exception("hash", 103);
                    return 0;
                }
                public immutable bool Equals(immutable(Key)* other)
                {
                    equals++;
                    if (mode == 5 && equals == 3) throw new Exception("equality", 105);
                    return Id == other.Id;
                }
                ~Key() { keys--; }
            }
            struct Total
            {
                public int Value;
                public String Text;
                public Total(int value) { Value = value; Text = s"total"; totals++; }
                public copy Total(immutable(Total)* source)
                {
                    Value = source.Value; Text = source.Text;
                    copies++;
                    if (mode == 6 && copies == 2) throw new Exception("total copy", 106);
                    totals++;
                }
                ~Total() { totals--; }
            }
            struct Source
            {
                private int count;
                public Source(int length) { count = length; }
                public Iterator GetEnumerator() => new Iterator(count);
                public struct Iterator
                {
                    private int position;
                    private int count;
                    private String token;
                    public Iterator(int length)
                    {
                        position = 0; count = length; token = s"iterator";
                        if (mode == 8) throw new Exception("iterator", 108);
                        starts++;
                    }
                    public bool MoveNext()
                    {
                        moves++;
                        if (mode == 9 && moves == 3) throw new Exception("advance", 109);
                        if (position == count) return false;
                        position++;
                        return true;
                    }
                    public int Current
                    {
                        get
                        {
                            reads++;
                            if (mode == 10 && reads == 3) throw new Exception("current", 110);
                            return position - 1;
                        }
                    }
                    ~Iterator() { ends++; }
                }
            }
            Key SelectKey(int value)
            {
                int first = selections;
                selections++;
                if (mode == 1 && selections == 3) throw new Exception("selector", 101);
                return new Key(value % 3, first);
            }
            Total Seed(Key key)
            {
                seeds++;
                if (mode == 2 && seeds == 2) throw new Exception("seed", 102);
                return new Total(key.Id * 100);
            }
            Total Reduce(Total value, int item)
            {
                reductions++;
                if (mode == 4 && reductions == 3) throw new Exception("reduce", 104);
                value.Value += item;
                return move value;
            }
            void Reset()
            {
                mode = 0; failAt = 0; attempts = 0; copies = 0; keyCopies = 0;
                selections = 0; reductions = 0; seeds = 0; hashes = 0; equals = 0;
                moves = 0; reads = 0;
            }
            int main()
            {
                {
                    Total seed = new(10);
                    var query = new Enumerable<int, Source, Source::Iterator>(new Source(9));
                    int baseline = allocated - released;
                    Reset();
                    int observed = 0;
                    {
                        HashMap<Key, Total> result = {{invocation}};
                        observed = attempts;
                        if (result.Count != 3 || selections != 9 || reductions != 9 || seeds != {{(factory ? 3 : 0)}}) return 1;
                        if (starts != ends || keys != 3 || totals != 4 || seed.Value != 10 || !seed.Text.Equals("total")) return 2;
                        var iterator = result.GetEnumerator();
                        while (iterator.MoveNext())
                        {
                            var entry = iterator.Current;
                            int id = entry.Key.Id;
                            if (entry.Key.First != id || !entry.Key.Text.Equals("key") || !entry.ValueImmutable.Text.Equals("total")) return 3;
                            int initial = 10;
                            if ({{(factory ? "true" : "false")}}) initial = id * 100;
                            if (entry.ValueImmutable.Value != initial + 9 + id * 3) return 4;
                        }
                    }
                    if (keys != 0 || totals != 1 || allocated - released != baseline) return 5;
                    for (int failure = 1; failure <= 10; failure++)
                    {
                        if (!{{(factory ? "true" : "false")}} && (failure == 2 || failure == 7)) continue;
                        Reset();
                        mode = failure;
                        int code = 0;
                        try { HashMap<Key, Total> result = {{invocation}}; }
                        catch (Exception* error) { code = error.GetCode(); }
                        mode = 0;
                        if (code != 100 + failure) return 6;
                        if (keys != 0 || totals != 1 || starts != ends || allocated - released != baseline) return 7;
                        if (seed.Value != 10 || !seed.Text.Equals("total")) return 8;
                    }
                    for (int failure = 1; failure <= observed; failure++)
                    {
                        Reset();
                        failAt = failure;
                        bool caught = false;
                        try { HashMap<Key, Total> result = {{invocation}}; }
                        catch (Exception* error) { caught = true; }
                        failAt = 0;
                        if (!caught) return 9;
                        if (keys != 0 || totals != 1 || starts != ends || allocated - released != baseline) return 10;
                        if (seed.Value != 10 || !seed.Text.Equals("total")) return 11;
                    }
                }
                if (keys != 0 || totals != 0 || allocated != released || starts != ends) return 12;
                return 0;
            }
            """, optimization);
    }
}
