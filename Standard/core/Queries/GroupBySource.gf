using Standard::Collections;

namespace Standard::Queries;

struct GroupBySource<T, K : IHashable, U, Source, Iterator>
{
    private Source source;
    private K(T)* keySelector;
    private U(T)* elementSelector;

    public GroupBySource(Source input, K(T)* key, U(T)* element)
    {
        source = move input;
        keySelector = key;
        elementSelector = element;
    }

    public Enumerator GetEnumerator() => new Enumerator(source, keySelector, elementSelector);

    // Keys live inside the owning grouping records, which remain stable as handles move.
    private struct KeyRef : IHashable, IEquatable<KeyRef>
    {
        private immutable(K)* value;
        public KeyRef(immutable(K)* key) { value = key; }
        public immutable nuint GetHashCode() => value.GetHashCode();
        public immutable bool Equals(immutable(KeyRef)* other)
            => Standard::Collections::hash_detail::Equal<K>(value, other.value);
    }

    public struct Enumerator : IEnumerator<Grouping<K, U>>
    {
        private Source source;
        private K(T)* keySelector;
        private U(T)* elementSelector;
        private Vector<Grouping<K, U>> groups;
        private nuint position;
        private bool initialized;
        private bool finished;
        private bool valid;

        public Enumerator(Source input, K(T)* key, U(T)* element)
        {
            source = move input;
            keySelector = key;
            elementSelector = element;
            groups = new();
            position = 0;
            initialized = false;
            finished = false;
            valid = false;
        }

        private void Build()
        {
            HashMap<KeyRef, nuint> index = new();
            Iterator iterator = source.GetEnumerator();
            while (iterator.MoveNext())
            {
                T current = iterator.Current;
                K key = keySelector(current);
                nuint*? existing = null;
                {
                    KeyRef probe = new(&key);
                    existing = index.Find(&probe);
                }
                if (existing != null) groups[*existing!].Add(elementSelector(move current));
                else
                {
                    Grouping<K, U> group = new(move key);
                    group.Add(elementSelector(move current));
                    nuint ordinal = groups.Count;
                    groups.Add(move group);
                    KeyRef stored = new(&groups[ordinal].Key);
                    index.Add(move stored, ordinal);
                }
            }
        }

        private void Finish()
        {
            valid = false;
            finished = true;
            groups.Clear();
        }

        public bool MoveNext()
        {
            valid = false;
            if (finished) return false;
            try
            {
                if (!initialized)
                {
                    initialized = true;
                    this.Build();
                }
                if (position == groups.Count)
                {
                    this.Finish();
                    return false;
                }
                position++;
                valid = true;
                return true;
            }
            catch
            {
                this.Finish();
                throw;
            }
        }

        public Grouping<K, U> Current
        {
            get
            {
                if (!valid) throw new Exception("Enumerator has no current element");
                try { return groups[position - 1]; }
                catch
                {
                    this.Finish();
                    throw;
                }
            }
        }
    }
}
