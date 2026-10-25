using std::Collections;
namespace std::Queries;

struct DefaultIfEmptySource<T, Source, Iterator>
{
    private Source source;
    private T value;
    public DefaultIfEmptySource(Source input, T fallback)
    { source = move input; value = move fallback; }
    public Enumerator GetEnumerator() => new Enumerator(source.GetEnumerator(), value);

    public struct Enumerator : IEnumerator<T>
    {
        private static const int BeforeFirst = 0;
        private static const int SourceValue = 1;
        private static const int FallbackValue = 2;
        private static const int Finished = 3;
        private Iterator iterator;
        private T value;
        private int state;
        public Enumerator(Iterator input, T fallback)
        { iterator = move input; value = move fallback; state = BeforeFirst; }
        public bool MoveNext()
        {
            int previous = state;
            state = Finished;
            if (previous == Finished || previous == FallbackValue) return false;
            if (iterator.MoveNext()) { state = SourceValue; return true; }
            if (previous == BeforeFirst) { state = FallbackValue; return true; }
            return false;
        }
        public T Current
        {
            get
            {
                if (state == SourceValue) return iterator.Current;
                if (state == FallbackValue) return value;
                throw new Exception("Enumerator has no current element");
            }
        }
    }
}
