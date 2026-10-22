namespace std;

// Borrows contiguous storage; each result is a full, possibly overlapping view.
public struct SpanWindows<T> : IEnumerable<Span<T>, SpanWindows<T>::Enumerator>
{
    private T*? data;
    private nuint length;
    private nuint width;
    private nuint step;
    public SpanWindows(T*? storage, nuint count, nuint width, nuint step = 1)
    {
        if (width == 0) throw new Exception("Window width must be positive");
        if (step == 0) throw new Exception("Window step must be positive");
        if (storage == null && count != 0) throw new Exception("Window storage is null");
        data = storage;
        length = count;
        this.width = width;
        this.step = step;
    }

    public SpanWindows<T>::Enumerator GetEnumerator()
        => new SpanWindows<T>::Enumerator(data, length, width, step);

    public Enumerable<Span<T>, SpanWindows<T>, SpanWindows<T>::Enumerator> AsEnumerable()
        => new Enumerable<Span<T>, SpanWindows<T>, SpanWindows<T>::Enumerator>(*this);

    public struct Enumerator : IEnumerator<Span<T>>
    {
        private T*? data;
        private nuint length;
        private nuint width;
        private nuint step;
        private nuint next;
        private nuint start;
        private bool finished;
        private bool valid;
        public Enumerator(T*? storage, nuint count, nuint width, nuint step)
        {
            if (width == 0 || step == 0) throw new Exception("Window dimensions must be positive");
            if (storage == null && count != 0) throw new Exception("Window storage is null");
            data = storage;
            length = count;
            this.width = width;
            this.step = step;
            next = 0;
            start = 0;
            finished = width > length;
            valid = false;
        }

        public bool MoveNext()
        {
            valid = false;
            if (finished) return false;
            start = next;
            // Test the remaining distance before adding, even for a huge step.
            if (step > length - width - next) finished = true;
            else next += step;
            valid = true;
            return true;
        }

        public Span<T> Current
        {
            get
            {
                if (!valid) throw new Exception("Enumerator has no current element");
                return new Span<T>(data! + start, width);
            }
        }
    }
}

public SpanWindows<T> Windows<T>(this Span<T> source, nuint width, nuint step = 1)
    => new SpanWindows<T>(source.Data, source.Length, width, step);
