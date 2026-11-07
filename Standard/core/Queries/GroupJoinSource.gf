using Standard::Collections;

namespace Standard::Queries;

struct GroupJoinSource<T, U, K : IHashable, R, OuterSource, OuterIterator, InnerSource, InnerIterator>
{
    private OuterSource outer;
    private InnerSource inner;
    private K(T)* outerKey;
    private K(U)* innerKey;
    private R(T, Group<U>)* resultSelector;

    public GroupJoinSource(OuterSource first, InnerSource second, K(T)* firstKey, K(U)* secondKey,
        R(T, Group<U>)* combine)
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
        private R(T, Group<U>)* resultSelector;
        private HashMap<K, Group<U>> lookup;
        private Vector<T> current;
        private Group<U> group;
        private bool initialized;
        private bool valid;
        private bool finished;

        public Enumerator(OuterIterator first, InnerSource second, K(T)* firstKey, K(U)* secondKey,
            R(T, Group<U>)* combine)
        {
            outer = move first;
            inner = move second;
            outerKey = firstKey;
            innerKey = secondKey;
            resultSelector = combine;
            lookup = new();
            current = new();
            group = new();
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
                Group<U>*? existing = lookup.Find(&key);
                if (existing != null) existing!.Add(move value);
                else
                {
                    Group<U> values = new();
                    values.Add(move value);
                    lookup.Add(move key, move values);
                }
            }
        }

        private void Finish()
        {
            valid = false;
            finished = true;
            group = new Group<U>();
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
                    ready = true;
                }
                group = new Group<U>();
                current.Clear();
                if (!ready && !outer.MoveNext())
                {
                    this.Finish();
                    return false;
                }
                current.Add(outer.Current);
                K key = outerKey(current[0]);
                Group<U>*? found = lookup.Find(&key);
                if (found != null) group = *found!;
                valid = true;
                return true;
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
                try { return resultSelector(current[0], group); }
                catch
                {
                    this.Finish();
                    throw;
                }
            }
        }
    }
}
