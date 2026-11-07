using Standard::Collections;
using Standard::Memory;

namespace Standard::Queries;

struct OrderBySource<T, K, Source, Iterator>
{
    private Source source;
    private K(T)* selector;
    private int(immutable(K)*, immutable(K)*)* compare;
    private bool descending;

    public OrderBySource(Source input, K(T)* keySelector,
        int(immutable(K)*, immutable(K)*)* comparison, bool reverse)
    {
        source = move input;
        selector = keySelector;
        compare = comparison;
        descending = reverse;
    }

    public Enumerator GetEnumerator() => new Enumerator(source, selector, compare, descending);

    public struct Enumerator
    {
        private Source source;
        private K(T)* selector;
        private int(immutable(K)*, immutable(K)*)* compare;
        private bool descending;
        private Vector<T> buffer;
        private Vector<nuint> indices;
        private nuint position;
        private bool initialized;
        private bool finished;
        private bool valid;

        public Enumerator(Source input, K(T)* keySelector,
            int(immutable(K)*, immutable(K)*)* comparison, bool reverse)
        {
            source = move input;
            selector = keySelector;
            compare = comparison;
            descending = reverse;
            buffer = new();
            indices = new();
            position = 0;
            initialized = false;
            finished = false;
            valid = false;
        }

        private void Build()
        {
            // Finish reading the source before invoking key selectors.
            {
                Iterator iterator = source.GetEnumerator();
                while (iterator.MoveNext()) buffer.Add(iterator.Current);
            }
            nuint count = buffer.Count;
            Vector<K> keys = new();
            keys.Reserve(count);
            for (nuint i = 0; i < count; i++) keys.Add(selector(buffer[i]));
            indices.Reserve(count);
            Vector<nuint> temporary = new();
            if (count > 1) temporary.Reserve(count);
            for (nuint i = 0; i < count; i++)
            {
                indices.Add(i);
                if (count > 1) temporary.Add(0);
            }
            Span<nuint> order = indices.AsSpan();
            Span<nuint> scratch = temporary.AsSpan();
            nuint width = 1;
            while (width < count)
            {
                nuint start = 0;
                while (start < count)
                {
                    nuint leftLength = count - start;
                    if (leftLength > width) leftLength = width;
                    nuint middle = start + leftLength;
                    nuint rightLength = count - middle;
                    if (rightLength > width) rightLength = width;
                    nuint end = middle + rightLength;
                    nuint left = start;
                    nuint right = middle;
                    nuint destination = start;
                    while (left < middle && right < end)
                    {
                        int relation = 0;
                        if (descending) relation = compare(&keys[order[right]], &keys[order[left]]);
                        else relation = compare(&keys[order[left]], &keys[order[right]]);
                        // Prefer the left run on ties in either direction.
                        if (relation <= 0)
                        {
                            scratch[destination] = order[left];
                            left++;
                        }
                        else
                        {
                            scratch[destination] = order[right];
                            right++;
                        }
                        destination++;
                    }
                    while (left < middle)
                    {
                        scratch[destination] = order[left];
                        left++;
                        destination++;
                    }
                    while (right < end)
                    {
                        scratch[destination] = order[right];
                        right++;
                        destination++;
                    }
                    start = end;
                }
                Span<nuint> previous = order;
                order = scratch;
                scratch = previous;
                if (width >= count - width) break;
                width *= 2;
            }
            // The final pass may reside in temporary storage.
            if (count > 1 && order.Data != indices.AsSpan().Data)
                indices = move temporary;
        }

        private void Finish()
        {
            valid = false;
            finished = true;
            buffer.Clear();
            indices.Clear();
        }

        public bool MoveNext()
        {
            valid = false;
            if (finished) return false;
            try
            {
                if (!initialized)
                {
                    initialized = true;
                    this.Build();
                }
                if (position == buffer.Count)
                {
                    this.Finish();
                    return false;
                }
                position++;
                valid = true;
                return true;
            }
            catch
            {
                this.Finish();
                throw;
            }
        }

        public ref immutable(T) Current
        {
            immutable get
            {
                if (!valid) throw new Exception("Enumerator has no current element");
                return buffer[indices[position - 1]];
            }
        }
    }
}
