namespace gflat.Tests.Std.Collections;

public sealed partial class LinkedListSearchTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SearchesBorrowNoncopyableElementsAndPreserveLinksAtEveryComparisonFailure(int optimization)
        => Run("""
            using Standard;
            using Standard::Collections;
            using Standard::Text;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            int attempts = 0;
            int allocated = 0;
            int released = 0;
            int live = 0;
            int calls = 0;
            int failAt = 0;
            bool deny = false;
            bool wrongBorrow = false;
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
            LinkedList<Key>*? root = null;
            immutable(Key)*? expectedQuery = null;
            bool Equal(immutable(Key)* left, immutable(Key)* right)
            {
                calls++;
                if (calls == failAt) throw new Exception("compare", 19);
                bool found = false;
                LinkedList<Key>::Node*? node = root!.First;
                while (node != null)
                {
                    if (&node!.Value == left) { found = true; break; }
                    node = node!.Next;
                }
                if (!found || right != expectedQuery) wrongBorrow = true;
                return left.Id == right.Id;
            }
            bool Check(int operation, LinkedList<Key>* list, immutable(Key)* key)
            {
                if (operation == 0) return list.Find(key, Equal) != null;
                if (operation == 1) return list.FindLast(key, Equal) != null;
                if (operation == 2) return list.Contains(key, Equal);
                return list.RemoveValue(key, Equal);
            }
            int main()
            {
                {
                    LinkedList<Key> list = new();
                    root = &list;
                    Key key = new(3, 'Q');
                    expectedQuery = &key;
                    calls = 0; deny = true;
                    for (int operation = 0; operation < 4; operation++)
                        if (Check(operation, &list, &key)) return 1;
                    deny = false;
                    if (calls != 0) return 2;
                    LinkedList<Key>::Node* a = list.AddLast(new Key(1, 'A'));
                    LinkedList<Key>::Node* b = list.AddLast(new Key(2, 'B'));
                    LinkedList<Key>::Node* c = list.AddLast(new Key(1, 'C'));
                    LinkedList<Key>::Node* d = list.AddLast(new Key(3, 'D'));
                    LinkedList<Key>::Node* e = list.AddLast(new Key(1, 'E'));
                    int memory = allocated - released;
                    int[4] observed = [4, 2, 4, 4];
                    for (int operation = 0; operation < 4; operation++)
                    for (int failure = 1; failure <= observed[operation]; failure++)
                    {
                        calls = 0; failAt = failure;
                        int code = 0;
                        try { Check(operation, &list, &key); }
                        catch (Exception* error) { code = error.GetCode(); }
                        failAt = 0;
                        if (code != 19 || calls != failure || list.Count != 5 || live != 6 || allocated - released != memory) return 3;
                        if (list.First != a || list.Last != e || a.Next != b || b.Previous != a || b.Next != c
                            || c.Previous != b || c.Next != d || d.Previous != c || d.Next != e || e.Previous != d) return 4;
                        if (!a.Value.Label.Equals("A") || !e.Value.Label.Equals("E")) return 5;
                    }
                    key.Id = 1;
                    int before = attempts;
                    deny = true; calls = 0;
                    if (list.Find(&key, Equal) != a || calls != 1) return 6;
                    calls = 0;
                    if (list.FindLast(&key, Equal) != e || calls != 1) return 7;
                    immutable(LinkedList<Key>)* view = &list;
                    calls = 0;
                    if (!view.Contains(&key, Equal) || calls != 1) return 8;
                    expectedQuery = &e.Value;
                    if (!list.RemoveValue(&e.Value, Equal) || list.First != b || live != 5) return 9;
                    LinkedList<Key> moved = move list;
                    root = &moved;
                    if (moved.FindLast(&e.Value, Equal) != e) return 10;
                    if (!moved.RemoveValue(&e.Value, Equal) || live != 4) return 11;
                    if (!moved.RemoveValue(&e.Value, Equal) || live != 3 || moved.Count != 2) return 12;
                    expectedQuery = &key;
                    key.Id = 99;
                    if (moved.Find(&key, Equal) != null || moved.FindLast(&key, Equal) != null || moved.RemoveValue(&key, Equal)) return 13;
                    deny = false;
                    if (attempts != before || wrongBorrow) return 14;
                    Key removed = moved.Remove(b);
                    if (removed.Id != 2 || moved.First != d || moved.Count != 1) return 15;
                }
                if (live != 0 || allocated != released || wrongBorrow) return 16;
                return 0;
            }
            """, optimization);
}
