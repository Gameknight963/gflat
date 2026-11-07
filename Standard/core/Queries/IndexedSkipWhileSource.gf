using Standard::Collections;

namespace Standard::Queries;

struct IndexedSkipWhileSource<T, Source, Iterator>
{
    private Source source;
    private bool(T, nuint)* predicate;

    public IndexedSkipWhileSource(Source input, bool(T, nuint)* test)
    {
        source = move input;
        predicate = test;
    }

    public IndexedSkipWhileSource<T, Source, Iterator>::Enumerator GetEnumerator()
        => new IndexedSkipWhileSource<T, Source, Iterator>::Enumerator(source.GetEnumerator(), predicate);

    public struct Enumerator : IEnumerator<T>
    {
        private Iterator iterator;
        private bool(T, nuint)* predicate;
        private nuint index;
        private bool started;
        private bool valid;
        private bool finished;
        private bool skipping;

        public Enumerator(Iterator input, bool(T, nuint)* test)
        {
            iterator = move input;
            predicate = test;
            index = 0;
            started = false;
            valid = false;
            finished = false;
            skipping = true;
        }

        public bool MoveNext()
        {
            valid = false;
            if (finished) return false;
            while (iterator.MoveNext())
            {
                if (skipping)
                {
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
                    if (predicate(iterator.Current, index)) continue;
                    skipping = false;
                }
                valid = true;
                return true;
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
