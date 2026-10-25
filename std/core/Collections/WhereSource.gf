using std::Collections;
namespace std;

struct WhereSource<T, Source, Iterator>
{
    private Source source;
    private bool(T)* predicate;

    public WhereSource(Source input, bool(T)* filter) { source = move input; predicate = filter; }
    public WhereSource<T, Source, Iterator>::Enumerator GetEnumerator()
        => new WhereSource<T, Source, Iterator>::Enumerator(source.GetEnumerator(), predicate);

    public struct Enumerator : IEnumerator<T>
    {
        private Iterator iterator;
        private bool(T)* predicate;
        private bool valid;

        public Enumerator(Iterator input, bool(T)* filter) { iterator = move input; predicate = filter; valid = false; }
        public bool MoveNext()
        {
            valid = false;
            while (iterator.MoveNext())
            {
                if (predicate(iterator.Current)) { valid = true; return true; }
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
