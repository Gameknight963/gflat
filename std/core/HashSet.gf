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
    public readonly Enumerator GetEnumerator() => new Enumerator(storage.GetEnumerator());

    public readonly bool SetEquals(readonly(HashSet<T>)* other)
    {
        return this.Count == other.Count && this.IsSubsetOf(other);
    }

    public readonly bool IsSubsetOf(readonly(HashSet<T>)* other)
    {
        if (this.Count > other.Count) return false;
        Enumerator iterator = this.GetEnumerator();
        while (iterator.MoveNext())
            if (!other.Contains(iterator.Current)) return false;
        return true;
    }

    public readonly bool IsSupersetOf(readonly(HashSet<T>)* other)
    {
        return other.IsSubsetOf(this);
    }

    public readonly bool IsProperSubsetOf(readonly(HashSet<T>)* other)
    {
        return this.Count < other.Count && this.IsSubsetOf(other);
    }

    public readonly bool IsProperSupersetOf(readonly(HashSet<T>)* other)
    {
        return this.Count > other.Count && this.IsSupersetOf(other);
    }

    public readonly bool Overlaps(readonly(HashSet<T>)* other)
    {
        if (this.Count == 0 || other.Count == 0) return false;
        // Probe the larger set while visiting only the smaller set's elements.
        if (this.Count > other.Count) return other.Overlaps(this);
        Enumerator iterator = this.GetEnumerator();
        while (iterator.MoveNext())
            if (other.Contains(iterator.Current)) return true;
        return false;
    }

    public struct Enumerator
    {
        private HashMap<T, byte>::Enumerator iterator;
        public Enumerator(HashMap<T, byte>::Enumerator source) { iterator = source; }
        public bool MoveNext() => iterator.MoveNext();
        public readonly(T)* Current { readonly get => iterator.Current.Key; }
    }
}
