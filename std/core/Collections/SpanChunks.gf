using std::Queries;
namespace std::Collections;

// Borrows contiguous storage; the final nonoverlapping chunk may be shorter.
public struct SpanChunks<T> : IEnumerable<Span<T>, SpanChunks<T>::Enumerator>
{
    private T*? data;
    private nuint length;
    private nuint size;

    public SpanChunks(T*? storage, nuint count, nuint size)
    {
        if (size == 0) throw new Exception("Chunk size must be positive");
        if (storage == null && count != 0) throw new Exception("Chunk storage is null");
        data = storage;
        length = count;
        this.size = size;
    }

    public SpanChunks<T>::Enumerator GetEnumerator()
        => new SpanChunks<T>::Enumerator(data, length, size);

    public Enumerable<Span<T>, SpanChunks<T>, SpanChunks<T>::Enumerator> AsEnumerable()
        => new Enumerable<Span<T>, SpanChunks<T>, SpanChunks<T>::Enumerator>(*this);

    public struct Enumerator : IEnumerator<Span<T>>
    {
        private T*? data;
        private nuint length;
        private nuint size;
        private nuint next;
        private nuint start;
        private nuint currentLength;
        private bool valid;

        public Enumerator(T*? storage, nuint count, nuint size)
        {
            if (size == 0) throw new Exception("Chunk size must be positive");
            if (storage == null && count != 0) throw new Exception("Chunk storage is null");
            data = storage;
            length = count;
            this.size = size;
            next = 0;
            start = 0;
            currentLength = 0;
            valid = false;
        }

        public bool MoveNext()
        {
            valid = false;
            if (next >= length) return false;
            start = next;
            currentLength = length - next;
            if (currentLength > size) currentLength = size;
            // The increment is at most the remaining distance, even for huge size.
            next += currentLength;
            valid = true;
            return true;
        }

        public Span<T> Current
        {
            get
            {
                if (!valid) throw new Exception("Enumerator has no current element");
                return new Span<T>(data! + start, currentLength);
            }
        }
    }
}

public SpanChunks<T> Chunks<T>(this Span<T> source, nuint size)
    => new SpanChunks<T>(source.Data, source.Length, size);
