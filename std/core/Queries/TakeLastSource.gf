using std::Collections;
namespace std::Queries;

// Each enumerator buffers its own bounded tail on the first move.
struct TakeLastSource<T, Source, Iterator>
{
    private Source source;
    private nuint limit;
    public TakeLastSource(Source input, nuint count) { source = move input; limit = count; }
    public TakeLastSource<T, Source, Iterator>::Enumerator GetEnumerator()
        => new TakeLastSource<T, Source, Iterator>::Enumerator(source, limit);

    public struct Enumerator : IEnumerator<T>
    {
        private Source source;
        private nuint limit;
        private Deque<T> buffer;
        private nuint position;
        private bool initialized;
        private bool finished;
        private bool valid;

        public Enumerator(Source input, nuint count)
        {
            source = move input; limit = count; buffer = new(); position = 0;
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
                if (limit == 0) return false;
                Deque<T> staged = new();
                {
                    Iterator iterator = source.GetEnumerator();
                    while (iterator.MoveNext())
                    {
                        if (staged.Count == limit) staged.RemoveFirst();
                        staged.AddLast(iterator.Current);
                    }
                }
                buffer = move staged;
                finished = false;
            }
            if (position == buffer.Count) { finished = true; return false; }
            position++;
            valid = true;
            return true;
        }

        public T Current
        {
            get
            {
                if (!valid) throw new Exception("Enumerator has no current element");
                return buffer[position - 1];
            }
        }
    }
}
