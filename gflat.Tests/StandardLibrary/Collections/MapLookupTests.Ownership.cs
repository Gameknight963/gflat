namespace gflat.Tests.Std.Collections;

public sealed partial class MapLookupTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 2)]
    public void OwningResultsFallbacksAndCopyFailuresPreserveMapAndOutput(bool sorted, int optimization)
    {
        string map = sorted ? "SortedMap" : "HashMap";
        string constructor = sorted ? "new(Compare)" : "new()";
        Run($$"""
            using Standard;
            using Standard::Collections;
            using Standard::Text;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            int attempts = 0;
            int allocations = 0;
            int releases = 0;
            int failAt = 0;
            int mode = 0;
            int keys = 0;
            int owners = 0;
            int copies = 0;
            int defaults = 0;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes)
                {
                    attempts++;
                    if (attempts == failAt) return null;
                    void*? result = malloc(bytes);
                    if (result != null) allocations++;
                    return result;
                }
                public replace void Free(void* storage) nothrow { releases++; free(storage); }
            }
            struct Key : IHashable, IEquatable<Key>
            {
                public int Id;
                private String text;
                public Key(int value) { Id = value; text = s"key"; keys++; }
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
                ~Key() { keys--; }
            }
            int Compare(immutable(Key)* left, immutable(Key)* right)
            {
                if (mode == 1 || mode == 2) throw new Exception("comparison", 100 + mode);
                return left.Id - right.Id;
            }
            struct Owner
            {
                public int Id;
                public String Text;
                public Owner()
                {
                    Id = 0; Text = s"default"; defaults++;
                    if (mode == 4) throw new Exception("default", 104);
                    owners++;
                }
                public Owner(int value) { Id = value; Text = s"owned"; owners++; }
                public copy Owner(immutable(Owner)* source)
                {
                    Id = source.Id; Text = source.Text; copies++;
                    if (mode == 3) throw new Exception("copy", 103);
                    owners++;
                }
                ~Owner() { owners--; }
            }
            int main()
            {
                {
                    {{map}}<Key, Owner> map = {{constructor}};
                    map.Add(new Key(1), new Owner(11));
                    map.Add(new Key(2), new Owner(22));
                    immutable({{map}}<Key, Owner>)* view = &map;
                    Key present = new(1);
                    Key missing = new(99);
                    Owner output = new(77);
                    readonly(char)* original = output.Text.Data!;
                    immutable(Owner)* stored = view.FindImmutable(&present)!;
                    readonly(char)* storedText = stored.Text.Data!;
                    int before = attempts;
                    if (view.TryGetValue(&missing, ref output) || output.Id != 77 || output.Text.Data != original || attempts != before) return 1;
                    if (!view.TryGetValue(&present, ref output) || output.Id != 11 || output.Text.Data == storedText) return 2;
                    output.Text.Append("changed");
                    if (!stored.Text.Equals("owned")) return 3;
                    {
                        Owner fallback = new(33);
                        readonly(char)* fallbackText = fallback.Text.Data!;
                        before = copies;
                        Owner returned = view.GetValueOrDefault(&missing, move fallback);
                        if (returned.Id != 33 || returned.Text.Data != fallbackText || copies != before) return 4;
                    }
                    mode = 4;
                    {
                        Owner returned = view.GetValueOrDefault(&present);
                        if (returned.Id != 11 || defaults != 0 || returned.Text.Data == storedText) return 5;
                    }
                    bool caught = false;
                    try { Owner returned = view.GetValueOrDefault(&missing); }
                    catch (Exception* error) { caught = error.GetCode() == 104; }
                    mode = 0;
                    if (!caught || defaults != 1) return 6;
                    {
                        Owner returned = view.GetValueOrDefault(&missing);
                        if (returned.Id != 0 || !returned.Text.Equals("default") || defaults != 2) return 7;
                    }
                    for (int operation = 0; operation < 3; operation++)
                    for (int failure = 1; failure <= 3; failure++)
                    {
                        int memory = allocations - releases;
                        int living = owners;
                        original = output.Text.Data!;
                        mode = failure;
                        int code = 0;
                        try
                        {
                            if (operation == 0) view.TryGetValue(&present, ref output);
                            else if (operation == 1) { Owner returned = view.GetValueOrDefault(&present); }
                            else { Owner returned = view.GetValueOrDefault(&present, new Owner(33)); }
                        }
                        catch (Exception* error) { code = error.GetCode(); }
                        mode = 0;
                        if (code != 100 + failure || allocations - releases != memory || owners != living) return 8;
                        if (output.Id != 11 || output.Text.Data != original || stored.Text.Data != storedText || map.Count != 2) return 9;
                    }
                    int observed = 0;
                    attempts = 0;
                    if (!view.TryGetValue(&present, ref output)) return 10;
                    observed = attempts;
                    if (observed == 0) return 11;
                    for (int failure = 1; failure <= observed; failure++)
                    {
                        int memory = allocations - releases;
                        original = output.Text.Data!;
                        attempts = 0; failAt = failure; caught = false;
                        try { view.TryGetValue(&present, ref output); }
                        catch (Exception* error) { caught = true; }
                        failAt = 0;
                        if (!caught || allocations - releases != memory || output.Text.Data != original || output.Id != 11) return 12;
                        if (stored.Text.Data != storedText || !stored.Text.Equals("owned") || owners != 3 || keys != 4) return 13;
                    }
                    Owner* storedOutput = map.Find(&present)!;
                    if (!map.TryGetValue(&present, ref *storedOutput) || storedOutput.Id != 11 || !storedOutput.Text.Equals("owned")) return 14;
                    map.Clear();
                    if (keys != 2 || owners != 1 || output.Id != 11 || !output.Text.Equals("owned")) return 15;
                }
                if (keys != 0 || owners != 0 || allocations != releases) return 16;
                return 0;
            }
            """, sorted, optimization);
    }
}
