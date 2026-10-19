namespace std;

// Owns sorted contiguous entries. Ordering equivalence identifies duplicate keys.
struct SortedMap<K, V>
{
    private Vector<Entry> storage;
    private int(readonly(K)*, readonly(K)*)* compare;

    public SortedMap(int(readonly(K)*, readonly(K)*)* comparison)
    {
        storage = new Vector<Entry>(); compare = comparison;
    }
    public nuint Count { readonly get nothrow => storage.Count; }
    public nuint Capacity { readonly get nothrow => storage.Capacity; }
    public bool IsEmpty { readonly get nothrow => storage.IsEmpty; }
    public void Reserve(nuint count) { storage.Reserve(count); }
    public void Clear() { storage.Clear(); }

    public struct Entry
    {
        private K key;
        private V value;
        public Entry(K item, V mapped) { key = move item; value = move mapped; }
        public readonly(K)* Key { readonly get nothrow => &key; }
        public ref V Value { get nothrow => value; }
        public readonly(V)* ValueReadOnly { readonly get nothrow => &value; }
    }

    private readonly nuint LowerBound(readonly(K)* key)
    {
        Span<readonly(Entry)> items = storage.AsReadOnly();
        nuint first = 0; nuint count = items.Length;
        while (count != 0)
        {
            nuint step = count / 2; nuint middle = first + step;
            if (compare(items[middle].Key, key) < 0) { first = middle + 1; count -= step + 1; }
            else count = step;
        }
        return first;
    }

    private readonly bool Matches(nuint index, readonly(K)* key)
    {
        if (index == storage.Count) return false;
        Span<readonly(Entry)> items = storage.AsReadOnly();
        return compare(items[index].Key, key) == 0;
    }

    public readonly bool ContainsKey(readonly(K)* key)
        => this.Matches(this.LowerBound(key), key);

    public V*? Find(readonly(K)* key)
    {
        nuint index = this.LowerBound(key);
        if (!this.Matches(index, key)) return null;
        return &storage[index].value;
    }

    public readonly readonly(V)*? FindReadOnly(readonly(K)* key)
    {
        nuint index = this.LowerBound(key);
        if (!this.Matches(index, key)) return null;
        Span<readonly(Entry)> items = storage.AsReadOnly();
        return items[index].ValueReadOnly;
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

    public ref V this[readonly(K)* key]
    {
        get
        {
            nuint index = this.LowerBound(key);
            if (!this.Matches(index, key)) throw new Exception("SortedMap key not found");
            return storage[index].value;
        }
    }

    public bool Remove(readonly(K)* key)
    {
        nuint index = this.LowerBound(key);
        if (!this.Matches(index, key)) return false;
        storage.RemoveAt(index);
        return true;
    }

    public readonly Enumerator GetEnumerator() => new Enumerator(storage.AsReadOnly());

    public struct Enumerator
    {
        private Span<readonly(Entry)>::Enumerator iterator;
        public Enumerator(Span<readonly(Entry)> items) { iterator = items.GetEnumerator(); }
        public bool MoveNext() => iterator.MoveNext();
        public readonly(Entry)* Current { readonly get => &iterator.Current; }
    }
}
