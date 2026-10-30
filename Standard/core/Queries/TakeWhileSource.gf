using Standard::Collections;
namespace Standard::Queries;

struct TakeWhileSource<T, Source, Iterator>
{
    private Source source;
    private bool(T)* predicate;

    public TakeWhileSource(Source input, bool(T)* test) { source = move input; predicate = test; }
    public TakeWhileSource<T, Source, Iterator>::Enumerator GetEnumerator()
        => new TakeWhileSource<T, Source, Iterator>::Enumerator(source.GetEnumerator(), predicate);

    public struct Enumerator : IEnumerator<T>
    {
        private Iterator iterator;
        private bool(T)* predicate;
        private bool valid;
        private bool finished;

        public Enumerator(Iterator input, bool(T)* test) { iterator = move input; predicate = test; valid = false; finished = false; }
        public bool MoveNext()
        {
            valid = false;
            if (finished) return false;
            if (!iterator.MoveNext()) { finished = true; return false; }
            if (!predicate(iterator.Current)) { finished = true; return false; }
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
