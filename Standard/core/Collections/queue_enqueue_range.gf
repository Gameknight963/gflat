using Standard::Memory;
using Standard::Queries;

namespace Standard::Collections;

public void EnqueueRange<T>(Queue<T>* target, Span<immutable(T)> values)
{
    if (values.IsEmpty) return;
    nuint stride = (nuint)sizeof(T);
    if (stride == 0) stride = 1;
    nuint maximum = ((nuint)0 - 1) / stride;
    if (values.Length > maximum - target.Count) throw new Exception("Queue count overflow");
    Vector<T> staged = new();
    staged.Reserve(values.Length);
    for (nuint i = 0; i < values.Length; i++) staged.Add(values[i]);
    target.EnqueueStagedRange(move staged);
}

public void EnqueueRange<T, S, E>(Queue<T>* target, Enumerable<T, S, E> values)
{
    nuint stride = (nuint)sizeof(T);
    if (stride == 0) stride = 1;
    nuint remaining = ((nuint)0 - 1) / stride - target.Count;
    Vector<T> staged = new();
    {
        E iterator = values.GetEnumerator();
        while (iterator.MoveNext())
        {
            if (staged.Count == remaining) throw new Exception("Queue count overflow");
            staged.Add(iterator.Current);
        }
    }
    target.EnqueueStagedRange(move staged);
}

extension Queue<T>
{
    public void EnqueueRange(Span<immutable(T)> values)
        => Standard::Collections::EnqueueRange<T>(this, values);

    public void EnqueueRange<S, E>(Enumerable<T, S, E> values)
        => Standard::Collections::EnqueueRange<T, S, E>(this, values);
}
