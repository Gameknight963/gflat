using Standard::Collections;

namespace Standard::Queries;

// Seed one set from the right sequence, then filter distinct left values.
struct SetFilterSource<T : IHashable, LS, LE, RS, RE>
{
    private LS left;
    private RS right;
    private bool intersect;

    public SetFilterSource(LS first, RS second, bool intersection)
    {
        left = move first;
        right = move second;
        intersect = intersection;
    }

    public Enumerator GetEnumerator()
    {
        LE first = left.GetEnumerator();
        RE second = right.GetEnumerator();
        return new Enumerator(move first, move second, intersect);
    }

    public struct Enumerator : IEnumerator<T>
    {
        private LE left;
        private RE right;
        private HashSet<T> values;
        private bool intersect;
        private bool initialized;
        private bool valid;
        private bool finished;

        public Enumerator(LE first, RE second, bool intersection)
        {
            left = move first;
            right = move second;
            values = new HashSet<T>();
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
                bool selected = false;
                if (intersect)
                {
                    T candidate = left.Current;
                    selected = values.Remove(&candidate);
                }
                else selected = values.Add(left.Current);
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
