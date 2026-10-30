using Standard::Collections;
namespace Standard::Queries;

// Query copies retain only the source and comparator; each cursor owns its buffer.
struct OrderSource<T, Source, Iterator>
{
    private Source source;
    private int(immutable(T)*, immutable(T)*)* compare;
    public OrderSource(Source input, int(immutable(T)*, immutable(T)*)* comparison)
    { source = move input; compare = comparison; }
    public Enumerator GetEnumerator() => new Enumerator(source, compare);

    // Concrete foreach/query pattern, like Span: Current borrows a buffered value.
    public struct Enumerator
    {
        private Source source;
        private int(immutable(T)*, immutable(T)*)* compare;
        private Vector<T> buffer;
        private nuint position;
        private bool initialized;
        private bool finished;
        private bool valid;
        public Enumerator(Source input, int(immutable(T)*, immutable(T)*)* comparison)
        {
            source = move input; compare = comparison; buffer = new(); position = 0;
            initialized = false; finished = false; valid = false;
        }
        public bool MoveNext()
        {
            valid = false;
            if (finished) return false;
            if (!initialized)
            {
                initialized = true; finished = true;
                Vector<T> staged = Standard::Queries::ToVector<T, Source, Iterator>(new Enumerable<T, Source, Iterator>(source));
                Standard::Collections::StableSort<T>(staged.AsSpan(), compare);
                buffer = move staged;
                finished = false;
            }
            if (position == buffer.Count) { finished = true; return false; }
            position++; valid = true; return true;
        }
        public ref immutable(T) Current
        {
            immutable get
            {
                if (!valid) throw new Exception("Enumerator has no current element");
                return buffer[position - 1];
            }
        }
    }
}
