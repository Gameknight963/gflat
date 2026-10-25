using std::Collections;
namespace std::Queries;

struct IndexedWhereSource<T, Source, Iterator>
{
    private Source source;
    private bool(T, nuint)* predicate;

    public IndexedWhereSource(Source input, bool(T, nuint)* filter)
    { source = move input; predicate = filter; }

    public IndexedWhereSource<T, Source, Iterator>::Enumerator GetEnumerator()
        => new IndexedWhereSource<T, Source, Iterator>::Enumerator(source.GetEnumerator(), predicate);

    public struct Enumerator : IEnumerator<T>
    {
        private Iterator iterator;
        private bool(T, nuint)* predicate;
        private nuint index;
        private bool started;
        private bool valid;
        private bool overflowed;

        public Enumerator(Iterator input, bool(T, nuint)* filter)
        {
            iterator = move input;
            predicate = filter;
            index = 0;
            started = false;
            valid = false;
            overflowed = false;
        }

        public bool MoveNext()
        {
            valid = false;
            if (overflowed) return false;
            while (iterator.MoveNext())
            {
                if (started)
                {
                    if (index == (nuint)0 - (nuint)1)
                    {
                        overflowed = true;
                        throw new Exception("Sequence index overflow");
                    }
                    index++;
                }
                else started = true;
                if (predicate(iterator.Current, index)) { valid = true; return true; }
            }
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
