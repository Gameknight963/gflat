namespace std;

private inline void ValidateIntRange(int start, int count)
{
    if (count < 0) throw new Exception("Range count must be nonnegative");
    if (count != 0 && (long)start + (long)count - 1 > 2147483647L)
        throw new Exception("Range exceeds the integer limit");
}

// Integer sequences are values. Enumerating them allocates no storage.
struct IntRange
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
            readonly get
            {
                if (!valid) throw new Exception("Enumerator has no current element");
                return current;
            }
        }
    }

    public int Start { readonly get => first; }
    public int Count { readonly get => count; }
    public inline readonly IntRange::Enumerator GetEnumerator() => new IntRange::Enumerator(first, count);

    public readonly FilteredIntRange Where(bool(int)* predicate)
        => new FilteredIntRange(*this, predicate, 0, this.Count, false);

    public readonly IntRange Take(int count)
    {
        if (count < 0) throw new Exception("Take count must be nonnegative");
        if (count > this.Count) count = this.Count;
        return new IntRange(this.Start, count);
    }

    public readonly IntRange Skip(int count)
    {
        if (count < 0) throw new Exception("Skip count must be nonnegative");
        if (count >= this.Count) return new IntRange(0, 0);
        return new IntRange(this.Start + count, this.Count - count);
    }

    public readonly bool Any() => this.Count != 0;

    public readonly long Sum()
    {
        long count = (long)this.Count;
        return count * (2L * (long)this.Start + count - 1L) / 2L;
    }
}
