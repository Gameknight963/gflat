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
    public ref immutable(T) Min
    {
        immutable get
        {
            if (storage.IsEmpty) throw new Exception("Cannot get the minimum of an empty sorted set");
            return *storage.KeyAt(0);
        }
    }
    public ref immutable(T) Max
    {
        immutable get
        {
            if (storage.IsEmpty) throw new Exception("Cannot get the maximum of an empty sorted set");
            return *storage.KeyAt(storage.Count - 1);
        }
    }
    public void Reserve(nuint count) { storage.Reserve(count); }
    public nuint EnsureCapacity(nuint requested) => storage.EnsureCapacity(requested);
    public void TrimExcess() { storage.TrimExcess(); }
    public void ShrinkToFit() { storage.ShrinkToFit(); }
    public bool Add(T value) => storage.TryAdd(move value, 0);
    public immutable bool Contains(immutable(T)* value) => storage.ContainsKey(value);
    // Borrows the stored comparison-equivalent value, or null when absent.
    public immutable immutable(T)*? Find(immutable(T)* value) => storage.FindKey(value);
    public bool Remove(immutable(T)* value) => storage.Remove(value);
    internal immutable bool CopyDestinationOverlaps(T* output, nuint length) nothrow
        => storage.CopyKeyDestinationOverlaps(output, length);

    public void Clear() { storage.Clear(); }

    public void ExceptWith(immutable(SortedSet<T>)* other)
    {
        if ((this).reinterpret<nuint>() == (other).reinterpret<nuint>())
        {
            this.Clear();
            return;
        }
        Enumerator iterator = other.GetEnumerator();
        while (iterator.MoveNext()) this.Remove(iterator.Current);
    }

    public void IntersectWith(immutable(SortedSet<T>)* other)
    {
        if ((this).reinterpret<nuint>() == (other).reinterpret<nuint>()) return;
        if (other.IsEmpty)
        {
            this.Clear();
            return;
        }
        bool same = storage.HasSameComparison(&other.storage);
        nuint index = 0;
        while (index < this.Count)
        {
            immutable(T)* key = storage.KeyAt(index);
            bool keep = false;
            if (same) keep = other.Contains(key);
            else
            {
                Enumerator iterator = other.GetEnumerator();
                while (iterator.MoveNext())
                {
                    if (storage.CompareKeys(key, iterator.Current) == 0)
                    {
                        keep = true;
                        break;
                    }
                }
            }
            if (keep) index++;
            else storage.RemoveAt(index);
        }
    }

    public nuint RemoveWhere(bool(immutable(T)*)* predicate)
    {
        nuint removed = 0;
        nuint index = 0;
        while (index < this.Count)
        {
            if (predicate(storage.KeyAt(index)))
            {
                storage.RemoveAt(index);
                removed++;
            }
            else index++;
        }
        return removed;
    }

    internal immutable bool HasEquivalentBefore(immutable(SortedSet<T>)* source, immutable(T)* key)
    {
        if (storage.HasSameComparison(&source.storage)) return false;
        Enumerator iterator = source.GetEnumerator();
        while (iterator.MoveNext())
        {
            immutable(T)* earlier = iterator.Current;
            if (earlier == key) return false;
            if (storage.CompareKeys(earlier, key) == 0) return true;
        }
        return false;
    }

    public immutable Enumerator GetEnumerator() => new Enumerator(storage.GetEnumerator());

    // Borrows the set object. New enumerators use its current storage.
    public struct View
    {
        private immutable(SortedSet<T>)* source;
        public View(immutable(SortedSet<T>)* owner) nothrow { source = owner; }
        public immutable Enumerator GetEnumerator() => source.GetEnumerator();
    }

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
