using std::Collections;
namespace std::Queries;

// The reusable source owns no seen-key set; each enumerator creates its own.
struct DistinctSource<T : IHashable, Source, Iterator>
{
    private Source source;

    public DistinctSource(Source input) { source = move input; }

    public Enumerator GetEnumerator() => new Enumerator(source.GetEnumerator());

    public struct Enumerator : IEnumerator<T>
    {
        private Iterator iterator;
        private HashSet<T> seen;
        private bool valid;
        private bool finished;

        public Enumerator(Iterator input)
        {
            iterator = move input;
            seen = new HashSet<T>();
            valid = false;
            finished = false;
        }

        public bool MoveNext()
        {
            valid = false;
            if (finished) return false;
            while (iterator.MoveNext())
            {
                if (seen.Add(iterator.Current)) { valid = true; return true; }
            }
            finished = true;
            return false;
        }

        public T Current
        {
            get
            {
                if (!valid) throw new Exception("Enumerator has no current element");
                return iterator.Current;
            }
        }
    }
}
