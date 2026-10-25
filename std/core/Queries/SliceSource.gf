using std::Collections;
namespace std::Queries;

struct SliceSource<T, Source, Iterator>
{
    private Source source;
    private nuint skip;
    private nuint take;

    public SliceSource(Source input, nuint skipped, nuint taken) { source = move input; skip = skipped; take = taken; }
    public SliceSource<T, Source, Iterator>::Enumerator GetEnumerator()
        => new SliceSource<T, Source, Iterator>::Enumerator(source.GetEnumerator(), skip, take);

    public struct Enumerator : IEnumerator<T>
    {
        private Iterator iterator;
        private nuint skip;
        private nuint take;
        private bool valid;

        public Enumerator(Iterator input, nuint skipped, nuint taken) { iterator = move input; skip = skipped; take = taken; valid = false; }
        public bool MoveNext()
        {
            valid = false;
            if (take == 0) return false;
            while (skip != 0)
            {
                if (!iterator.MoveNext()) { take = 0; return false; }
                skip--;
            }
            if (!iterator.MoveNext()) { take = 0; return false; }
            take--;
            valid = true;
            return true;
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
