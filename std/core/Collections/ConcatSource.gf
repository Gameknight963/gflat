namespace std;

struct ConcatSource<T, LeftSource, LeftIterator, RightSource, RightIterator>
{
    private LeftSource left;
    private RightSource right;

    public ConcatSource(LeftSource first, RightSource second)
    { left = move first; right = move second; }

    public ConcatSource<T, LeftSource, LeftIterator, RightSource, RightIterator>::Enumerator GetEnumerator()
    {
        LeftIterator first = left.GetEnumerator();
        RightIterator second = right.GetEnumerator();
        return new ConcatSource<T, LeftSource, LeftIterator, RightSource, RightIterator>::Enumerator(move first, move second);
    }

    public struct Enumerator : IEnumerator<T>
    {
        private static const byte Left = (byte)0;
        private static const byte Right = (byte)1;
        private static const byte Finished = (byte)2;
        private LeftIterator left;
        private RightIterator right;
        private byte phase;
        private bool valid;

        public Enumerator(LeftIterator first, RightIterator second)
        {
            left = move first; right = move second;
            phase = Left; valid = false;
        }

        public bool MoveNext()
        {
            byte active = phase;
            phase = Finished; valid = false;
            if (active == Finished) return false;
            if (active == Left && left.MoveNext())
            {
                phase = Left; valid = true; return true;
            }
            if (right.MoveNext())
            {
                phase = Right; valid = true; return true;
            }
            return false;
        }

        public T Current
        {
            get
            {
                if (!valid) throw new Exception("Enumerator has no current element");
                if (phase == Left) return left.Current;
                return right.Current;
            }
        }
    }
}
