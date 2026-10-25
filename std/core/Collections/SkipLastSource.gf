namespace std;

// Retains a bounded lookahead, rather than materializing the whole input.
struct SkipLastSource<T, Source, Iterator>
{
    private Source source;
    private nuint limit;
    public SkipLastSource(Source input, nuint count) { source = move input; limit = count; }
    public SkipLastSource<T, Source, Iterator>::Enumerator GetEnumerator()
        => new SkipLastSource<T, Source, Iterator>::Enumerator(source.GetEnumerator(), limit);

    public struct Enumerator : IEnumerator<T>
    {
        private Iterator iterator;
        private nuint limit;
        private Deque<T> pending;
        private bool valid;
        private bool finished;

        public Enumerator(Iterator input, nuint count)
        {
            iterator = move input; limit = count; pending = new();
            valid = false; finished = false;
        }

        public bool MoveNext()
        {
            bool previous = valid;
            valid = false;
            if (finished) return false;
            // A failed advance cannot be retried.
            finished = true;
            if (limit == 0)
            {
                if (!iterator.MoveNext()) return false;
            }
            else
            {
                if (previous) pending.RemoveFirst();
                while (pending.Count <= limit)
                {
                    if (!iterator.MoveNext())
                    {
                        pending.Clear();
                        return false;
                    }
                    pending.AddLast(iterator.Current);
                }
            }
            finished = false; valid = true;
            return true;
        }

        public T Current
        {
            get
            {
                if (!valid) throw new Exception("Enumerator has no current element");
                if (limit == 0) return iterator.Current;
                return pending.Front;
            }
        }
    }
}
