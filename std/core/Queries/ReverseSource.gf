using std::Collections;
namespace std::Queries;

// Reusable source view; the buffer belongs to each independent iterator.
struct ReverseSource<T, Source, Iterator>
{
    private Source source;
    public ReverseSource(Source input) { source = move input; }
    public Enumerator GetEnumerator() => new Enumerator(source);

    public struct Enumerator : IEnumerator<T>
    {
        private Source source;
        private Vector<T> buffer;
        private nuint remaining;
        private bool initialized;
        private bool finished;
        private bool valid;

        public Enumerator(Source input)
        {
            source = move input; buffer = new(); remaining = 0;
            initialized = false; finished = false; valid = false;
        }
        public bool MoveNext()
        {
            valid = false;
            if (finished) return false;
            if (!initialized)
            {
                initialized = true;
                finished = true;
                buffer = std::Queries::ToVector<T, Source, Iterator>(new Enumerable<T, Source, Iterator>(source));
                remaining = buffer.Count;
                finished = false;
            }
            if (remaining == 0) { finished = true; return false; }
            remaining--; valid = true;
            return true;
        }
        public T Current
        {
            get
            {
                if (!valid) throw new Exception("Enumerator has no current element");
                return buffer[remaining];
            }
        }
    }
}
