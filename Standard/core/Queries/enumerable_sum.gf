namespace Standard::Queries;

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
public long Sum<T, S, E>(Enumerable<T, S, E> source)
=> source.Sum();

extension Enumerable<T, S, E>
{
    public long Sum()
    {
        long total = 0l;
        E iterator = (*this).GetEnumerator();
        while (iterator.MoveNext())
        {
            long current = iterator.Current;
            total = CheckedSumAdd(total, current);
        }
        return total;
    }
}

public long Sum<T, S, E>(Enumerable<T, S, E> source, int(T)* selector)
    => source.Sum(selector);

extension Enumerable<T, S, E>
{
    public long Sum(int(T)* selector)
    {
        long total = 0l;
        E iterator = (*this).GetEnumerator();
        while (iterator.MoveNext())
        {
            long current = selector(iterator.Current);
            total = CheckedSumAdd(total, current);
        }
        return (long)total;
    }
}

public long Sum<T, S, E>(Enumerable<T, S, E> source, long(T)* selector)
    => source.Sum(selector);

extension Enumerable<T, S, E>
{
    public long Sum(long(T)* selector)
    {
        long total = 0l;
        E iterator = (*this).GetEnumerator();
        while (iterator.MoveNext())
        {
            long current = selector(iterator.Current);
            total = CheckedSumAdd(total, current);
        }
        return (long)total;
    }
}

public float Sum<S, E>(Enumerable<float, S, E> source)
    => source.Sum();

extension Enumerable<float, S, E>
{
    public float Sum()
    {
        double total = 0.0d;
        E iterator = (*this).GetEnumerator();
        while (iterator.MoveNext())
        {
            double current = iterator.Current;
            total += current;
        }
        return (float)total;
    }
}

public float Sum<T, S, E>(Enumerable<T, S, E> source, float(T)* selector)
    => source.Sum(selector);

extension Enumerable<T, S, E>
{
    public float Sum(float(T)* selector)
    {
        double total = 0.0d;
        E iterator = (*this).GetEnumerator();
        while (iterator.MoveNext())
        {
            double current = selector(iterator.Current);
            total += current;
        }
        return (float)total;
    }
}

public double Sum<S, E>(Enumerable<double, S, E> source)
    => source.Sum();

extension Enumerable<double, S, E>
{
    public double Sum()
    {
        double total = 0.0d;
        E iterator = (*this).GetEnumerator();
        while (iterator.MoveNext())
        {
            double current = iterator.Current;
            total += current;
        }
        return (double)total;
    }
}

public double Sum<T, S, E>(Enumerable<T, S, E> source, double(T)* selector)
    => source.Sum(selector);

extension Enumerable<T, S, E>
{
    public double Sum(double(T)* selector)
    {
        double total = 0.0d;
        E iterator = (*this).GetEnumerator();
        while (iterator.MoveNext())
        {
            double current = selector(iterator.Current);
            total += current;
        }
        return (double)total;
    }
}
