using Standard::Collections;

namespace Standard::Queries;

// Seed one set from the right sequence, then filter distinct left values.
struct SetFilterBySource<T, K : IHashable, LS, LE, RS, RE>
{
    private LS left;
    private RS right;
    private K(T)* selector;
    private bool intersect;

    public SetFilterBySource(LS first, RS second, K(T)* select, bool intersection)
    {
        left = move first;
        right = move second;
        selector = select;
        intersect = intersection;
    }

    public Enumerator GetEnumerator()
    {
        LE first = left.GetEnumerator();
        RE second = right.GetEnumerator();
        return new Enumerator(move first, move second, selector, intersect);
    }

    public struct Enumerator : IEnumerator<T>
    {
        private LE left;
        private RE right;
        private HashSet<K> values;
        private K(T)* selector;
        private bool intersect;
        private bool initialized;
        private bool valid;
        private bool finished;

        public Enumerator(LE first, RE second, K(T)* select, bool intersection)
        {
            left = move first;
            right = move second;
            values = new HashSet<K>();
            selector = select;
            intersect = intersection;
            initialized = false;
            valid = false;
            finished = false;
        }

        public bool MoveNext()
        {
            valid = false;
            if (finished) return false;
            finished = true;
            if (!initialized)
            {
                while (right.MoveNext()) values.Add(right.Current);
                initialized = true;
            }
            while (left.MoveNext())
            {
                K key = selector(left.Current);
                bool selected = false;
                if (intersect) selected = values.Remove(&key);
                else selected = values.Add(move key);
                if (selected)
                {
                    valid = true;
                    finished = false;
                    return true;
                }
            }
            return false;
        }

        public T Current
        {
            get
            {
                if (!valid) throw new Exception("Enumerator has no current element");
                return left.Current;
            }
        }
    }
}
