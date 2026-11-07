using Standard::Queries;

namespace Standard::Collections;

public void AddRange<T, S, E>(ref Vector<T> target, Enumerable<T, S, E> source)
{
    nuint stride = (nuint)sizeof(T);
    if (stride == 0) stride = 1;
    nuint remaining = ((nuint)0 - 1) / stride - target.Count;
    Vector<T> staged = new();
    {
        E iterator = source.GetEnumerator();
        while (iterator.MoveNext())
        {
            if (staged.Count == remaining) throw new Exception("Vector count overflow");
            staged.Add(iterator.Current);
        }
    }
    target.AppendStagedRange(move staged);
}

extension Vector<T>
{
    public void AddRange<S, E>(Enumerable<T, S, E> source)
        => Standard::Collections::AddRange<T, S, E>(ref *this, source);
}
