namespace std;

struct SelectSource<T, U, Source, Iterator>
{
    private Source source;
    private U(T)* selector;

    public SelectSource(Source input, U(T)* project) { source = move input; selector = project; }
    public SelectSource<T, U, Source, Iterator>::Enumerator GetEnumerator()
        => new SelectSource<T, U, Source, Iterator>::Enumerator(source.GetEnumerator(), selector);

    public struct Enumerator : IEnumerator<U>
    {
        private Iterator iterator;
        private U(T)* selector;
        private bool valid;

        public Enumerator(Iterator input, U(T)* project) { iterator = move input; selector = project; valid = false; }
        public bool MoveNext() { valid = false; valid = iterator.MoveNext(); return valid; }
        public U Current
        {
            get
            {
                if (!valid) throw new Exception("Enumerator has no current element");
                return selector(iterator.Current);
            }
        }
    }
}
