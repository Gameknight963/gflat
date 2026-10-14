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

    public int Start { readonly get => first; }
    public int Count { readonly get => count; }
    public inline readonly IntRangeEnumerator GetEnumerator() => new IntRangeEnumerator(first, count);
}

struct IntRangeEnumerator
{
    private int next;
    private int remaining;
    private int current;
    private bool valid;

    public IntRangeEnumerator(int start, int count)
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

    public inline readonly FilteredIntRangeEnumerator GetEnumerator()
        => new FilteredIntRangeEnumerator(source.GetEnumerator(), predicate, skip, take);

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
}

struct FilteredIntRangeEnumerator
{
    private IntRangeEnumerator source;
    private bool(int)* predicate;
    private int skip;
    private int take;
    private int current;
    private bool valid;

    public FilteredIntRangeEnumerator(IntRangeEnumerator input, bool(int)* test, int skipCount, int takeCount)
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

IntRange Range(int start, int count) => new IntRange(start, count);

FilteredIntRange Where(this IntRange source, bool(int)* predicate)
    => new FilteredIntRange(source, predicate, 0, source.Count, false);

IntRange Take(this IntRange source, int count)
{
    if (count < 0) throw new Exception("Take count must be nonnegative");
    if (count > source.Count) count = source.Count;
    return new IntRange(source.Start, count);
}

IntRange Skip(this IntRange source, int count)
{
    if (count < 0) throw new Exception("Skip count must be nonnegative");
    if (count >= source.Count) return new IntRange(0, 0);
    return new IntRange(source.Start + count, source.Count - count);
}

int Count(this IntRange source) => source.Count;
int Count(this FilteredIntRange source)
{
    int count = 0;
    foreach (int value in source) count++;
    return count;
}

bool Any(this IntRange source) => source.Count != 0;
bool Any(this FilteredIntRange source)
{
    FilteredIntRangeEnumerator iterator = source.GetEnumerator();
    return iterator.MoveNext();
}

// Widen the sum: every valid IntRange fits within a signed 64-bit total.
long Sum(this IntRange source)
{
    long count = (long)source.Count;
    return count * (2L * (long)source.Start + count - 1L) / 2L;
}

long Sum(this FilteredIntRange source)
{
    long sum = 0;
    foreach (int value in source) sum += (long)value;
    return sum;
}
