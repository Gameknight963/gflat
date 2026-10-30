using Standard::Collections;
namespace Standard::Queries;

struct ZipSource<T, U, R, LeftSource, LeftIterator, RightSource, RightIterator>
{
    private LeftSource left;
    private RightSource right;
    private R(T, U)* selector;

    public ZipSource(LeftSource first, RightSource second, R(T, U)* project)
    { left = move first; right = move second; selector = project; }

    public ZipSource<T, U, R, LeftSource, LeftIterator, RightSource, RightIterator>::Enumerator GetEnumerator()
    {
        LeftIterator first = left.GetEnumerator();
        RightIterator second = right.GetEnumerator();
        return new ZipSource<T, U, R, LeftSource, LeftIterator, RightSource, RightIterator>::Enumerator(move first, move second, selector);
    }

    public struct Enumerator : IEnumerator<R>
    {
        private LeftIterator left;
        private RightIterator right;
        private R(T, U)* selector;
        private bool valid;
        private bool finished;

        public Enumerator(LeftIterator first, RightIterator second, R(T, U)* project)
        { left = move first; right = move second; selector = project; valid = false; finished = false; }

        public bool MoveNext()
        {
            valid = false;
            if (finished) return false;
            if (!left.MoveNext()) { finished = true; return false; }
            if (!right.MoveNext()) { finished = true; return false; }
            valid = true; return true;
        }

        public R Current
        {
            get
            {
                if (!valid) throw new Exception("Enumerator has no current element");
                return selector(left.Current, right.Current);
            }
        }
    }
}
