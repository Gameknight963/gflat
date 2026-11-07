using System.Text;

namespace gflat.Tests.Std.Queries;

public sealed partial class SetOperationTests
{
    private const string Tracking = """
        using Standard;
        using Standard::Collections;
        using Standard::Memory;
        using Standard::Queries;
        using Standard::Text;

        extern "C" void*? malloc(nuint bytes);
        extern "C" void free(void* storage);
        int allocations = 0;
        int releases = 0;
        int attempts = 0;
        int failAt = 0;
        int live = 0;
        int active = 0;
        int created = 0;
        int destroyed = 0;
        int mode = 0;
        int copies = 0;
        int hashes = 0;
        int equals = 0;
        int leftMoves = 0;
        int rightMoves = 0;
        namespace Memory
        {
            public replace void*? Allocate(nuint bytes)
            {
                attempts++;
                if (failAt != 0 && attempts == failAt) return null;
                void*? storage = malloc(bytes);
                if (storage != null) allocations++;
                return storage;
            }
            public replace void Free(void* storage) nothrow
            {
                releases++;
                free(storage);
            }
        }
        struct Key : IHashable, IEquatable<Key>
        {
            public int Id;
            private String text;
            public Key(int id, String label)
            {
                Id = id;
                text = move label;
                live++;
            }
            public copy Key(immutable(Key)* source)
            {
                Id = source.Id;
                text = source.text;
                copies++;
                if (mode == 6 && copies == 3) throw new Exception("copy", 106);
                live++;
            }
            public char Tag { immutable get => text[0]; }
            public immutable nuint GetHashCode()
            {
                hashes++;
                if (mode == 7 && hashes == 3) throw new Exception("hash", 107);
                return 0;
            }
            public immutable bool Equals(immutable(Key)* other)
            {
                equals++;
                if (mode == 8 && equals == 3) throw new Exception("equality", 108);
                return Id == other.Id;
            }
            ~Key() { live--; }
        }
        struct KeySource
        {
            private Span<Key> values;
            private int identity;
            public KeySource(Span<Key> input, int side)
            {
                values = input;
                identity = side;
            }
            public Iterator GetEnumerator() => new Iterator(values, identity);
            public struct Iterator
            {
                private Span<Key> values;
                private nuint position;
                private int identity;
                private String token;
                public Iterator(Span<Key> input, int side)
                {
                    values = input;
                    identity = side;
                    position = 0;
                    token = s"iterator";
                    if (mode == 1 && identity == 2) throw new Exception("construction", 101);
                    active++;
                    created++;
                }
                public bool MoveNext()
                {
                    if (identity == 1) leftMoves++;
                    else rightMoves++;
                    if (mode == 2 && identity == 2 && position == 1) throw new Exception("right advance", 102);
                    if (mode == 3 && identity == 1 && position == 1) throw new Exception("left advance", 103);
                    if (position == values.Length) return false;
                    position++;
                    return true;
                }
                public ref Key Current
                {
                    get
                    {
                        if (mode == 4 && identity == 2) throw new Exception("right current", 104);
                        if (mode == 5 && identity == 1) throw new Exception("left current", 105);
                        return values[position - 1];
                    }
                }
                ~Iterator()
                {
                    active--;
                    destroyed++;
                }
            }
        }
        """;

    [Theory]
    [InlineData(0, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    [InlineData(2, true)]
    public void OwningCollisionKeysKeepFirstRepresentativesAndCleanUpEveryObservedFailure(int optimization, bool by)
    {
        var source = new StringBuilder(Tracking);
        if (by) source.Append("Key SelectKey(Key value) => value;\n");
        foreach (string operation in new[] { "Union", "Except", "Intersect" }) source.Append(OwnedCheck(operation, by));
        source.Append("""
            int main()
            {
                {
                    Vector<Key> first = new();
                    first.Add(new Key(3, s"A"));
                    first.Add(new Key(1, s"B"));
                    first.Add(new Key(3, s"C"));
                    first.Add(new Key(2, s"D"));
                    first.Add(new Key(1, s"E"));
                    Vector<Key> second = new();
                    second.Add(new Key(2, s"X"));
                    second.Add(new Key(4, s"Y"));
                    second.Add(new Key(2, s"Z"));
                    second.Add(new Key(1, s"W"));
                    var left = new Enumerable<Key, KeySource, KeySource::Iterator>(new KeySource(first.AsSpan(), 1));
                    var right = new Enumerable<Key, KeySource, KeySource::Iterator>(new KeySource(second.AsSpan(), 2));
                    int result = CheckUnion(left, right);
                    if (result != 0) return result;
                    result = CheckExcept(left, right);
                    if (result != 0) return result;
                    result = CheckIntersect(left, right);
                    if (result != 0) return result;
                    if (first[0].Tag != 'A' || first[2].Tag != 'C' || second[0].Tag != 'X') return 90;
                }
                if (live != 0 || active != 0 || created != destroyed || allocations != releases) return 91;
                return 0;
            }
            """);
        if (by) SetOperationByTests.Run(source.ToString(), optimization);
        else Run(source.ToString(), optimization);
    }

    private static string OwnedCheck(string operation, bool by)
    {
        int[] ids = operation switch { "Union" => [3, 1, 2, 4], "Except" => [3], _ => [1, 2] };
        string tags = operation switch { "Union" => "ABDY", "Except" => "A", _ => "BD" };
        string representativeChecks = string.Concat(ids.Select((id, i) =>
            $"        if (result[{i}].Id != {id} || result[{i}].Tag != '{tags[i]}') return 12;\n"));
        return $$"""
            int Check{{operation}}(Enumerable<Key, KeySource, KeySource::Iterator> left,
                Enumerable<Key, KeySource, KeySource::Iterator> right)
            {
                int initialLive = live;
                int initialStorage = allocations - releases;
                leftMoves = 0;
                rightMoves = 0;
                int before = attempts;
                {
                    var query = left.{{operation}}{{(by ? "By" : "")}}(right{{(by ? ", SelectKey" : "")}});
                    if (attempts != before || active != 0 || leftMoves != 0 || rightMoves != 0) return 10;
                    Vector<Key> result = query.ToVector();
                    if (result.Count != (nuint){{ids.Length}}) return 11;
            {{representativeChecks}}
                }
                if (live != initialLive || active != 0 || allocations - releases != initialStorage) return 13;
                leftMoves = 0;
                rightMoves = 0;
                {
                    var iterator = left.{{operation}}{{(by ? "By" : "")}}(right{{(by ? ", SelectKey" : "")}}).GetEnumerator();
                    if (active != 2) return 14;
                    bool invalid = false;
                    try { Key item = iterator.Current; }
                    catch (Exception* error) { invalid = true; }
                    if (!invalid || !iterator.MoveNext()) return 15;
                    if (rightMoves != {{(operation == "Union" ? 0 : 5)}} || leftMoves != {{(operation == "Intersect" ? 2 : 1)}}) return 16;
                }
                if (live != initialLive || active != 0 || allocations - releases != initialStorage) return 17;
                foreach (Key item in left.{{operation}}{{(by ? "By" : "")}}(right{{(by ? ", SelectKey" : "")}})) break;
                if (live != initialLive || active != 0 || allocations - releases != initialStorage) return 18;

                for (int failure = 1; failure <= 8; failure++)
                {
                    mode = failure;
                    copies = 0;
                    hashes = 0;
                    equals = 0;
                    int code = 0;
                    try
                    {
                        var iterator = left.{{operation}}{{(by ? "By" : "")}}(right{{(by ? ", SelectKey" : "")}}).GetEnumerator();
                        bool moving = false;
                        try
                        {
                            while (true)
                            {
                                moving = true;
                                bool more = iterator.MoveNext();
                                moving = false;
                                if (!more) break;
                                Key item = iterator.Current;
                            }
                        }
                        catch (Exception* error)
                        {
                            code = error.GetCode();
                            mode = 0;
                            if (moving)
                            {
                                int calls = leftMoves + rightMoves;
                                if (iterator.MoveNext() || calls != leftMoves + rightMoves) return 20;
                                bool invalid = false;
                                try { Key item = iterator.Current; }
                                catch (Exception* ignored) { invalid = true; }
                                if (!invalid) return 21;
                            }
                        }
                    }
                    catch (Exception* error) { code = error.GetCode(); }
                    mode = 0;
                    if (code != 100 + failure) return 22;
                    if (live != initialLive || active != 0 || allocations - releases != initialStorage) return 23;
                }

                int observed = 0;
                attempts = 0;
                {
                    var iterator = left.{{operation}}{{(by ? "By" : "")}}(right{{(by ? ", SelectKey" : "")}}).GetEnumerator();
                    while (iterator.MoveNext()) { Key item = iterator.Current; }
                    observed = attempts;
                    bool invalid = false;
                    try { Key item = iterator.Current; }
                    catch (Exception* error) { invalid = true; }
                    if (!invalid || iterator.MoveNext()) return 24;
                }
                if (observed == 0) return 25;
                for (int failure = 1; failure <= observed; failure++)
                {
                    failAt = failure;
                    attempts = 0;
                    bool caught = false;
                    try
                    {
                        var iterator = left.{{operation}}{{(by ? "By" : "")}}(right{{(by ? ", SelectKey" : "")}}).GetEnumerator();
                        while (iterator.MoveNext()) { Key item = iterator.Current; }
                    }
                    catch (Exception* error) { caught = true; }
                    failAt = 0;
                    if (!caught) return 27;
                    if (live != initialLive || active != 0 || allocations - releases != initialStorage) return 28;
                }
                if (created != destroyed) return 29;
                return 0;
            }

            """;
    }
}
