using Standard::Collections;

namespace Standard::Queries;

// Reuse concatenation and distinct-value ownership with a shorter public source type.
struct UnionSource<T : IHashable, LS, LE, RS, RE>
{
    private DistinctSource<T, ConcatSource<T, LS, LE, RS, RE>, ConcatSource<T, LS, LE, RS, RE>::Enumerator> source;

    public UnionSource(LS first, RS second)
    {
        source = new DistinctSource<T, ConcatSource<T, LS, LE, RS, RE>, ConcatSource<T, LS, LE, RS, RE>::Enumerator>(
            new ConcatSource<T, LS, LE, RS, RE>(move first, move second));
    }

    public Enumerator GetEnumerator() => new Enumerator(source.GetEnumerator());

    public struct Enumerator : IEnumerator<T>
    {
        private DistinctSource<T, ConcatSource<T, LS, LE, RS, RE>, ConcatSource<T, LS, LE, RS, RE>::Enumerator>::Enumerator iterator;
        private bool finished;

        public Enumerator(DistinctSource<T, ConcatSource<T, LS, LE, RS, RE>, ConcatSource<T, LS, LE, RS, RE>::Enumerator>::Enumerator input)
        {
            iterator = move input;
            finished = false;
        }

        public bool MoveNext()
        {
            if (finished) return false;
            finished = true;
            if (!iterator.MoveNext()) return false;
            finished = false;
            return true;
        }

        public T Current { get => iterator.Current; }
    }
}
