namespace gflat.Tests.Std.Queries;

public sealed partial class IndexQueryTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void IndexingIsLazyAndOwningItemsAndIteratorStorageCleanUpOnFailure(int optimization)
        => Run("""
            using Standard;
            using Standard::Collections;
            using Standard::Memory;
            using Standard::Queries;
            using Standard::Text;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            int attempts = 0;
            int allocated = 0;
            int released = 0;
            int failAt = 0;
            int mode = 0;
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
            struct Source
            {
                private Span<String> values;
                public Source(Span<String> input) { values = input; }
                public Cursor GetEnumerator() => new Cursor(values);
                public struct Cursor
                {
                    private Span<String> values;
                    private nuint position;
                    private String token;
                    public Cursor(Span<String> input)
                    {
                        values = input; position = 0; token = s"iterator";
                        if (mode == 1) throw new Exception("open", 101);
                        starts++;
                    }
                    public bool MoveNext()
                    {
                        moves++;
                        if (mode == 2 && moves == 2) throw new Exception("advance", 102);
                        if (position == values.Length) return false;
                        position++;
                        return true;
                    }
                    public String Current
                    {
                        get
                        {
                            reads++;
                            if (mode == 3 && reads == 2) throw new Exception("current", 103);
                            return values[position - 1];
                        }
                    }
                    ~Cursor() { ends++; }
                }
            }
            int main()
            {
                {
                    Vector<String> input = new();
                    input.Add(s"first"); input.Add(s"second"); input.Add(s"third");
                    int baseline = allocated - released;
                    int before = attempts;
                    var query = new Enumerable<String, Source, Source::Cursor>(new Source(input.AsSpan())).Index();
                    if (attempts != before || starts != 0 || moves != 0 || reads != 0) return 1;
                    {
                        var cursor = query.GetEnumerator();
                        if (starts != 1 || moves != 0 || reads != 0) return 2;
                        bool invalid = false;
                        try { Indexed<String> item = cursor.Current; }
                        catch (Exception* error) { invalid = true; }
                        if (!invalid || !cursor.MoveNext() || reads != 0) return 3;
                        Indexed<String> first = cursor.Current;
                        Indexed<String> again = cursor.Current;
                        Indexed<String> copied = first;
                        if (first.Index != 0 || again.Index != 0 || reads != 2 || first.Item.Data == again.Item.Data
                            || first.Item.Data == copied.Item.Data || first.Item.Data == input[0].Data) return 4;
                        first.Item.Append("changed");
                        if (!again.Item.Equals("first") || !copied.Item.Equals("first") || !input[0].Equals("first")) return 5;
                        if (!cursor.MoveNext() || !cursor.MoveNext() || cursor.MoveNext()) return 6;
                        int completedMoves = moves;
                        if (cursor.MoveNext() || moves != completedMoves) return 7;
                        invalid = false;
                        try { Indexed<String> item = cursor.Current; }
                        catch (Exception* error) { invalid = true; }
                        if (!invalid) return 8;
                    }
                    if (starts != ends || allocated - released != baseline) return 9;
                    reads = 0;
                    if (query.Count() != 3 || reads != 0 || starts != ends) return 10;
                    foreach (Indexed<String> item in query) break;
                    if (starts != ends || allocated - released != baseline) return 11;
                    for (int failure = 1; failure <= 3; failure++)
                    {
                        mode = failure; moves = 0; reads = 0;
                        int code = 0;
                        try
                        {
                            var cursor = query.GetEnumerator();
                            try
                            {
                                while (cursor.MoveNext()) { Indexed<String> item = cursor.Current; }
                            }
                            catch (Exception* error)
                            {
                                code = error.GetCode();
                                mode = 0;
                                if (failure == 2)
                                {
                                    int savedMoves = moves;
                                    if (cursor.MoveNext() || moves != savedMoves) return 12;
                                    bool invalid = false;
                                    try { Indexed<String> item = cursor.Current; }
                                    catch (Exception* ignored) { invalid = true; }
                                    if (!invalid) return 13;
                                }
                                else
                                {
                                    Indexed<String> retry = cursor.Current;
                                    if (retry.Index != 1 || !retry.Item.Equals("second")) return 14;
                                }
                            }
                        }
                        catch (Exception* error) { code = error.GetCode(); }
                        mode = 0;
                        if (code != 100 + failure || starts != ends || allocated - released != baseline) return 15;
                    }
                    attempts = 0;
                    int observed = 0;
                    {
                        Vector<Indexed<String>> snapshot = query.ToVector();
                        observed = attempts;
                        if (snapshot.Count != 3 || snapshot[2].Index != 2 || !snapshot[2].Item.Equals("third")) return 16;
                    }
                    for (int failure = 1; failure <= observed; failure++)
                    {
                        attempts = 0; failAt = failure;
                        bool caught = false;
                        try { Vector<Indexed<String>> snapshot = query.ToVector(); }
                        catch (Exception* error) { caught = true; }
                        failAt = 0;
                        if (!caught || starts != ends || allocated - released != baseline) return 17;
                    }
                    Vector<Indexed<String>> retained = query.ToVector();
                    input.Clear();
                    if (!retained[0].Item.Equals("first") || retained[1].Index != 1 || !retained[1].Item.Equals("second")) return 18;
                }
                if (allocated != released || starts != ends) return 19;
                return 0;
            }
            """, optimization);
}
