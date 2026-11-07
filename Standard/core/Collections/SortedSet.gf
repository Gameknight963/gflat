namespace Standard::Collections;

// Owns unique values ordered by the supplied comparison, rather than a hash.
struct SortedSet<T>
{
    private SortedMap<T, byte> storage;
    public SortedSet(int(immutable(T)*, immutable(T)*)* comparison) nothrow
    {
        storage = new SortedMap<T, byte>(comparison);
    }
    public nuint Count { immutable get nothrow => storage.Count; }
    public nuint Capacity { immutable get nothrow => storage.Capacity; }
    public bool IsEmpty { immutable get nothrow => storage.IsEmpty; }
    public void Reserve(nuint count) { storage.Reserve(count); }
    public bool Add(T value) => storage.TryAdd(move value, 0);
    public immutable bool Contains(immutable(T)* value) => storage.ContainsKey(value);
    // Borrows the stored comparison-equivalent value, or null when absent.
    public immutable immutable(T)*? Find(immutable(T)* value) => storage.FindKey(value);
    public bool Remove(immutable(T)* value) => storage.Remove(value);
    public void Clear() { storage.Clear(); }
    public immutable Enumerator GetEnumerator() => new Enumerator(storage.GetEnumerator());

    public immutable bool SetEquals(immutable(SortedSet<T>)* other)
    {
        if ((this).reinterpret<nuint>() == (other).reinterpret<nuint>()) return true;
        if (storage.HasSameComparison(&other.storage)) return this.Count == other.Count && this.IsSubsetOf(other);
        return this.IsSubsetOf(other) && this.IsSupersetOf(other);
    }

    public immutable bool IsSubsetOf(immutable(SortedSet<T>)* other)
    {
        if ((this).reinterpret<nuint>() == (other).reinterpret<nuint>() || this.IsEmpty) return true;
        if (this.Count > other.Count) return false;
        Enumerator left = this.GetEnumerator();
        if (storage.HasSameComparison(&other.storage))
        {
            Enumerator right = other.GetEnumerator();
            bool available = right.MoveNext();
            while (left.MoveNext())
            {
                while (available)
                {
                    int order = storage.CompareKeys(left.Current, right.Current);
                    if (order < 0) return false;
                    if (order == 0) break;
                    available = right.MoveNext();
                }
                if (!available) return false;
            }
            return true;
        }
        // The other set can contain multiple representatives of one of our keys.
        while (left.MoveNext())
        {
            bool found = false;
            Enumerator right = other.GetEnumerator();
            while (right.MoveNext())
            {
                if (storage.CompareKeys(left.Current, right.Current) == 0)
                {
                    found = true;
                    break;
                }
            }
            if (!found) return false;
        }
        return true;
    }

    public immutable bool IsSupersetOf(immutable(SortedSet<T>)* other)
    {
        if ((this).reinterpret<nuint>() == (other).reinterpret<nuint>()) return true;
        if (storage.HasSameComparison(&other.storage)) return other.IsSubsetOf(this);
        Enumerator iterator = other.GetEnumerator();
        while (iterator.MoveNext())
            if (!this.Contains(iterator.Current)) return false;
        return true;
    }

    public immutable bool IsProperSubsetOf(immutable(SortedSet<T>)* other)
    {
        if (this.Count >= other.Count) return false;
        if (storage.HasSameComparison(&other.storage)) return this.IsSubsetOf(other);
        if (!this.IsSubsetOf(other)) return false;
        Enumerator iterator = other.GetEnumerator();
        while (iterator.MoveNext())
            if (!this.Contains(iterator.Current)) return true;
        return false;
    }

    public immutable bool IsProperSupersetOf(immutable(SortedSet<T>)* other)
    {
        if (storage.HasSameComparison(&other.storage)) return this.Count > other.Count && this.IsSupersetOf(other);
        if (this.IsEmpty || !this.IsSupersetOf(other)) return false;
        return !this.IsSubsetOf(other);
    }

    public immutable bool Overlaps(immutable(SortedSet<T>)* other)
    {
        if (this.IsEmpty || other.IsEmpty) return false;
        if ((this).reinterpret<nuint>() == (other).reinterpret<nuint>()) return true;
        if (this.Count < other.Count && storage.HasSameComparison(&other.storage)) return other.Overlaps(this);
        Enumerator iterator = other.GetEnumerator();
        while (iterator.MoveNext())
            if (this.Contains(iterator.Current)) return true;
        return false;
    }

    public struct Enumerator
    {
        private SortedMap<T, byte>::Enumerator iterator;
        public Enumerator(SortedMap<T, byte>::Enumerator source) { iterator = source; }
        public bool MoveNext() => iterator.MoveNext();
        public immutable(T)* Current { immutable get => iterator.Current.Key; }
    }
}
