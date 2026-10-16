namespace std;

struct FilteredIntRange
{
    private IntRange source;
    private bool(int)* predicate;
    private int skip;
    private int take;
    // The source's count is a bound, not a Take operation. Skipping an entire
    // filtered source still evaluates its predicate while consuming input.
    private bool explicitLimit;

    public FilteredIntRange(IntRange range, bool(int)* test, int skipCount, int takeCount, bool limited = true)
    {
        if (skipCount < 0 || takeCount < 0) throw new Exception("Sequence counts must be nonnegative");
        source = range;
        predicate = test;
        skip = skipCount;
        take = takeCount;
        explicitLimit = limited;
    }

    struct Enumerator
    {
        private IntRange::Enumerator source;
        private bool(int)* predicate;
        private int skip;
        private int take;
        private int current;
        private bool valid;

        public Enumerator(IntRange::Enumerator input, bool(int)* test, int skipCount, int takeCount)
        {
            if (skipCount < 0 || takeCount < 0) throw new Exception("Sequence counts must be nonnegative");
            source = input;
            predicate = test;
            skip = skipCount;
            take = takeCount;
            current = 0;
            valid = false;
        }

        public inline bool MoveNext()
        {
            valid = false;
            if (take == 0) return false;
            while (source.MoveNext())
            {
                int value = source.Current;
                if (!predicate(value)) continue;
                if (skip != 0) { skip--; continue; }
                current = value;
                take--;
                valid = true;
                return true;
            }
            return false;
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

    public inline readonly FilteredIntRange::Enumerator GetEnumerator()
        => new FilteredIntRange::Enumerator(source.GetEnumerator(), predicate, skip, take);

    public readonly FilteredIntRange Take(int count)
    {
        if (count < 0) throw new Exception("Take count must be nonnegative");
        if (explicitLimit && count > take) count = take;
        return new FilteredIntRange(source, predicate, skip, count);
    }

    public readonly FilteredIntRange Skip(int count)
    {
        if (count < 0) throw new Exception("Skip count must be nonnegative");
        if (explicitLimit && count > take) count = take;
        long combined = (long)skip + (long)count;
        int skipped = 2147483647;
        if (combined < 2147483647L) skipped = (int)combined;
        int remaining = take;
        if (explicitLimit) remaining -= count;
        return new FilteredIntRange(source, predicate, skipped, remaining, explicitLimit);
    }
    
    public readonly int Count()
    {
        int count = 0;
        foreach (int value in *this) count++;
        return count;
    }

    public readonly bool Any()
    {
        FilteredIntRange::Enumerator iterator = this.GetEnumerator();
        return iterator.MoveNext();
    }
    public readonly long Sum()
    {
        long sum = 0;
        foreach (int value in *this) sum += (long)value;
        return sum;
    }
}
