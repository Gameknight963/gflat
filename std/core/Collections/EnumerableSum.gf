namespace std;

private long CheckedSumAdd(long total, long value)
{
    const long maximum = 9223372036854775807l;
    const long minimum = -9223372036854775807l - 1l;
    if ((value > 0l && total > maximum - value)
        || (value < 0l && total < minimum - value))
        throw new Exception("Sequence sum overflow");
    return total + value;
}

// T must support ordinary implicit value conversion to long.
// Check each signed 64-bit prefix, returning zero for an empty sequence.
public long Sum<T, S, E>(this Enumerable<T, S, E> source)
{
    long total = 0l;
    E iterator = source.GetEnumerator();
    while (iterator.MoveNext())
    {
        long current = iterator.Current;
        total = CheckedSumAdd(total, current);
    }
    return total;
}
