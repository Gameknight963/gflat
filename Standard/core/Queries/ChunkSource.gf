using Standard::Collections;

namespace Standard::Queries;

struct ChunkSource<T, Source, Iterator>
{
    private Source source;
    private nuint size;

    public ChunkSource(Source input, nuint chunkSize)
    {
        if (chunkSize == 0) throw new Exception("Chunk size must be positive");
        source = move input;
        size = chunkSize;
    }

    public Enumerator GetEnumerator() => new Enumerator(source.GetEnumerator(), size);

    public struct Enumerator : IEnumerator<Group<T>>
    {
        private Iterator iterator;
        private nuint size;
        private Group<T> current;
        private bool valid;
        private bool finished;

        public Enumerator(Iterator input, nuint chunkSize)
        {
            if (chunkSize == 0) throw new Exception("Chunk size must be positive");
            iterator = move input;
            size = chunkSize;
            current = new();
            valid = false;
            finished = false;
        }

        public bool MoveNext()
        {
            valid = false;
            if (finished) return false;
            finished = true;
            current = new Group<T>();
            Group<T> staged = new();
            bool ended = false;
            while (staged.Count < size)
            {
                if (!iterator.MoveNext()) { ended = true; break; }
                staged.Add(iterator.Current);
            }
            if (staged.IsEmpty) return false;
            current = move staged;
            finished = ended;
            valid = true;
            return true;
        }

        public Group<T> Current
        {
            get
            {
                if (!valid) throw new Exception("Enumerator has no current element");
                return current;
            }
        }
    }
}
