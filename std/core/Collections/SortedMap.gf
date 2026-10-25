namespace std;

// Owns sorted contiguous entries. Ordering equivalence identifies duplicate keys.
struct SortedMap<K, V>
{
    private Vector<Entry> storage;
    private int(immutable(K)*, immutable(K)*)* compare;

    public SortedMap(int(immutable(K)*, immutable(K)*)* comparison)
    {
        storage = new Vector<Entry>(); compare = comparison;
    }
    public nuint Count { immutable get nothrow => storage.Count; }
    public nuint Capacity { immutable get nothrow => storage.Capacity; }
    public bool IsEmpty { immutable get nothrow => storage.IsEmpty; }
    public void Reserve(nuint count) { storage.Reserve(count); }
    public void Clear() { storage.Clear(); }

    public struct Entry
    {
        private K key;
        private V value;
        public Entry(K item, V mapped) { key = move item; value = move mapped; }
        public immutable(K)* Key { immutable get nothrow => &key; }
        public ref V Value { immutable get nothrow => value; }
        public readonly(V)* ValueReadOnly { readonly get nothrow => &value; }
        public immutable(V)* ValueImmutable { immutable get nothrow => &value; }
    }

    private immutable nuint LowerBound(immutable(K)* key)
    {
        Span<immutable(Entry)> items = storage.AsImmutable();
        nuint first = 0; nuint count = items.Length;
        while (count != 0)
        {
            nuint step = count / 2; nuint middle = first + step;
            if (compare(items[middle].Key, key) < 0) { first = middle + 1; count -= step + 1; }
            else count = step;
        }
        return first;
    }

    private immutable bool Matches(nuint index, immutable(K)* key)
    {
        if (index == storage.Count) return false;
        Span<immutable(Entry)> items = storage.AsImmutable();
        return compare(items[index].Key, key) == 0;
    }

    public immutable bool ContainsKey(immutable(K)* key)
        => this.Matches(this.LowerBound(key), key);

    public V*? Find(immutable(K)* key)
    {
        nuint index = this.LowerBound(key);
        if (!this.Matches(index, key)) return null;
        return &storage[index].value;
    }

    public readonly readonly(V)*? FindReadOnly(immutable(K)* key)
    {
        nuint index = this.LowerBound(key);
        if (!this.Matches(index, key)) return null;
        Span<readonly(Entry)> items = storage.AsReadOnly();
        return items[index].ValueReadOnly;
    }

    public immutable immutable(V)*? FindImmutable(immutable(K)* key)
    {
        nuint index = this.LowerBound(key);
        if (!this.Matches(index, key)) return null;
        Span<immutable(Entry)> items = storage.AsImmutable();
        return items[index].ValueImmutable;
    }

    public bool TryAdd(K key, V value)
    {
        nuint index = this.LowerBound(&key);
        if (this.Matches(index, &key)) return false;
        storage.Insert(index, new Entry(move key, move value));
        return true;
    }

    public void Add(K key, V value)
    {
        if (!this.TryAdd(move key, move value)) throw new Exception("Duplicate SortedMap key");
    }

    public void Set(K key, V value)
    {
        nuint index = this.LowerBound(&key);
        if (this.Matches(index, &key)) { storage[index].value = move value; return; }
        storage.Insert(index, new Entry(move key, move value));
    }

    public ref V this[immutable(K)* key]
    {
        immutable get
        {
            nuint index = this.LowerBound(key);
            if (!this.Matches(index, key)) throw new Exception("SortedMap key not found");
            return storage[index].value;
        }
    }

    public bool Remove(immutable(K)* key)
    {
        nuint index = this.LowerBound(key);
        if (!this.Matches(index, key)) return false;
        storage.RemoveAt(index);
        return true;
    }

    // Transfer both owners without copying; the lookup borrow is not used after removal.
    public bool Remove(immutable(K)* key, ref V output)
    {
        nuint index = this.LowerBound(key);
        if (!this.Matches(index, key)) return false;
        Span<Entry> entries = storage.AsSpan();
        nuint start = (nuint)entries.Data!;
        nuint address = (nuint)&output;
        nuint stride = (nuint)sizeof(Entry);
        if (stride == 0) stride = 1;
        nuint bytes = storage.Capacity * stride;
        if (address >= start && (address - start < bytes
            || ((nuint)sizeof(V) == 0 && address - start == bytes)))
            throw new Exception("SortedMap output must not refer to its storage");
        V removed = move *(&entries[index].value);
        move *(&output) at &entries[index].value;
        move removed at &output;
        storage.RemoveAt(index);
        return true;
    }

    public Entry Extract(immutable(K)* key)
    {
        nuint index = this.LowerBound(key);
        if (!this.Matches(index, key)) throw new Exception("SortedMap key not found");
        return storage.RemoveAt(index);
    }

    public immutable Enumerator GetEnumerator() => new Enumerator(storage.AsImmutable());

    public struct Enumerator
    {
        private Span<immutable(Entry)>::Enumerator iterator;
        public Enumerator(Span<immutable(Entry)> items) { iterator = items.GetEnumerator(); }
        public bool MoveNext() => iterator.MoveNext();
        public immutable(Entry)* Current { immutable get => &iterator.Current; }
    }
}
