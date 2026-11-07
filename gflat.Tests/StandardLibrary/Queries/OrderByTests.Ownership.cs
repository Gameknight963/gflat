namespace gflat.Tests.Std.Queries;

public sealed partial class OrderByTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NoncopyableKeysAndOwningValuesCleanUpOnEveryObservedAllocationAndCallbackFailure(int optimization)
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
            int keys = 0;
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
            struct Item
            {
                public int Id;
                public String Text;
                public Item(int id) { Id = id; Text = s"item"; owners++; }
                public copy Item(immutable(Item)* source)
                {
                    Id = source.Id;
                    Text = source.Text;
                    if (mode == 3 && Id == 2) throw new Exception("copy", 23);
                    owners++;
                }
                ~Item() { owners--; }
            }
            struct Key
            {
                public int Value;
                private String text;
                public Key(int value) { Value = value; text = s"key"; keys++; }
                public immutable int CompareTo(immutable(Key)* other)
                {
                    if (mode == 2) throw new Exception("compare", 22);
                    return Value - other.Value;
                }
                ~Key() { keys--; }
            }
            Key Project(Item value)
            {
                if (mode == 1 && value.Id == 4) throw new Exception("selector", 21);
                return new Key(value.Id % 3);
            }
            int CompareKeys(immutable(Key)* left, immutable(Key)* right) => left.CompareTo(right);
            int main()
            {
                {
                    Vector<Item> input = new();
                    for (int i = 0; i < 9; i++) input.Add(new Item(i));
                    var query = input.AsEnumerable().OrderBy(Project, CompareKeys);
                    int observed = 0;
                    int baseline = allocated - released;
                    {
                        var cursor = query.GetEnumerator();
                        attempts = 0;
                        if (!cursor.MoveNext()) return 1;
                        observed = attempts;
                        if (keys != 0 || owners != 18 || cursor.Current.Id != 0) return 2;
                        readonly(char)* original = input[0].Text.Data!;
                        if (cursor.Current.Text.Data == original) return 3;
                        input[0].Text.Append("changed");
                        if (!cursor.Current.Text.Equals("item")) return 4;
                        int[9] expected = [0, 3, 6, 1, 4, 7, 2, 5, 8];
                        deny = true;
                        for (nuint i = 0; i < 9; i++)
                        {
                            if (cursor.Current.Id != expected[i]) return 5;
                            bool next = cursor.MoveNext();
                            if (next != (i < 8)) return 6;
                        }
                        deny = false;
                        if (owners != 9 || keys != 0) return 7;
                    }
                    if (allocated - released != baseline || observed == 0) return 8;
                    for (int failure = 1; failure <= observed + 3; failure++)
                    {
                        {
                            var cursor = query.GetEnumerator();
                            attempts = 0;
                            if (failure <= observed) failAt = failure;
                            else mode = failure - observed;
                            int code = 0;
                            bool caught = false;
                            try { cursor.MoveNext(); }
                            catch (Exception* error) { caught = true; code = error.GetCode(); }
                            failAt = 0;
                            mode = 0;
                            if (!caught || cursor.MoveNext() || owners != 9 || keys != 0) return 9;
                            if (failure > observed && code != 20 + failure - observed) return 10;
                            bool invalid = false;
                            try { cursor.Current; } catch (Exception* error) { invalid = true; }
                            if (!invalid) return 11;
                        }
                        if (allocated - released != baseline || owners != 9 || keys != 0) return 12;
                    }
                    {
                        var cursor = query.GetEnumerator();
                        if (!cursor.MoveNext() || owners != 18) return 13;
                    }
                    if (owners != 9 || keys != 0 || allocated - released != baseline) return 14;
                }
                if (owners != 0 || keys != 0 || allocated != released) return 15;
                return 0;
            }
            """, optimization);
}
