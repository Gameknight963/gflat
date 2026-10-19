namespace std;

namespace hash_detail
{
    // Kept separately because a generic parameter currently has one interface constraint.
    bool Equal<T : IEquatable<T>>(readonly(T)* left, readonly(T)* right)
        => left.Equals(right);
}

// Owns keys and values. Keys must also implement IEquatable<K>.
// Lookups borrow keys; growth invalidates all borrowed entries and values.
struct HashMap<K : IHashable, V>
{
    private Entry*? entries;
    private byte*? states;
    private nuint capacity;
    private nuint count;
    private nuint used;

    public HashMap() { entries = null; states = null; capacity = 0; count = 0; used = 0; }
    public nuint Count { readonly get => count; }
    public nuint Capacity { readonly get => capacity; }
    public bool IsEmpty { readonly get => count == 0; }

    // Keys are exposed through a readonly view so their hash cannot be changed.
    public struct Entry
    {
        private K key;
        private V value;
        private nuint hash;
        public Entry(K item, V mapped, nuint code) { key = move item; value = move mapped; hash = code; }
        public readonly(K)* Key { readonly get => &key; }
        public ref V Value { get => value; }
        public readonly(V)* ValueReadOnly { readonly get => &value; }
    }

    private readonly nuint Locate(readonly(K)* key, nuint hash)
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

    public readonly bool ContainsKey(readonly(K)* key)
        => this.Locate(key, key.GetHashCode()) != capacity;

    public V*? Find(readonly(K)* key)
    {
        nuint slot = this.Locate(key, key.GetHashCode());
        if (slot == capacity) return null;
        return &entries![slot].value;
    }

    public readonly readonly(V)*? FindReadOnly(readonly(K)* key)
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
        if (used >= capacity / 2)
        {
            if (count < capacity / 4) this.Rehash(capacity);
            else this.Reserve(count + 1);
        }
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

    public ref V this[readonly(K)* key]
    {
        get
        {
            nuint slot = this.Locate(key, key.GetHashCode());
            if (slot == capacity) throw new Exception("HashMap key not found");
            return entries![slot].value;
        }
    }

    public bool Remove(readonly(K)* key)
    {
        nuint slot = this.Locate(key, key.GetHashCode());
        if (slot == capacity) return false;
        (entries! + slot).~Entry();
        states![slot] = 2;
        count--;
        return true;
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

    public Enumerator GetEnumerator() => new Enumerator(entries, states, capacity);

    ~HashMap() { this.Clear(); if (entries != null) Memory::Free((void*)entries); }

    // Enumeration borrows the map; do not mutate or destroy it while enumerating.
    public struct Enumerator
    {
        private Entry*? entries;
        private byte*? states;
        private nuint capacity;
        private nuint position;
        private bool valid;
        public Enumerator(Entry*? storage, byte*? flags, nuint size)
        { entries = storage; states = flags; capacity = size; position = 0; valid = false; }
        public bool MoveNext()
        {
            if (valid) position++;
            while (position < capacity && states![position] != 1) position++;
            valid = position < capacity;
            return valid;
        }
        public readonly(Entry)* Current
        {
            readonly get
            {
                if (!valid) throw new Exception("Enumerator has no current entry");
                return entries! + position;
            }
        }
    }
}
