namespace std;

// Owns unique values. T must also implement IEquatable<T>.
struct HashSet<T : IHashable>
{
    private HashMap<T, byte> storage;
    public HashSet() { storage = new HashMap<T, byte>(); }
    public nuint Count { readonly get => storage.Count; }
    public nuint Capacity { readonly get => storage.Capacity; }
    public bool IsEmpty { readonly get => storage.IsEmpty; }
    public void Reserve(nuint count) { storage.Reserve(count); }
    public bool Add(T value) => storage.TryAdd(move value, 0);
    public readonly bool Contains(readonly(T)* value) => storage.ContainsKey(value);
    public bool Remove(readonly(T)* value) => storage.Remove(value);
    public void Clear() { storage.Clear(); }
    public Enumerator GetEnumerator() => new Enumerator(storage.GetEnumerator());

    public struct Enumerator
    {
        private HashMap<T, byte>::Enumerator iterator;
        public Enumerator(HashMap<T, byte>::Enumerator source) { iterator = source; }
        public bool MoveNext() => iterator.MoveNext();
        public readonly(T)* Current { readonly get => iterator.Current.Key; }
    }
}
