using Standard::Collections;

namespace Standard::Queries;

// Select keys over the concatenated inputs while retaining their whole values.
struct UnionBySource<T, K : IHashable, LS, LE, RS, RE>
{
    private DistinctBySource<T, K, ConcatSource<T, LS, LE, RS, RE>, ConcatSource<T, LS, LE, RS, RE>::Enumerator> source;

    public UnionBySource(LS first, RS second, K(T)* select)
    {
        source = new DistinctBySource<T, K, ConcatSource<T, LS, LE, RS, RE>, ConcatSource<T, LS, LE, RS, RE>::Enumerator>(
            new ConcatSource<T, LS, LE, RS, RE>(move first, move second), select);
    }

    public Enumerator GetEnumerator() => new Enumerator(source.GetEnumerator());

    public struct Enumerator : IEnumerator<T>
    {
        private DistinctBySource<T, K, ConcatSource<T, LS, LE, RS, RE>, ConcatSource<T, LS, LE, RS, RE>::Enumerator>::Enumerator iterator;
        private bool finished;

        public Enumerator(DistinctBySource<T, K, ConcatSource<T, LS, LE, RS, RE>, ConcatSource<T, LS, LE, RS, RE>::Enumerator>::Enumerator input)
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
