namespace Standard::Queries;

private long CheckedAverageAdd(long total, long value)
{
    const long maximum = 9223372036854775807l;
    const long minimum = -9223372036854775807l - 1l;
    if ((value > 0l && total > maximum - value) || (value < 0l && total < minimum - value))
        throw new Exception("Sequence average sum overflow");
    return total + value;
}

public double Average<S, E>(Enumerable<int, S, E> source)
    => source.Average();

extension Enumerable<int, S, E>
{
    public double Average()
    {
        E iterator = (*this).GetEnumerator();
        if (!iterator.MoveNext()) throw new Exception("Sequence is empty");
        long total = iterator.Current;
        long count = 1l;
        while (iterator.MoveNext())
        {
            if (count == 9223372036854775807l) throw new Exception("Sequence average count overflow");
            long current = iterator.Current;
            total = CheckedAverageAdd(total, current);
            count++;
        }
        return (double)((double)total / (double)count);
    }
}

public double Average<T, S, E>(Enumerable<T, S, E> source, int(T)* selector)
    => source.Average(selector);

extension Enumerable<T, S, E>
{
    public double Average(int(T)* selector)
    {
        E iterator = (*this).GetEnumerator();
        if (!iterator.MoveNext()) throw new Exception("Sequence is empty");
        long total = selector(iterator.Current);
        long count = 1l;
        while (iterator.MoveNext())
        {
            if (count == 9223372036854775807l) throw new Exception("Sequence average count overflow");
            long current = selector(iterator.Current);
            total = CheckedAverageAdd(total, current);
            count++;
        }
        return (double)((double)total / (double)count);
    }
}

public double Average<S, E>(Enumerable<long, S, E> source)
    => source.Average();

extension Enumerable<long, S, E>
{
    public double Average()
    {
        E iterator = (*this).GetEnumerator();
        if (!iterator.MoveNext()) throw new Exception("Sequence is empty");
        long total = iterator.Current;
        long count = 1l;
        while (iterator.MoveNext())
        {
            if (count == 9223372036854775807l) throw new Exception("Sequence average count overflow");
            long current = iterator.Current;
            total = CheckedAverageAdd(total, current);
            count++;
        }
        return (double)((double)total / (double)count);
    }
}

public double Average<T, S, E>(Enumerable<T, S, E> source, long(T)* selector)
    => source.Average(selector);

extension Enumerable<T, S, E>
{
    public double Average(long(T)* selector)
    {
        E iterator = (*this).GetEnumerator();
        if (!iterator.MoveNext()) throw new Exception("Sequence is empty");
        long total = selector(iterator.Current);
        long count = 1l;
        while (iterator.MoveNext())
        {
            if (count == 9223372036854775807l) throw new Exception("Sequence average count overflow");
            long current = selector(iterator.Current);
            total = CheckedAverageAdd(total, current);
            count++;
        }
        return (double)((double)total / (double)count);
    }
}

public float Average<S, E>(Enumerable<float, S, E> source)
    => source.Average();

extension Enumerable<float, S, E>
{
    public float Average()
    {
        E iterator = (*this).GetEnumerator();
        if (!iterator.MoveNext()) throw new Exception("Sequence is empty");
        double total = iterator.Current;
        long count = 1l;
        while (iterator.MoveNext())
        {
            if (count == 9223372036854775807l) throw new Exception("Sequence average count overflow");
            double current = iterator.Current;
            total += current;
            count++;
        }
        return (float)((double)total / (double)count);
    }
}

public float Average<T, S, E>(Enumerable<T, S, E> source, float(T)* selector)
    => source.Average(selector);

extension Enumerable<T, S, E>
{
    public float Average(float(T)* selector)
    {
        E iterator = (*this).GetEnumerator();
        if (!iterator.MoveNext()) throw new Exception("Sequence is empty");
        double total = selector(iterator.Current);
        long count = 1l;
        while (iterator.MoveNext())
        {
            if (count == 9223372036854775807l) throw new Exception("Sequence average count overflow");
            double current = selector(iterator.Current);
            total += current;
            count++;
        }
        return (float)((double)total / (double)count);
    }
}

public double Average<S, E>(Enumerable<double, S, E> source)
    => source.Average();

extension Enumerable<double, S, E>
{
    public double Average()
    {
        E iterator = (*this).GetEnumerator();
        if (!iterator.MoveNext()) throw new Exception("Sequence is empty");
        double total = iterator.Current;
        long count = 1l;
        while (iterator.MoveNext())
        {
            if (count == 9223372036854775807l) throw new Exception("Sequence average count overflow");
            double current = iterator.Current;
            total += current;
            count++;
        }
        return (double)((double)total / (double)count);
    }
}

public double Average<T, S, E>(Enumerable<T, S, E> source, double(T)* selector)
    => source.Average(selector);

extension Enumerable<T, S, E>
{
    public double Average(double(T)* selector)
    {
        E iterator = (*this).GetEnumerator();
        if (!iterator.MoveNext()) throw new Exception("Sequence is empty");
        double total = selector(iterator.Current);
        long count = 1l;
        while (iterator.MoveNext())
        {
            if (count == 9223372036854775807l) throw new Exception("Sequence average count overflow");
            double current = selector(iterator.Current);
            total += current;
            count++;
        }
        return (double)((double)total / (double)count);
    }
}
