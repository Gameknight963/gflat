namespace std;

struct DistinctBySource<T, K : IHashable, Source, Iterator>
{
    private Source source;
    private K(T)* selector;

    public DistinctBySource(Source input, K(T)* select)
    { source = move input; selector = select; }

    public DistinctBySource<T, K, Source, Iterator>::Enumerator GetEnumerator()
        => new DistinctBySource<T, K, Source, Iterator>::Enumerator(source.GetEnumerator(), selector);

    public struct Enumerator : IEnumerator<T>
    {
        private Iterator iterator;
        private HashSet<K> seen;
        private K(T)* selector;
        private bool valid;
        private bool finished;

        public Enumerator(Iterator input, K(T)* select)
        {
            iterator = move input; seen = new HashSet<K>(); selector = select;
            valid = false; finished = false;
        }

        public bool MoveNext()
        {
            valid = false;
            if (finished) return false;
            while (iterator.MoveNext())
            {
                K key = selector(iterator.Current);
                if (seen.Add(move key)) { valid = true; return true; }
            }
            finished = true;
            return false;
        }

        public T Current
        {
            get
            {
                if (!valid) throw new Exception("Enumerator has no current element");
                return iterator.Current;
            }
        }
    }
}
