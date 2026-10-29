namespace std::Collections;

// Owns unique values. T must also implement IEquatable<T>.
struct HashSet<T : IHashable>
{
    private HashMap<T, byte> storage;
    public HashSet() { storage = new HashMap<T, byte>(); }
    public nuint Count { immutable get nothrow => storage.Count; }
    public nuint Capacity { immutable get nothrow => storage.Capacity; }
    public bool IsEmpty { immutable get nothrow => storage.IsEmpty; }
    public void Reserve(nuint count) { storage.Reserve(count); }
    public bool Add(T value) => storage.TryAdd(move value, 0);
    public immutable bool Contains(immutable(T)* value) => storage.ContainsKey(value);
    /// Borrows the stored equivalent value, or null when absent; no owner copies or library allocation.
    public immutable immutable(T)*? Find(immutable(T)* value) => storage.FindKey(value);
    public bool Remove(immutable(T)* value) => storage.Remove(value);
    public nuint RemoveWhere(bool(immutable(T)*)* predicate)
        => storage.RemoveKeysWhere(predicate);

    public void Clear() { storage.Clear(); }
    public void ExceptWith(immutable(HashSet<T>)* other)
    {
        if (this.Count == 0) return;
        if ((nuint)this == (nuint)other) { this.Clear(); return; }
        Enumerator iterator = other.GetEnumerator();
        while (iterator.MoveNext()) this.Remove(iterator.Current);
    }

    public void IntersectWith(immutable(HashSet<T>)* other)
    {
        storage.IntersectKeysWith(&other.storage);
    }
    public immutable Enumerator GetEnumerator() => new Enumerator(storage.GetEnumerator());

    public immutable bool SetEquals(immutable(HashSet<T>)* other)
    {
        return this.Count == other.Count && this.IsSubsetOf(other);
    }

    public immutable bool IsSubsetOf(immutable(HashSet<T>)* other)
    {
        if (this.Count > other.Count) return false;
        Enumerator iterator = this.GetEnumerator();
        while (iterator.MoveNext())
            if (!other.Contains(iterator.Current)) return false;
        return true;
    }

    public immutable bool IsSupersetOf(immutable(HashSet<T>)* other)
    {
        return other.IsSubsetOf(this);
    }

    public immutable bool IsProperSubsetOf(immutable(HashSet<T>)* other)
    {
        return this.Count < other.Count && this.IsSubsetOf(other);
    }

    public immutable bool IsProperSupersetOf(immutable(HashSet<T>)* other)
    {
        return this.Count > other.Count && this.IsSupersetOf(other);
    }

    public immutable bool Overlaps(immutable(HashSet<T>)* other)
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
        public immutable(T)* Current { immutable get => iterator.Current.Key; }
    }
}
