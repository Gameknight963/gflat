using Standard::Collections;

namespace Standard::Queries;

struct IndexedTakeWhileSource<T, Source, Iterator>
{
    private Source source;
    private bool(T, nuint)* predicate;

    public IndexedTakeWhileSource(Source input, bool(T, nuint)* test)
    {
        source = move input;
        predicate = test;
    }

    public IndexedTakeWhileSource<T, Source, Iterator>::Enumerator GetEnumerator()
        => new IndexedTakeWhileSource<T, Source, Iterator>::Enumerator(source.GetEnumerator(), predicate);

    public struct Enumerator : IEnumerator<T>
    {
        private Iterator iterator;
        private bool(T, nuint)* predicate;
        private nuint index;
        private bool started;
        private bool valid;
        private bool finished;

        public Enumerator(Iterator input, bool(T, nuint)* test)
        {
            iterator = move input;
            predicate = test;
            index = 0;
            started = false;
            valid = false;
            finished = false;
        }

        public bool MoveNext()
        {
            valid = false;
            if (finished) return false;
            if (!iterator.MoveNext()) { finished = true; return false; }
            if (started)
            {
                if (index == (nuint)0 - (nuint)1)
                {
                    finished = true;
                    throw new Exception("Sequence index overflow");
                }
                index++;
            }
            else started = true;
            if (!predicate(iterator.Current, index)) { finished = true; return false; }
            valid = true;
            return true;
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
