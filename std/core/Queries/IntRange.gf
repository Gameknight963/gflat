using std::Collections;
namespace std::Queries;

private inline void ValidateIntRange(int start, int count)
{
    if (count < 0) throw new Exception("Range count must be nonnegative");
    if (count != 0 && (long)start + (long)count - 1 > 2147483647L)
        throw new Exception("Range exceeds the integer limit");
}

// Integer sequences are values. Enumerating them allocates no storage.
struct IntRange : IEnumerable<int, IntRange::Enumerator>
{
    private int first;
    private int count;

    public IntRange(int start, int length)
    {
        ValidateIntRange(start, length);
        first = start;
        count = length;
    }

    struct Enumerator
    {
        private int next;
        private int remaining;
        private int current;
        private bool valid;

        public Enumerator(int start, int count)
        {
            ValidateIntRange(start, count);
            next = start;
            remaining = count;
            current = 0;
            valid = false;
        }

        public inline bool MoveNext()
        {
            valid = false;
            if (remaining == 0) return false;
            current = next;
            remaining--;
            if (remaining != 0) next++;
            valid = true;
            return true;
        }

        public int Current
        {
            immutable get
            {
                if (!valid) throw new Exception("Enumerator has no current element");
                return current;
            }
        }
    }

    public int Start { immutable get => first; }
    public int Count { immutable get => count; }
    public inline immutable IntRange::Enumerator GetEnumerator() => new IntRange::Enumerator(first, count);
    public immutable Enumerable<int, IntRange, IntRange::Enumerator> AsEnumerable()
        => new Enumerable<int, IntRange, IntRange::Enumerator>(*this);

    public immutable FilteredIntRange Where(bool(int)* predicate)
        => new FilteredIntRange(*this, predicate, 0, this.Count, false);

    public immutable IntRange Take(int count)
    {
        if (count < 0) throw new Exception("Take count must be nonnegative");
        if (count > this.Count) count = this.Count;
        return new IntRange(this.Start, count);
    }

    public immutable IntRange Skip(int count)
    {
        if (count < 0) throw new Exception("Skip count must be nonnegative");
        if (count >= this.Count) return new IntRange(0, 0);
        return new IntRange(this.Start + count, this.Count - count);
    }

    public immutable bool Any() => this.Count != 0;

    public immutable long Sum()
    {
        long count = (long)this.Count;
        return count * (2L * (long)this.Start + count - 1L) / 2L;
    }
}
