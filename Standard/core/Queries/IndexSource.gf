using Standard::Collections;

namespace Standard::Queries;

struct IndexSource<T, Source, Iterator>
{
    private Source source;

    public IndexSource(Source input)
    {
        source = move input;
    }
    public Enumerator GetEnumerator() => new Enumerator(source.GetEnumerator());

    public struct Enumerator : IEnumerator<Indexed<T>>
    {
        private Iterator iterator;
        private nuint index;
        private bool started;
        private bool valid;
        private bool finished;

        public Enumerator(Iterator input)
        {
            iterator = move input;
            index = 0;
            started = false;
            valid = false;
            finished = false;
        }

        public bool MoveNext()
        {
            valid = false;
            if (finished) return false;
            finished = true;
            if (!iterator.MoveNext()) return false;
            if (started)
            {
                if (index == (nuint)0 - (nuint)1) throw new Exception("Sequence index overflow");
                index++;
            }
            else started = true;
            valid = true;
            finished = false;
            return true;
        }

        public Indexed<T> Current
        {
            get
            {
                if (!valid) throw new Exception("Enumerator has no current element");
                return new Indexed<T>(index, iterator.Current);
            }
        }
    }
}
