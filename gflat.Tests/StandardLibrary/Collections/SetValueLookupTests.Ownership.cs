namespace gflat.Tests.Std.Collections;

public sealed partial class SetValueLookupTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 2)]
    public void CanonicalOwningCopiesPreserveOutputsThroughLookupCopyAndAllocationFailures(bool sorted, int optimization)
    {
        string kind = sorted ? "SortedSet" : "HashSet";
        string constructor = sorted ? "new(Compare)" : "new()";
        Run($$"""
            using Standard;
            using Standard::Collections;
            using Standard::Text;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            int live = 0;
            int attempts = 0;
            int allocated = 0;
            int released = 0;
            int failAt = 0;
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
            struct Key : IHashable, IEquatable<Key>
            {
                public int Id;
                public String Label;
                public Key(int id, char label) { Id = id; Label = String::From(&label, 1); live++; }
                public copy Key(immutable(Key)* source)
                {
                    Id = source.Id; Label = source.Label;
                    if (mode == 3) throw new Exception("copy", 103);
                    live++;
                }
                public immutable nuint GetHashCode()
                {
                    if (mode == 1) throw new Exception("hash", 101);
                    return 0;
                }
                public immutable bool Equals(immutable(Key)* other)
                {
                    if (mode == 2) throw new Exception("equality", 102);
                    return Id == other.Id;
                }
                ~Key() { live--; }
            }
            int Compare(immutable(Key)* left, immutable(Key)* right)
            {
                if (mode == 1 || mode == 2) throw new Exception("compare", 100 + mode);
                return left.Id - right.Id;
            }
            int main()
            {
                {
                    {{kind}}<Key> values = {{constructor}};
                    Key query = new(1, 'Q');
                    Key missing = new(99, 'Z');
                    Key output = new(77, 'O');
                    readonly(char)* original = output.Label.Data!;
                    int before = attempts;
                    deny = true; mode = 3;
                    if (values.TryGetValue(&query, ref output) || output.Id != 77 || output.Label.Data != original) return 1;
                    deny = false; mode = 0;
                    if (attempts != before) return 2;
                    values.Add(new Key(1, 'A'));
                    values.Add(new Key(1, 'B'));
                    values.Add(new Key(2, 'C'));
                    immutable(Key)* stored = values.Find(&query)!;
                    readonly(char)* storedText = stored.Label.Data!;
                    if (!values.TryGetValue(&query, ref output) || output.Id != 1 || !output.Label.Equals("A") || output.Label.Data == storedText) return 3;
                    output.Label.Append("changed");
                    if (!stored.Label.Equals("A")) return 4;
                    for (int failure = 1; failure <= 3; failure++)
                    {
                        original = output.Label.Data!;
                        int memory = allocated - released;
                        mode = failure;
                        int code = 0;
                        try { values.TryGetValue(&query, ref output); }
                        catch (Exception* error) { code = error.GetCode(); }
                        mode = 0;
                        if (code != 100 + failure || output.Label.Data != original || output.Id != 1 || live != 5 || allocated - released != memory) return 5;
                        if (values.Count != 2 || stored.Label.Data != storedText) return 6;
                    }
                    attempts = 0;
                    if (!values.TryGetValue(&query, ref output)) return 7;
                    int observed = attempts;
                    if (observed == 0) return 8;
                    for (int failure = 1; failure <= observed; failure++)
                    {
                        original = output.Label.Data!;
                        int memory = allocated - released;
                        attempts = 0; failAt = failure;
                        bool caught = false;
                        try { values.TryGetValue(&query, ref output); }
                        catch (Exception* error) { caught = true; }
                        failAt = 0;
                        if (!caught || output.Label.Data != original || output.Id != 1 || live != 5 || allocated - released != memory) return 9;
                    }
                    before = attempts;
                    deny = true;
                    original = output.Label.Data!;
                    if (values.TryGetValue(&missing, ref output) || output.Label.Data != original || attempts != before) return 10;
                    deny = false;
                    output.Id = 1;
                    if (!values.TryGetValue(&output, ref output) || !output.Label.Equals("A")) return 11;
                    values.Clear();
                    if (!output.Label.Equals("A") || live != 3) return 12;
                }
                if (live != 0 || allocated != released) return 13;
                return 0;
            }
            """, sorted, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SortedEndpointsBorrowNoncopyableValuesInComparerOrderAndRejectEmptySets(int optimization)
        => Run("""
            using Standard;
            using Standard::Collections;
            using Standard::Text;

            int live = 0;
            bool fail = false;
            struct Owner
            {
                public int Id;
                private String text;
                public Owner(int id) { Id = id; text = s"owned"; live++; }
                ~Owner() { live--; }
            }
            int Descending(immutable(Owner)* left, immutable(Owner)* right)
            {
                if (fail) throw new Exception("unexpected comparison");
                return right.Id - left.Id;
            }
            int main()
            {
                {
                    SortedSet<Owner> values = new(Descending);
                    bool caught = false;
                    try { int value = values.Min.Id; }
                    catch (Exception* error) { caught = true; }
                    if (!caught) return 1;
                    caught = false;
                    try { int value = values.Max.Id; }
                    catch (Exception* error) { caught = true; }
                    if (!caught) return 2;
                    values.Add(new Owner(3)); values.Add(new Owner(1)); values.Add(new Owner(5));
                    immutable(SortedSet<Owner>)* view = &values;
                    fail = true;
                    immutable(Owner)* first = &view.Min;
                    immutable(Owner)* last = &view.Max;
                    if (first.Id != 5 || last.Id != 1 || live != 3) return 3;
                    fail = false;
                    Owner key = new(5);
                    if (values.Find(&key) != first) return 4;
                    values.Remove(&key);
                    if (values.Min.Id != 3 || values.Max.Id != 1 || live != 3) return 5;
                    values.Clear();
                    if (live != 1) return 6;
                }
                return live;
            }
            """, true, optimization);

    [Fact]
    public void SortedEndpointsCannotMutateStoredKeys()
    {
        var error = Assert.Throws<CompileExceptions.TypeCheckException>(() => Compiler.Emit(Sources("""
            using Standard::Collections;

            int Compare(immutable(int)* left, immutable(int)* right) => *left - *right;
            int main()
            {
                SortedSet<int> values = new(Compare);
                values.Add(1);
                values.Min = 3;
                return 0;
            }
            """, true)));
        Assert.Matches("(?i)readonly|immutable", error.Message);
    }
}
