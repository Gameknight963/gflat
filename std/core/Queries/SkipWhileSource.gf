using std::Collections;
namespace std::Queries;

struct SkipWhileSource<T, Source, Iterator>
{
    private Source source;
    private bool(T)* predicate;

    public SkipWhileSource(Source input, bool(T)* test) { source = move input; predicate = test; }
    public SkipWhileSource<T, Source, Iterator>::Enumerator GetEnumerator()
        => new SkipWhileSource<T, Source, Iterator>::Enumerator(source.GetEnumerator(), predicate);

    public struct Enumerator : IEnumerator<T>
    {
        private Iterator iterator;
        private bool(T)* predicate;
        private bool skipping;
        private bool valid;
        private bool finished;

        public Enumerator(Iterator input, bool(T)* test) { iterator = move input; predicate = test; skipping = true; valid = false; finished = false; }
        public bool MoveNext()
        {
            valid = false;
            if (finished) return false;
            while (iterator.MoveNext())
            {
                if (skipping)
                {
                    if (predicate(iterator.Current)) continue;
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
