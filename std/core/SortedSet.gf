namespace std;

// Owns unique values ordered by the supplied comparison, rather than a hash.
struct SortedSet<T>
{
    private SortedMap<T, byte> storage;
    public SortedSet(int(readonly(T)*, readonly(T)*)* comparison)
    {
        storage = new SortedMap<T, byte>(comparison);
    }
    public nuint Count { readonly get nothrow => storage.Count; }
    public nuint Capacity { readonly get nothrow => storage.Capacity; }
    public bool IsEmpty { readonly get nothrow => storage.IsEmpty; }
    public void Reserve(nuint count) { storage.Reserve(count); }
    public bool Add(T value) => storage.TryAdd(move value, 0);
    public readonly bool Contains(readonly(T)* value) => storage.ContainsKey(value);
    public bool Remove(readonly(T)* value) => storage.Remove(value);
    public void Clear() { storage.Clear(); }
    public readonly Enumerator GetEnumerator() => new Enumerator(storage.GetEnumerator());

    public struct Enumerator
    {
        private SortedMap<T, byte>::Enumerator iterator;
        public Enumerator(SortedMap<T, byte>::Enumerator source) { iterator = source; }
        public bool MoveNext() => iterator.MoveNext();
        public readonly(T)* Current { readonly get => iterator.Current.Key; }
    }
}
