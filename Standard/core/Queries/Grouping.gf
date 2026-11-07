using Standard::Collections;
using Standard::Memory;

namespace Standard::Queries;

public struct Grouping<K, T> : IEnumerable<T, Grouping<K, T>::Enumerator>
{
    private struct Data
    {
        public K Key;
        public Vector<T> Values;
        public Data(K key)
        {
            Key = move key;
            Values = new();
        }
    }

    private SharedStorage<Data> storage;

    internal Grouping(K key) { storage = new SharedStorage<Data>(new Data(move key)); }

    public ref immutable(K) Key { immutable get => storage.ValueImmutable.Key; }
    public nuint Count
    {
        immutable get nothrow
        {
            if (storage.IsEmpty) return 0;
            return storage.ValueImmutable.Values.Count;
        }
    }
    public bool IsEmpty { immutable get nothrow => this.Count == 0; }

    internal void Add(T value)
    {
        if (!storage.IsUnique) throw new Exception("Cannot append to a shared grouping");
        storage.Value.Values.Add(move value);
    }

    public immutable Enumerator GetEnumerator() => new Enumerator(*this);
    public immutable Enumerable<T, Grouping<K, T>, Grouping<K, T>::Enumerator> AsEnumerable()
        => new Enumerable<T, Grouping<K, T>, Grouping<K, T>::Enumerator>(*this);

    public struct Enumerator
    {
        private Grouping<K, T> owner;
        private nuint position;
        private bool valid;

        public Enumerator(Grouping<K, T> group)
        {
            owner = move group;
            position = 0;
            valid = false;
        }
        public bool MoveNext()
        {
            valid = false;
            if (position == owner.Count) return false;
            position++;
            valid = true;
            return true;
        }
        public ref immutable(T) Current
        {
            immutable get
            {
                if (!valid) throw new Exception("Enumerator has no current element");
                return owner.storage.ValueImmutable.Values[position - 1];
            }
        }
    }
}
