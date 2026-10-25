namespace std;

struct IndexedSelectSource<T, U, Source, Iterator>
{
    private Source source;
    private U(T, nuint)* selector;

    public IndexedSelectSource(Source input, U(T, nuint)* project)
    { source = move input; selector = project; }

    public IndexedSelectSource<T, U, Source, Iterator>::Enumerator GetEnumerator()
        => new IndexedSelectSource<T, U, Source, Iterator>::Enumerator(source.GetEnumerator(), selector);

    public struct Enumerator : IEnumerator<U>
    {
        private Iterator iterator;
        private U(T, nuint)* selector;
        private nuint index;
        private bool started;
        private bool valid;
        private bool overflowed;

        public Enumerator(Iterator input, U(T, nuint)* project)
        {
            iterator = move input;
            selector = project;
            index = 0;
            started = false;
            valid = false;
            overflowed = false;
        }

        public bool MoveNext()
        {
            valid = false;
            if (overflowed) return false;
            if (!iterator.MoveNext()) return false;
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
            valid = true;
            return true;
        }

        public U Current
        {
            get
            {
                if (!valid) throw new Exception("Enumerator has no current element");
                return selector(iterator.Current, index);
            }
        }
    }
}
