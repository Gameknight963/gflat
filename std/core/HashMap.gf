namespace std;

namespace hash_detail
{
    // Kept separately because a generic parameter currently has one interface constraint.
    bool Equal<T : IEquatable<T>>(immutable(T)* left, immutable(T)* right)
        => left.Equals(right);
}

// Owns keys and values. Keys must also implement IEquatable<K>.
// Lookups borrow keys; insertion/Reserve can rehash and invalidate borrowed views.
struct HashMap<K : IHashable, V>
{
    private Entry*? entries;
    private byte*? states;
    private nuint capacity;
    private nuint count;
    private nuint used;

    public HashMap() { entries = null; states = null; capacity = 0; count = 0; used = 0; }
    public nuint Count { immutable get nothrow => count; }
    public nuint Capacity { immutable get nothrow => capacity / 2; }
    public bool IsEmpty { immutable get nothrow => count == 0; }

    // Keys are exposed through an immutable view so their hash cannot be changed.
    public struct Entry
    {
        private K key;
        private V value;
        private nuint hash;
        public Entry(K item, V mapped, nuint code) { key = move item; value = move mapped; hash = code; }
        public immutable(K)* Key { immutable get => &key; }
        public ref V Value { immutable get => value; }
        public readonly(V)* ValueReadOnly { readonly get => &value; }
        public immutable(V)* ValueImmutable { immutable get => &value; }
    }

    private immutable nuint Locate(immutable(K)* key, nuint hash)
    {
        if (capacity == 0) return capacity;
        nuint slot = hash % capacity;
        for (nuint visited = 0; visited < capacity; visited++)
        {
            if (states![slot] == 0) return capacity;
            if (states![slot] == 1 && entries![slot].hash == hash
                && hash_detail::Equal<K>(&entries![slot].key, key)) return slot;
            slot++;
            if (slot == capacity) slot = 0;
        }
        return capacity;
    }

    public immutable bool ContainsKey(immutable(K)* key)
        => this.Locate(key, key.GetHashCode()) != capacity;

    public V*? Find(immutable(K)* key)
    {
        nuint slot = this.Locate(key, key.GetHashCode());
        if (slot == capacity) return null;
        return &entries![slot].value;
    }

    public readonly readonly(V)*? FindReadOnly(immutable(K)* key)
    {
        nuint slot = this.Locate(key, key.GetHashCode());
        if (slot == capacity) return null;
        return &entries![slot].value;
    }

    public immutable immutable(V)*? FindImmutable(immutable(K)* key)
    {
        nuint slot = this.Locate(key, key.GetHashCode());
        if (slot == capacity) return null;
        return &entries![slot].value;
    }

    // requested is an entry count, not a bucket count. Keep at least half the buckets empty.
    public void Reserve(nuint requested)
    {
        if (requested <= capacity / 2) return;
        nuint maximum = (nuint)0 - (nuint)1;
        nuint stride = (nuint)sizeof(Entry);
        if (requested > maximum / (stride + 1) / 2) throw new Exception("HashMap capacity overflow");
        nuint size = requested * 2;
        if (size < 8) size = 8;
        this.Rehash(size);
    }

    private void Rehash(nuint size)
    {
        nuint stride = (nuint)sizeof(Entry);
        void*? allocation = Memory::Allocate(size * (stride + 1));
        if (allocation == null) throw new BadAllocException("HashMap allocation failed");
        Entry* replacement = (Entry*)allocation;
        byte* flags = (byte*)allocation + size * stride;
        for (nuint i = 0; i < size; i++) flags[i] = 0;
        for (nuint i = 0; i < capacity; i++)
        {
            if (states![i] == 1)
            {
                nuint slot = entries![i].hash % size;
                while (flags[slot] != 0) { slot++; if (slot == size) slot = 0; }
                move *(entries! + i) at replacement + slot;
                flags[slot] = 1;
            }
        }
        if (entries != null) Memory::Free((void*)entries);
        entries = replacement; states = flags; capacity = size; used = count;
    }

    // Both arguments transfer into a new entry. On duplicates they are destroyed normally.
    public bool TryAdd(K key, V value)
    {
        nuint hash = key.GetHashCode();
        if (this.Locate(&key, hash) != capacity) return false;
        nuint available = capacity / 2;
        if (count == available)
        {
            nuint maximum = ((nuint)0 - (nuint)1) / ((nuint)sizeof(Entry) + 1) / 2;
            if (count == maximum) throw new Exception("HashMap capacity overflow");
            nuint grown = 4;
            if (available != 0)
            {
                grown = maximum;
                if (available <= maximum / 2) grown = available * 2;
            }
            if (grown > maximum) grown = maximum;
            this.Reserve(grown);
        }
        else if (used >= capacity - capacity / 4) this.Rehash(capacity);
        nuint slot = hash % capacity;
        while (states![slot] == 1) { slot++; if (slot == capacity) slot = 0; }
        bool fresh = states![slot] == 0;
        new* Entry(move key, move value, hash) at entries! + slot;
        states![slot] = 1;
        count++;
        if (fresh) used++;
        return true;
    }

    public void Add(K key, V value)
    {
        if (!this.TryAdd(move key, move value)) throw new Exception("Duplicate HashMap key");
    }

    public void Set(K key, V value)
    {
        nuint slot = this.Locate(&key, key.GetHashCode());
        if (slot != capacity) { entries![slot].value = move value; return; }
        this.Add(move key, move value);
    }

    public ref V this[immutable(K)* key]
    {
        immutable get
        {
            nuint slot = this.Locate(key, key.GetHashCode());
            if (slot == capacity) throw new Exception("HashMap key not found");
            return entries![slot].value;
        }
    }

    public bool Remove(immutable(K)* key)
    {
        nuint slot = this.Locate(key, key.GetHashCode());
        if (slot == capacity) return false;
        (entries! + slot).~Entry();
        states![slot] = 2;
        count--;
        return true;
    }

    // Transfer both owners out of the occupied slot, leaving a probe tombstone.
    public Entry Extract(immutable(K)* key)
    {
        nuint slot = this.Locate(key, key.GetHashCode());
        if (slot == capacity) throw new Exception("HashMap key not found");
        Entry result = move *(entries! + slot);
        states![slot] = 2;
        count--;
        return result;
    }

    // Internal bucket mutation: never uses an enumerator over the changing map.
    internal void IntersectKeysWith(immutable(HashMap<K, V>)* other)
    {
        if (count == 0 || (nuint)this == (nuint)other) return;
        if (other.Count == 0) { this.Clear(); return; }
        for (nuint slot = 0; slot < capacity; slot++)
        {
            if (states![slot] == 1 && !other.ContainsKey(&entries![slot].key))
            {
                (entries! + slot).~Entry();
                states![slot] = 2;
                count--;
            }
        }
    }

    public void Clear()
    {
        for (nuint i = 0; i < capacity; i++)
        {
            if (states![i] == 1) (entries! + i).~Entry();
            states![i] = 0;
        }
        count = 0; used = 0;
    }

    public immutable Enumerator GetEnumerator() => new Enumerator(entries, states, capacity);

    ~HashMap() { this.Clear(); if (entries != null) Memory::Free((void*)entries); }

    // Enumeration borrows the map; do not mutate or destroy it while enumerating.
    public struct Enumerator
    {
        private immutable(Entry)*? entries;
        private readonly(byte)*? states;
        private nuint capacity;
        private nuint position;
        private bool valid;
        public Enumerator(immutable(Entry)*? storage, readonly(byte)*? flags, nuint size)
        { entries = storage; states = flags; capacity = size; position = 0; valid = false; }
        public bool MoveNext()
        {
            if (valid) position++;
            while (position < capacity && states![position] != 1) position++;
            valid = position < capacity;
            return valid;
        }
        public immutable(Entry)* Current
        {
            immutable get
            {
                if (!valid) throw new Exception("Enumerator has no current entry");
                return entries! + position;
            }
        }
    }
}
