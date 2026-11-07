using Standard::Collections;

namespace Standard::Queries;

struct JoinSource<T, U, K : IHashable, R, OuterSource, OuterIterator, InnerSource, InnerIterator>
{
    private OuterSource outer;
    private InnerSource inner;
    private K(T)* outerKey;
    private K(U)* innerKey;
    private R(T, U)* resultSelector;

    public JoinSource(OuterSource first, InnerSource second, K(T)* firstKey, K(U)* secondKey, R(T, U)* combine)
    {
        outer = move first;
        inner = move second;
        outerKey = firstKey;
        innerKey = secondKey;
        resultSelector = combine;
    }

    public Enumerator GetEnumerator() => new Enumerator(outer.GetEnumerator(), inner, outerKey, innerKey, resultSelector);

    public struct Enumerator : IEnumerator<R>
    {
        private OuterIterator outer;
        private InnerSource inner;
        private K(T)* outerKey;
        private K(U)* innerKey;
        private R(T, U)* resultSelector;
        private HashMap<K, Vector<U>> lookup;
        private Vector<T> current;
        private Vector<U>*? group;
        private nuint position;
        private bool initialized;
        private bool valid;
        private bool finished;

        public Enumerator(OuterIterator first, InnerSource second, K(T)* firstKey, K(U)* secondKey, R(T, U)* combine)
        {
            outer = move first;
            inner = move second;
            outerKey = firstKey;
            innerKey = secondKey;
            resultSelector = combine;
            lookup = new();
            current = new();
            group = null;
            position = 0;
            initialized = false;
            valid = false;
            finished = false;
        }

        private void BuildLookup()
        {
            InnerIterator iterator = inner.GetEnumerator();
            while (iterator.MoveNext())
            {
                U value = iterator.Current;
                K key = innerKey(value);
                Vector<U>*? existing = lookup.Find(&key);
                if (existing != null) existing!.Add(move value);
                else
                {
                    Vector<U> values = new();
                    values.Add(move value);
                    lookup.Add(move key, move values);
                }
            }
        }

        private void Finish()
        {
            valid = false;
            finished = true;
            group = null;
            current.Clear();
            lookup.Clear();
        }

        public bool MoveNext()
        {
            valid = false;
            if (finished) return false;
            try
            {
                bool ready = false;
                if (!initialized)
                {
                    initialized = true;
                    if (!outer.MoveNext())
                    {
                        this.Finish();
                        return false;
                    }
                    this.BuildLookup();
                    if (lookup.IsEmpty)
                    {
                        this.Finish();
                        return false;
                    }
                    ready = true;
                }
                if (group != null)
                {
                    position++;
                    if (position < group!.Count)
                    {
                        valid = true;
                        return true;
                    }
                    group = null;
                }
                while (true)
                {
                    current.Clear();
                    if (!ready && !outer.MoveNext())
                    {
                        this.Finish();
                        return false;
                    }
                    ready = false;
                    current.Add(outer.Current);
                    K key = outerKey(current[0]);
                    group = lookup.Find(&key);
                    if (group != null)
                    {
                        position = 0;
                        valid = true;
                        return true;
                    }
                }
            }
            catch
            {
                this.Finish();
                throw;
            }
        }

        public R Current
        {
            get
            {
                if (!valid) throw new Exception("Enumerator has no current element");
                try { return resultSelector(current[0], (*group!)[position]); }
                catch
                {
                    this.Finish();
                    throw;
                }
            }
        }
    }
}
