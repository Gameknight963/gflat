namespace gflat.Tests.Std.Collections;

public sealed partial class SortedSetRelationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RelationsBorrowNoncopyableOwnersAndPreserveBothSetsAtEveryComparisonFailure(int optimization)
        => Run("""
            using Standard;
            using Standard::Collections;
            using Standard::Text;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            int live = 0;
            int attempts = 0;
            int allocated = 0;
            int released = 0;
            int calls = 0;
            int failAt = 0;
            bool deny = false;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes)
                {
                    attempts++;
                    if (deny) return null;
                    void*? result = malloc(bytes);
                    if (result != null) allocated++;
                    return result;
                }
                public replace void Free(void* storage) nothrow { released++; free(storage); }
            }
            struct Key
            {
                public int Id;
                public String Label;
                public Key(int id, char label) { Id = id; Label = String::From(&label, 1); live++; }
                ~Key() { live--; }
            }
            int Ascending(immutable(Key)* left, immutable(Key)* right)
            {
                calls++;
                if (calls == failAt) throw new Exception("comparison", 91);
                return left.Id - right.Id;
            }
            int Descending(immutable(Key)* left, immutable(Key)* right) => -Ascending(left, right);
            bool Check(int operation, immutable(SortedSet<Key>)* left, immutable(SortedSet<Key>)* right)
            {
                if (operation == 0) return left.SetEquals(right);
                if (operation == 1) return left.IsSubsetOf(right);
                if (operation == 2) return left.IsProperSubsetOf(right);
                if (operation == 3) return left.IsSupersetOf(right);
                if (operation == 4) return left.IsProperSupersetOf(right);
                return left.Overlaps(right);
            }
            nuint Fingerprint(immutable(SortedSet<Key>)* source)
            {
                nuint result = source.Count;
                var iterator = source.GetEnumerator();
                while (iterator.MoveNext())
                {
                    immutable(Key)* value = iterator.Current;
                    result = result ^ value.reinterpret<nuint>() ^ value.Label.Data.reinterpret<nuint>() ^ (nuint)value.Id;
                }
                return result;
            }
            int main()
            {
                for (int different = 0; different < 2; different++)
                for (int profile = 0; profile < 3; profile++)
                {
                    SortedSet<Key> left = new(Ascending);
                    SortedSet<Key> right = new(Ascending);
                    if (different == 1) right = new SortedSet<Key>(Descending);
                    for (int i = 0; i < 4; i++) left.Add(new Key(i * 2, 'L'));
                    if (profile == 0)
                    {
                        for (int i = 0; i < 8; i++) right.Add(new Key(i, 'R'));
                    }
                    else
                    {
                        for (int i = 0; i < 4; i++)
                        {
                            int id = i * 2;
                            if (profile == 1 && i == 3) id = 7;
                            right.Add(new Key(id, 'R'));
                        }
                    }
                    nuint leftBefore = Fingerprint(&left);
                    nuint rightBefore = Fingerprint(&right);
                    int owners = live;
                    int memory = allocated - released;
                    for (int operation = 0; operation < 6; operation++)
                    {
                        calls = 0;
                        int before = attempts;
                        deny = true;
                        Check(operation, &left, &right);
                        deny = false;
                        int observed = calls;
                        if (attempts != before || live != owners || allocated - released != memory) return 1;
                        if (Fingerprint(&left) != leftBefore || Fingerprint(&right) != rightBefore) return 2;
                        for (int failure = 1; failure <= observed; failure++)
                        {
                            calls = 0; failAt = failure;
                            int code = 0;
                            try { Check(operation, &left, &right); }
                            catch (Exception* error) { code = error.GetCode(); }
                            failAt = 0;
                            if (code != 91 || calls != failure || live != owners || allocated - released != memory) return 3;
                            if (Fingerprint(&left) != leftBefore || Fingerprint(&right) != rightBefore) return 4;
                        }
                    }
                }
                {
                    SortedSet<Key> left = new(Ascending);
                    SortedSet<Key> right = new(Ascending);
                    for (int i = 0; i < 128; i++)
                    {
                        right.Add(new Key(i, 'R'));
                        if (i % 2 == 0) left.Add(new Key(i, 'L'));
                    }
                    calls = 0; deny = true;
                    if (!left.IsSubsetOf(&right) || calls > 2 * 192) return 5;
                    calls = 0;
                    if (!left.IsProperSubsetOf(&right) || calls > 2 * 192) return 6;
                    calls = 0;
                    if (!right.IsSupersetOf(&left) || calls > 2 * 192) return 7;
                    deny = false;
                }
                if (live != 0 || allocated != released) return 8;
                return 0;
            }
            """, optimization);
}
