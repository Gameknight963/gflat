namespace std::Collections;

// Owns unique values ordered by the supplied comparison, rather than a hash.
struct SortedSet<T>
{
    private SortedMap<T, byte> storage;
    public SortedSet(int(immutable(T)*, immutable(T)*)* comparison)
    {
        storage = new SortedMap<T, byte>(comparison);
    }
    public nuint Count { immutable get nothrow => storage.Count; }
    public nuint Capacity { immutable get nothrow => storage.Capacity; }
    public bool IsEmpty { immutable get nothrow => storage.IsEmpty; }
    public void Reserve(nuint count) { storage.Reserve(count); }
    public bool Add(T value) => storage.TryAdd(move value, 0);
    public immutable bool Contains(immutable(T)* value) => storage.ContainsKey(value);
    /// Borrows the stored comparison-equivalent value, or null; no owner copies or library allocation.
    public immutable immutable(T)*? Find(immutable(T)* value) => storage.FindKey(value);
    public bool Remove(immutable(T)* value) => storage.Remove(value);
    public void Clear() { storage.Clear(); }
    public immutable Enumerator GetEnumerator() => new Enumerator(storage.GetEnumerator());

    public struct Enumerator
    {
        private SortedMap<T, byte>::Enumerator iterator;
        public Enumerator(SortedMap<T, byte>::Enumerator source) { iterator = source; }
        public bool MoveNext() => iterator.MoveNext();
        public immutable(T)* Current { immutable get => iterator.Current.Key; }
    }
}
