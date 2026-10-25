using std::Collections;
namespace std::Queries;

struct EndpointSource<T, Source, Iterator>
{
    private Source source;
    private T value;
    private bool prepend;
    public EndpointSource(Source input, T endpoint, bool first)
    {
        source = move input; value = move endpoint; prepend = first;
    }
    public Enumerator GetEnumerator() => new Enumerator(source.GetEnumerator(), value, prepend);

    public struct Enumerator : IEnumerator<T>
    {
        private static const int BeforeFirst = 0;
        private static const int SourceValue = 1;
        private static const int EndpointValue = 2;
        private static const int Finished = 3;
        private Iterator iterator;
        private T value;
        private bool prepend;
        private int state;
        public Enumerator(Iterator input, T endpoint, bool first)
        {
            iterator = move input; value = move endpoint; prepend = first;
            state = BeforeFirst;
        }
        public bool MoveNext()
        {
            int previous = state;
            state = Finished;
            if (previous == Finished) return false;
            if (prepend && previous == BeforeFirst)
            {
                state = EndpointValue; return true;
            }
            if (!prepend && previous == EndpointValue) return false;
            if (iterator.MoveNext())
            {
                state = SourceValue; return true;
            }
            if (!prepend)
            {
                state = EndpointValue; return true;
            }
            return false;
        }
        public T Current
        {
            get
            {
                if (state == SourceValue) return iterator.Current;
                if (state == EndpointValue) return value;
                throw new Exception("Enumerator has no current element");
            }
        }
    }
}
