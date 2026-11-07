using Standard::Collections;
using Standard::Memory;

namespace Standard::Queries;

// Copies share immutable values. Iterators retain their own share of the storage.
public struct Group<T> : IEnumerable<T, Group<T>::Enumerator>
{
    private SharedStorage<Vector<T>> storage;

    public Group() nothrow { storage = new(); }

    public nuint Count
    {
        immutable get nothrow
        {
            if (storage.IsEmpty) return 0;
            return storage.ValueImmutable.Count;
        }
    }

    public bool IsEmpty { immutable get nothrow => this.Count == 0; }

    internal void Add(T value)
    {
        if (storage.IsEmpty) storage = new SharedStorage<Vector<T>>(new Vector<T>());
        else if (!storage.IsUnique)
            throw new Exception("Cannot append to a shared group");
        storage.Value.Add(move value);
    }

    public immutable Enumerator GetEnumerator() => new Enumerator(*this);

    public immutable Enumerable<T, Group<T>, Group<T>::Enumerator> AsEnumerable()
        => new Enumerable<T, Group<T>, Group<T>::Enumerator>(*this);

    public struct Enumerator
    {
        private Group<T> owner;
        private nuint position;
        private bool valid;

        public Enumerator(Group<T> group)
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
                return owner.storage.ValueImmutable[position - 1];
            }
        }
    }
}
